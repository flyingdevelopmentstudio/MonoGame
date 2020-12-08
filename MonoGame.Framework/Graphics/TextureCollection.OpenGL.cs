// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

#if MONOMAC
using MonoMac.OpenGL;
#elif WINDOWS || LINUX
using OpenTK.Graphics.OpenGL;
#elif GLES
using OpenTK.Graphics.ES30;
#endif

namespace Microsoft.Xna.Framework.Graphics
{
    public sealed partial class TextureCollection
    {
        private TextureTarget[] _targets;

        void PlatformInit()
        {
            _targets = new TextureTarget[_textures.Length];
        }

        void PlatformClear()
        {
            for (var i = 0; i < _targets.Length; i++)
                _targets[i] = 0;
        }

        void PlatformSetTextures(GraphicsDevice device)
        {
            // Skip out if nothing has changed.
            if (_dirty == 0)
                return;

            for (var i = 0; i < _textures.Length; i++)
            {
                var mask = 1 << i;
                if ((_dirty & mask) == 0)
                    continue;

                var tex = _textures[i];
                var glTexture = tex != null ? tex.glTexture : -1;
                if (glTexture > 0)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + i);
                    GraphicsExtensions.CheckGLError();

                    var glTarget = tex.glTarget;
                    _targets[i] = glTarget;
                    GL.BindTexture(glTarget, glTexture);
                    GraphicsExtensions.CheckGLError();

                    // Generate mipmaps for rendertargets when they are being used as textures (instead of everytime they are rendered to)
                    int minLevelDirty = 0;
                    int maxLevelDirty = 0;
                    if (tex is RenderTargetCube)
                    {
                        var renderTarget = tex as RenderTargetCube;
                        minLevelDirty = renderTarget.minLevelDirty;
                        maxLevelDirty = renderTarget.maxLevelDirty;
                        renderTarget.minLevelDirty = 0;
                        renderTarget.maxLevelDirty = 0;
                    }
                    else if (tex is RenderTarget2D)
                    {
                        var renderTarget = tex as RenderTarget2D;
                        minLevelDirty = renderTarget.minLevelDirty;
                        maxLevelDirty = renderTarget.maxLevelDirty;
                        renderTarget.minLevelDirty = 0;
                        renderTarget.maxLevelDirty = 0;
                    }

                    if (maxLevelDirty > minLevelDirty)
                    {
                        GL.TexParameter(glTarget, TextureParameterName.TextureBaseLevel, minLevelDirty);
                        GraphicsExtensions.CheckGLError();

                        GL.TexParameter(glTarget, TextureParameterName.TextureMaxLevel, maxLevelDirty);
                        GraphicsExtensions.CheckGLError();

#if GLES
                        GL.GenerateMipmap(glTarget);
                        GraphicsExtensions.CheckGLError();
#else
                        GL.GenerateMipmap((GenerateMipmapTarget)glTarget);
                        GraphicsExtensions.CheckGLError();
#endif
                    }
                }

                _dirty &= ~mask;
                if (_dirty == 0)
                    break;
            }

            _dirty = 0;
        }
    }
}
