﻿// MonoGame - Copyright (C) The MonoGame Team
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

                GL.ActiveTexture(TextureUnit.Texture0 + i);
                GraphicsExtensions.CheckGLError();

                // Clear the previous binding if the 
                // target is different from the new one.
                if (_targets[i] != 0 && (tex == null || _targets[i] != tex.glTarget))
                {
                    GL.BindTexture(_targets[i], 0);
                    _targets[i] = 0;
                    GraphicsExtensions.CheckGLError();
                }

                if (tex != null && tex.glTexture != -1)
                {
                    _targets[i] = tex.glTarget;
                    GL.BindTexture(tex.glTarget, tex.glTexture);
                    GraphicsExtensions.CheckGLError();

                    // Generate mipmaps for rendertargets when they are being used as textures (instead of everytime they are rendered to)
                    var renderTarget = tex as RenderTarget2D;
                    if (renderTarget != null && renderTarget.mipmapsDirty)
                    {
#if GLES
                        GL.GenerateMipmap(renderTarget.glTarget);
#else
                        GL.GenerateMipmap((GenerateMipmapTarget)renderTarget.glTarget);
#endif
                        renderTarget.mipmapsDirty = false;
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
