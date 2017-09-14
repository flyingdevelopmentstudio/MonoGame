// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
using System;
using System.IO;
using System.Runtime.InteropServices;

#if OPENGL
#if MONOMAC
using MonoMac.OpenGL;
#elif WINDOWS || LINUX
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
#elif GLES
#if ANGLE
using OpenTK.Graphics;
#endif
using OpenTK.Graphics.ES30;
#endif
#endif

namespace Microsoft.Xna.Framework.Graphics
{
	public partial class Texture3D : Texture
	{
        private void GenerateGLTextureIfRequired()
        {
            if (this.glTexture < 0)
            {
                GL.GenTextures(1, out this.glTexture);
                GraphicsExtensions.CheckGLError();

                // For best compatibility and to keep the default wrap mode of XNA, only set ClampToEdge if either
                // dimension is not a power of two.
                var wrap = TextureWrapMode.Repeat;
                if (((_width & (_width - 1)) != 0) || ((_height & (_height - 1)) != 0) || ((_depth & (_depth - 1)) != 0))
                    wrap = TextureWrapMode.ClampToEdge;

                GL.BindTexture(TextureTarget.Texture3D, this.glTexture);
                GraphicsExtensions.CheckGLError();

                GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (_levelCount > 1) ? (int)TextureMinFilter.LinearMipmapLinear : (int)TextureMinFilter.Linear);
                GraphicsExtensions.CheckGLError();

                GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GraphicsExtensions.CheckGLError();

                GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)wrap);
                GraphicsExtensions.CheckGLError();

                GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)wrap);
                GraphicsExtensions.CheckGLError();

                GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)wrap);
                GraphicsExtensions.CheckGLError();
            }
        }

        private void PlatformConstruct(GraphicsDevice graphicsDevice, int width, int height, int depth, bool mipMap, SurfaceFormat format, bool renderTarget)
        {
            this.glTarget = TextureTarget.Texture3D;
            this.glLastSamplerStates = new SamplerState[GraphicsDevice.MaxTextureSlots];

            Threading.BlockOnUIThread(() =>
            {
                GenerateGLTextureIfRequired();

                format.GetGLFormat(out glInternalFormat, out glFormat, out glType);

#if GLES
            GL.TexImage3D((All)glTarget, 0, (int)glInternalFormat, width, height, depth, 0, (All)glFormat, (All)glType, IntPtr.Zero);
#else
            GL.TexImage3D(glTarget, 0, glInternalFormat, width, height, depth, 0, glFormat, glType, IntPtr.Zero);
#endif
            GraphicsExtensions.CheckGLError();

            if (mipMap)
                throw new NotImplementedException("Texture3D does not yet support mipmaps.");
            });
        }

        private void PlatformSetData<T>(int level,
                                     int left, int top, int right, int bottom, int front, int back,
                                     T[] data, int startIndex, int elementCount, int width, int height, int depth)
        {
            Threading.BlockOnUIThread(() =>
            {

                var elementSizeInByte = Marshal.SizeOf(typeof(T));
                var dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);

                try
                {
                    var dataPtr = (IntPtr)(dataHandle.AddrOfPinnedObject().ToInt64() + startIndex * elementSizeInByte);

                    GL.BindTexture(glTarget, glTexture);
                    GraphicsExtensions.CheckGLError();

                    GL.PixelStore(PixelStoreParameter.UnpackAlignment, GraphicsExtensions.GetSize(this.Format));
                    GraphicsExtensions.CheckGLError();
#if GLES
            GL.TexSubImage3D((All)glTarget, level, left, top, front, width, height, depth, (All)glFormat, (All)glType, dataPtr);
#else
                    GL.TexSubImage3D(glTarget, level, left, top, front, width, height, depth, glFormat, glType, dataPtr);
#endif
                    GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                    GraphicsExtensions.CheckGLError();
                }
                finally
                {
                    dataHandle.Free();
                }
            });
        }

        private void PlatformGetData<T>(int level, int left, int top, int right, int bottom, int front, int back, T[] data, int startIndex, int elementCount)
             where T : struct
        {

            throw new NotImplementedException();
        }
	}
}

