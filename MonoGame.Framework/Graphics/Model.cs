using System;
using System.Collections.Generic;
using System.Deployment.Internal;
using System.Linq;

namespace Microsoft.Xna.Framework.Graphics
{
    public class Model
	{
		private static Matrix[] sharedDrawBoneMatrices;
		
		private GraphicsDevice graphicsDevice;
        private GraphicsDevice GraphicsDevice { get { return this.graphicsDevice; } }

		// Summary:
		//     Gets a collection of ModelBone objects which describe how each mesh in the
		//     Meshes collection for this model relates to its parent mesh.
		public ModelBoneCollection Bones { get; private set; }
		//
		// Summary:
		//     Gets a collection of ModelMesh objects which compose the model. Each ModelMesh
		//     in a model may be moved independently and may be composed of multiple materials
		//     identified as ModelMeshPart objects.
		public List<ModelMesh> Meshes { get; private set; }
		//
		// Summary:
		//     Gets the root bone for this model.
		public ModelBone Root { get; set; }
		//
		// Summary:
		//     Gets or sets an object identifying this model.
		public object Tag { get; set; }

		public Model()
		{

		}

		public Model(GraphicsDevice graphicsDevice, List<ModelBone> bones, List<ModelMesh> meshes)
		{
			// TODO: Complete member initialization
			this.graphicsDevice = graphicsDevice;

			Bones = new ModelBoneCollection(bones);
			Meshes = new List<ModelMesh>(meshes);
		}

        public struct MeshPart
        {
            public ModelMeshPart mgPart;

            public override int GetHashCode()
            {
                const uint hash = 0x9e3779b9;
                var seed = mgPart.IndexBuffer.GetHashCode() + hash;
                seed ^= mgPart.VertexBuffer.GetHashCode() + hash + (seed << 6) + (seed >> 2);
                return (int)seed;
            }
            public override bool Equals(object obj)
            {
                return obj is MeshPart other && (mgPart.IndexBuffer.GetHashCode() == other.mgPart.IndexBuffer.GetHashCode() && mgPart.VertexBuffer.GetHashCode() == other.mgPart.VertexBuffer.GetHashCode());
            }
        }

        private enum IndexConversion
        {
            None,
            From16To16,
            From16To32,
            From32To16,
            From32To32,
        };
        public void RebuildIndexBuffers()
        {
            Dictionary<MeshPart, List<ModelMeshPart>> ibMeshParts = new Dictionary<MeshPart, List<ModelMeshPart>>();
            var meshCount = this.Meshes.Count;
            for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                var mesh = this.Meshes[meshIndex];
                var partCount = mesh.MeshParts.Count;
                for (var partIndex = 0; partIndex < partCount; partIndex++)
                {
                    var mgPart = mesh.MeshParts[partIndex];
                    var ibCount = ibMeshParts.Count;
                    List<ModelMeshPart> meshParts;
                    var key = new MeshPart { mgPart = mgPart };
                    if (!ibMeshParts.TryGetValue(key, out meshParts))
                    {
                        meshParts = new List<ModelMeshPart>();
                        ibMeshParts[key] = meshParts;
                    }
                    meshParts.Add(mgPart);
                }
            }

            Dictionary<IndexBuffer, bool> disposable = new Dictionary<IndexBuffer, bool>();

            foreach (var kv in ibMeshParts)
            {
                var parts = kv.Value;
                var partCount = parts.Count;

                if (partCount > 0)
                {
                    var vertexBuffer = parts[0].VertexBuffer;
                    var vertexData = vertexBuffer._data;
                    var vertexDeclaration = vertexBuffer.VertexDeclaration;
                    var vertexStride = vertexDeclaration.VertexStride;
                    var vertexElements = vertexDeclaration.GetVertexElements();
                    var vertexElementCount = vertexElements.Length;
                    int positionOffset = 0;
                    int positionCount = 0;
                    int positionSize = 0;

                    for (int i = 0; i < vertexElementCount; i++)
                    {
                        var v = vertexElements[i];
                        if (v.VertexElementUsage == VertexElementUsage.Position)
                        {
                            positionOffset = v.Offset;
                            switch (v.VertexElementFormat)
                            {
                                case VertexElementFormat.Vector3:
                                    {
                                        positionCount = 3;
                                        positionSize = 4;
                                        break;
                                    }
                                default:
                                    throw new InvalidOperationException("unhandled position format in vertex buffer");
                            }
                            break;
                        }
                    }

                    var indexBuffer = parts[0].IndexBuffer;
                    var data = indexBuffer._data;
                    var size = data.Length;

                    var newIndexBuffer = indexBuffer;
                    var newData = data;
                    var newSize = size;
                    var newFormat = indexBuffer.IndexElementSize;

                    if (vertexBuffer.VertexCount >= UInt16.MaxValue && indexBuffer.IndexElementSize == IndexElementSize.SixteenBits)
                    {
                        newSize = size * 2;
                        newFormat = IndexElementSize.ThirtyTwoBits;
                    }
                    else if (vertexBuffer.VertexCount < UInt16.MaxValue && indexBuffer.IndexElementSize == IndexElementSize.ThirtyTwoBits)
                    {
                        newSize = size / 2;
                        newFormat = IndexElementSize.SixteenBits;
                    }

                    if (newSize != size)
                    {
                        newData = new byte[newSize];
                        newIndexBuffer = new IndexBuffer(indexBuffer.GraphicsDevice, newFormat, indexBuffer.IndexCount, indexBuffer.BufferUsage);
                        bool todispose;
                        if (!disposable.TryGetValue(indexBuffer, out todispose))
                        {
                            disposable[indexBuffer] = true;
                        }
                    }
                    else
                    {
                        disposable[indexBuffer] = false;
                    }

                    bool old32 = indexBuffer.IndexElementSize == IndexElementSize.ThirtyTwoBits;
                    var new32 = newIndexBuffer.IndexElementSize == IndexElementSize.ThirtyTwoBits;
                    IndexConversion indexConversion = IndexConversion.None;
                    if (!old32 && !new32)
                        indexConversion = IndexConversion.From16To16;
                    else if (!old32 && new32)
                        indexConversion = IndexConversion.From16To32;
                    else if (old32 && !new32)
                        indexConversion = IndexConversion.From32To16;
                    else if (old32 && new32)
                        indexConversion = IndexConversion.From32To32;

                    double[] partMin = new double[3];
                    double[] partMax = new double[3];
                    for (var partIndex = 0; partIndex < partCount; partIndex++)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            partMin[i] = double.MaxValue;
                            partMax[i] = double.MinValue;
                        }

                        var part = parts[partIndex];
                        var startIndex = part.StartIndex;
                        var vertexOffset = part.VertexOffset;
                        part.VertexOffset = 0;
                        part.IndexBuffer = newIndexBuffer;
                        var primitiveCount = part.PrimitiveCount;
                        var indexCount = primitiveCount * 3;

                        switch (indexConversion)
                        {
                            case IndexConversion.From16To16:
                                {
                                    for (var index = 0; index < indexCount; index++)
                                    {
                                        var offset = (startIndex + index) * 2;
                                        var b0 = data[offset];
                                        var b1 = data[offset + 1];
                                        int newIndex = (b1 << 8 | b0) + vertexOffset;
                                        newData[offset] = (byte)(newIndex & 0xFF);
                                        newData[offset + 1] = (byte)((newIndex >> 8) & 0xFF);
                                        var vertexPositionOffset = newIndex * vertexStride + positionOffset;
                                        for (int i = 0; i < 3; i++)
                                        {
                                            double f = (double)BitConverter.ToSingle(vertexData, vertexPositionOffset + 4 * i);
                                            if (f < partMin[i]) partMin[i] = f;
                                            if (f > partMax[i]) partMax[i] = f;
                                        }
                                    }
                                    break;
                                }
                            case IndexConversion.From16To32:
                                {
                                    for (var index = 0; index < indexCount; index++)
                                    {
                                        var offset = (startIndex + index) * 2;
                                        var b0 = data[offset];
                                        var b1 = data[offset + 1];
                                        int newIndex = (b1 << 8 | b0) + vertexOffset;
                                        var newOffset = (startIndex + index) * 4;
                                        newData[newOffset] = (byte)(newIndex & 0xFF);
                                        newData[newOffset + 1] = (byte)((newIndex >> 8) & 0xFF);
                                        newData[newOffset + 2] = (byte)((newIndex >> 16) & 0xFF);
                                        newData[newOffset + 3] = (byte)((newIndex >> 24) & 0xFF);
                                        var vertexPositionOffset = newIndex * vertexStride + positionOffset;
                                        for (int i = 0; i < 3; i++)
                                        {
                                            double f = (double)BitConverter.ToSingle(vertexData, vertexPositionOffset + 4 * i);
                                            if (f < partMin[i]) partMin[i] = f;
                                            if (f > partMax[i]) partMax[i] = f;
                                        }
                                    }

                                    break;
                                }
                            case IndexConversion.From32To16:
                                {
                                    for (var index = 0; index < indexCount; index++)
                                    {
                                        var offset = (startIndex + index) * 4;
                                        var b0 = data[offset];
                                        var b1 = data[offset + 1];
                                        var b2 = data[offset + 2];
                                        var b3 = data[offset + 3];
                                        int newIndex = (b3 << 24 | b2 << 16 | b1 << 8 | b0) + vertexOffset;
                                        var newOffset = (startIndex + index) * 2;
                                        newData[newOffset] = (byte)(newIndex & 0xFF);
                                        newData[newOffset + 1] = (byte)((newIndex >> 8) & 0xFF);
                                        var vertexPositionOffset = newIndex * vertexStride + positionOffset;
                                        for (int i = 0; i < 3; i++)
                                        {
                                            double f = (double)BitConverter.ToSingle(vertexData, vertexPositionOffset + 4 * i);
                                            if (f < partMin[i]) partMin[i] = f;
                                            if (f > partMax[i]) partMax[i] = f;
                                        }
                                    }
                                    break;
                                }
                            case IndexConversion.From32To32:
                                {
                                    for (var index = 0; index < indexCount; index++)
                                    {
                                        var newOffset = (startIndex + index) * 4;
                                        var b0 = data[newOffset];
                                        var b1 = data[newOffset + 1];
                                        var b2 = data[newOffset + 2];
                                        var b3 = data[newOffset + 3];
                                        int newIndex = (b3 << 24 | b2 << 16 | b1 << 8 | b0) + vertexOffset;
                                        newData[newOffset] = (byte)(newIndex & 0xFF);
                                        newData[newOffset + 1] = (byte)((newIndex >> 8) & 0xFF);
                                        newData[newOffset + 2] = (byte)((newIndex >> 16) & 0xFF);
                                        newData[newOffset + 3] = (byte)((newIndex >> 24) & 0xFF);
                                        var vertexPositionOffset = newIndex * vertexStride + positionOffset;
                                        for (int i = 0; i < 3; i++)
                                        {
                                            double f = (double)BitConverter.ToSingle(vertexData, vertexPositionOffset + 4 * i);
                                            if (f < partMin[i]) partMin[i] = f;
                                            if (f > partMax[i]) partMax[i] = f;
                                        }
                                    }
                                    break;
                                }
                            default:
                                throw new InvalidOperationException("unhandled indices conversion");
                        }

                        double partMinX = partMin[0];
                        double partMinY = partMin[1];
                        double partMinZ = partMin[2];

                        double partMaxX = partMax[0];
                        double partMaxY = partMax[1];
                        double partMaxZ = partMax[2];

                        double partSizeX = partMaxX - partMinX;
                        double partSizeY = partMaxY - partMinY;
                        double partSizeZ = partMaxZ - partMinZ;

                        double partRadiusX = partSizeX * 0.5;
                        double partRadiusY = partSizeY * 0.5;
                        double partRadiusZ = partSizeZ * 0.5;

                        part.Center = new Vector3((float)(partMinX + partRadiusX), (float)(partMinY + partRadiusY), (float)(partMinZ + partRadiusZ));
                        part.SqRadius = partRadiusX * partRadiusX + partRadiusY * partRadiusY + partRadiusZ * partRadiusZ;
                        part.Radius = Math.Sqrt(part.SqRadius);
                        part.Min = new Vector3((float)partMinX, (float)partMinY, (float)partMinZ);
                        part.Max = new Vector3((float)partMaxX, (float)partMaxY, (float)partMaxZ);
                        part.Size = new Vector3((float)partSizeX, (float)partSizeY, (float)partSizeZ);
                        part.HalfSize = new Vector3((float)partRadiusX, (float)partRadiusY, (float)partRadiusZ);
                    }

                    newIndexBuffer.SetData(newData);
                }
            }

            foreach (var kv in ibMeshParts)
            {
                kv.Value[0].IndexBuffer._data = null;
                kv.Value[0].VertexBuffer._data = null;
            }


            foreach (var kv in disposable)
            {
                if (kv.Value == true)
                {
                    kv.Key.Dispose();
                }
                kv.Key._data = null;
            }
        }

        public void BuildHierarchy()
		{
			var globalScale = Matrix.CreateScale(0.01f);
			
			foreach(var node in this.Root.Children)
			{
				BuildHierarchy(node, this.Root.Transform * globalScale, 0);
			}
		}
		
		private void BuildHierarchy(ModelBone node, Matrix parentTransform, int level)
		{
			node.ModelTransform = node.Transform * parentTransform;
			
			foreach (var child in node.Children) 
			{
				BuildHierarchy(child, node.ModelTransform, level + 1);
			}
			
			//string s = string.Empty;
			//
			//for (int i = 0; i < level; i++) 
			//{
			//	s += "\t";
			//}
			//
			//Debug.WriteLine("{0}:{1}", s, node.Name);
		}
		
		public void Draw(Matrix world, Matrix view, Matrix projection) 
		{       
            int boneCount = this.Bones.Count;
			
			if (sharedDrawBoneMatrices == null ||
				sharedDrawBoneMatrices.Length < boneCount)
			{
				sharedDrawBoneMatrices = new Matrix[boneCount];    
			}
			
			// Look up combined bone matrices for the entire model.            
			CopyAbsoluteBoneTransformsTo(sharedDrawBoneMatrices);

            // Draw the model.
            foreach (ModelMesh mesh in Meshes)
            {
                foreach (Effect effect in mesh.Effects)
                {
					IEffectMatrices effectMatricies = effect as IEffectMatrices;
					if (effectMatricies == null) {
						throw new InvalidOperationException();
					}
                    effectMatricies.World = sharedDrawBoneMatrices[mesh.ParentBone.Index] * world;
                    effectMatricies.View = view;
                    effectMatricies.Projection = projection;
                }

                mesh.Draw();
            }
		}
		
		public void CopyAbsoluteBoneTransformsTo(Matrix[] destinationBoneTransforms)
		{
			if (destinationBoneTransforms == null)
				throw new ArgumentNullException("destinationBoneTransforms");
            if (destinationBoneTransforms.Length < this.Bones.Count)
				throw new ArgumentOutOfRangeException("destinationBoneTransforms");
            int count = this.Bones.Count;
			for (int index1 = 0; index1 < count; ++index1)
			{
                ModelBone modelBone = (this.Bones)[index1];
				if (modelBone.Parent == null)
				{
					destinationBoneTransforms[index1] = modelBone.transform;
				}
				else
				{
					int index2 = modelBone.Parent.Index;
					Matrix.Multiply(ref modelBone.transform, ref destinationBoneTransforms[index2], out destinationBoneTransforms[index1]);
				}
			}
		}
	}

	//// Summary:
	////     Represents a 3D model composed of multiple ModelMesh objects which may be
	////     moved independently.
	//public sealed class Model
	//{
	//    // Summary:
	//    //     Gets a collection of ModelBone objects which describe how each mesh in the
	//    //     Meshes collection for this model relates to its parent mesh.
	//    public ModelBoneCollection Bones { get { throw new NotImplementedException(); } }
	//    //
	//    // Summary:
	//    //     Gets a collection of ModelMesh objects which compose the model. Each ModelMesh
	//    //     in a model may be moved independently and may be composed of multiple materials
	//    //     identified as ModelMeshPart objects.
	//    public ModelMeshCollection Meshes { get { throw new NotImplementedException(); } }
	//    //
	//    // Summary:
	//    //     Gets the root bone for this model.
	//    public ModelBone Root { get { throw new NotImplementedException(); } }
	//    //
	//    // Summary:
	//    //     Gets or sets an object identifying this model.
	//    public object Tag { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }

	//    // Summary:
	//    //     Copies a transform of each bone in a model relative to all parent bones of
	//    //     the bone into a given array.
	//    //
	//    // Parameters:
	//    //   destinationBoneTransforms:
	//    //     The array to receive bone transforms.
	//    public void CopyAbsoluteBoneTransformsTo(Matrix[] destinationBoneTransforms) { throw new NotImplementedException(); }
	//    //
	//    // Summary:
	//    //     Copies an array of transforms into each bone in the model.
	//    //
	//    // Parameters:
	//    //   sourceBoneTransforms:
	//    //     An array containing new bone transforms.
	//    public void CopyBoneTransformsFrom(Matrix[] sourceBoneTransforms) { throw new NotImplementedException(); }
	//    //
	//    // Summary:
	//    //     Copies each bone transform relative only to the parent bone of the model
	//    //     to a given array.
	//    //
	//    // Parameters:
	//    //   destinationBoneTransforms:
	//    //     The array to receive bone transforms.
	//    public void CopyBoneTransformsTo(Matrix[] destinationBoneTransforms) { throw new NotImplementedException(); }
	//    //
	//    // Summary:
	//    //     Render a model after applying the matrix transformations.
	//    //
	//    // Parameters:
	//    //   world:
	//    //     A world transformation matrix.
	//    //
	//    //   view:
	//    //     A view transformation matrix.
	//    //
	//    //   projection:
	//    //     A projection transformation matrix.
	//    public void Draw(Matrix world, Matrix view, Matrix projection) { throw new NotImplementedException(); }
	//}
}
