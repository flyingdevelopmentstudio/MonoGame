// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;

#if MONOMAC
using MonoMac.OpenGL;
#elif WINDOWS || LINUX
using OpenTK.Graphics.OpenGL;
#elif GLES
using OpenTK.Graphics.ES20;
#endif

namespace Microsoft.Xna.Framework.Graphics
{
    internal partial class ConstantBuffer
    {
        private ShaderProgram _shaderProgram = null;
        private ShaderLocation _shaderLocation = null;

        /// <summary>
        /// A hash value which can be used to compare constant buffers.
        /// </summary>
        internal int HashKey { get; private set; }

        private void PlatformInitialize()
        {
            var data = new byte[_parameters.Length];
            for (var i = 0; i < _parameters.Length; i++)
            {
                unchecked
                {
                    data[i] = (byte)(_parameters[i] | _offsets[i]);
                }
            }

            HashKey = MonoGame.Utilities.Hash.ComputeHash(data);
        }

        private void PlatformClear()
        {
            // Force the uniform location to be looked up again
            _shaderProgram = null;
        }

        public unsafe void PlatformApply(GraphicsDevice device, ShaderProgram program)
        {
            // NOTE: We assume here the program has 
            // already been set on the device.

            // If the program changed then lookup the
            // uniform again and apply the state.
            if (_shaderProgram != program)
            {
                _shaderLocation = program.GetUniformLocation(_name);
                _shaderProgram = program;
                _dirty = true;
            }

            if (_shaderLocation == null)
                return;

            // If the shader program is the same, the effect may still be different and have different values in the buffer
            // this is not working should be a per location cache.
            //if (!Object.ReferenceEquals(this, _lastConstantBufferApplied))
            //    _dirty = true;
            if (!Object.ReferenceEquals(this, _shaderLocation.lastConstantBufferApplied))
                _dirty = true;

            // If the buffer content hasn't changed then we're
            // done... use the previously set uniform state.
            if (!_dirty)
                return;

            fixed (byte* bytePtr = _buffer)
            {
                // TODO: We need to know the type of buffer float/int/bool
                // and cast this correctly... else it doesn't work as i guess
                // GL is checking the type of the uniform.

                GL.Uniform4(_shaderLocation.location, _buffer.Length / 16, (float*)bytePtr);
                GraphicsExtensions.CheckGLError();
            }

            // Clear the dirty flag.
            _dirty = false;

            // per location cache
            _shaderLocation.lastConstantBufferApplied = this;
        }
    }
}
