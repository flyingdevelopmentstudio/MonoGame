// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;

#if MONOMAC
using MonoMac.OpenGL;
#elif WINDOWS || LINUX
using OpenTK.Graphics.OpenGL;
#else
using OpenTK.Graphics.ES30;
#endif

namespace Microsoft.Xna.Framework.Graphics
{
    public partial class VertexDeclaration
    {
        Dictionary<ShaderProgramKey, VertexDeclarationAttributeInfo> shaderAttributeInfo = new Dictionary<ShaderProgramKey, VertexDeclarationAttributeInfo>();
        ShaderProgramKey programKey;
        static VertexDeclarationAttributeInfo cachedDeclaration = null;
        static Int64 cachedOffset = -1;
        static VertexBuffer cachedVertexBuffer;

		internal void Apply(Shader vertexShader, Shader pixelShader, IntPtr offset)
		{
            VertexDeclarationAttributeInfo attrInfo;
            programKey.vertexKey = vertexShader.HashKey;
            programKey.pixelKey = pixelShader.HashKey;
            if (!shaderAttributeInfo.TryGetValue(programKey, out attrInfo))
            {
                // Get the vertex attribute info and cache it
                attrInfo = new VertexDeclarationAttributeInfo(GraphicsDevice.MaxVertexAttributes);

                foreach (var ve in _elements)
                {
                    var attributeLocation = vertexShader.GetAttribLocation(ve.VertexElementUsage, ve.UsageIndex);
                    // XNA appears to ignore usages it can't find a match for, so we will do the same
                    if (attributeLocation >= 0)
                    {
                        attrInfo.Elements.Add(new VertexDeclarationAttributeInfo.Element()
                        {
                            Offset = ve.Offset,
                            AttributeLocation = attributeLocation,
                            NumberOfElements = ve.VertexElementFormat.OpenGLNumberOfElements(),
                            VertexAttribPointerType = ve.VertexElementFormat.OpenGLVertexAttribPointerType(),
                            Normalized = ve.OpenGLVertexAttribNormalized(),
                        });
                        attrInfo.EnabledAttributes[attributeLocation] = true;
                    }
                }

                attrInfo._hashCode = attrInfo.GetHashCode();
                shaderAttributeInfo.Add(programKey, attrInfo);
            }

            var offsetI64 = offset.ToInt64();
            if (cachedDeclaration == null || cachedDeclaration._hashCode != attrInfo._hashCode || cachedOffset != offsetI64 || cachedVertexBuffer != this.GraphicsDevice._vertexBuffer)
            {
                cachedOffset = offsetI64;
                cachedDeclaration = attrInfo;
                cachedVertexBuffer = this.GraphicsDevice._vertexBuffer;
                // Apply the vertex attribute info
                foreach (var element in attrInfo.Elements)
                {
                    GL.VertexAttribPointer(element.AttributeLocation,
                        element.NumberOfElements,
                        element.VertexAttribPointerType,
                        element.Normalized,
                        this.VertexStride,
                        (IntPtr)(offset.ToInt64() + element.Offset));
                    GraphicsExtensions.CheckGLError();
                }
                GraphicsDevice.SetVertexAttributeArray(attrInfo.EnabledAttributes);
            }
        }

        /// <summary>
        /// Vertex attribute information for a particular shader/vertex declaration combination.
        /// </summary>
        class VertexDeclarationAttributeInfo
        {
            internal bool[] EnabledAttributes;
            internal int _hashCode;

            internal class Element
            {
                public int Offset;
                public int AttributeLocation;
                public int NumberOfElements;
                public VertexAttribPointerType VertexAttribPointerType;
                public bool Normalized;
            }

            internal List<Element> Elements;

            internal VertexDeclarationAttributeInfo(int maxVertexAttributes)
            {
                EnabledAttributes = new bool[maxVertexAttributes];
                Elements = new List<Element>();
            }
        }
    }
}
