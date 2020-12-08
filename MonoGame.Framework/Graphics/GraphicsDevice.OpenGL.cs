// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

#if MONOMAC
using MonoMac.OpenGL;
using GLPrimitiveType = MonoMac.OpenGL.BeginMode;
#endif

#if WINDOWS || LINUX
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using GLPrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;
#endif

#if ANGLE
using OpenTK.Graphics;
#endif

#if GLES
using OpenTK.Graphics.ES30;
using GLPrimitiveType = OpenTK.Graphics.ES30.BeginMode;
#endif


namespace Microsoft.Xna.Framework.Graphics
{
    public partial class GraphicsDevice
    {
#if WINDOWS || LINUX || ANGLE
        internal IGraphicsContext Context { get; private set; }
#endif

#if GLES
        private DrawBufferMode[] _drawBuffers;
#else
        private DrawBuffersEnum[] _drawBuffers;
#endif
        static List<Action> disposeActions = new List<Action>();
        static object disposeActionsLock = new object();

        private readonly ShaderProgramCache _programCache = new ShaderProgramCache();

        private ShaderProgram _shaderProgram = null;

        static readonly float[] _posFixup = new float[4];

        private bool[] _enabledVertexAttributes = null;

        internal FramebufferHelper framebufferHelper;

        internal int glFramebuffer = 0;
        internal int MaxVertexAttributes;        
        internal List<string> _extensions = new List<string>();
        internal int _maxTextureSize = 0;

        // Keeps track of last applied state to avoid redundant OpenGL calls
        internal bool _lastBlendEnable = false;
        internal BlendState _lastBlendState = new BlendState();
        internal DepthStencilState _lastDepthStencilState = new DepthStencilState();
        internal RasterizerState _lastRasterizerState = new RasterizerState();
        private Vector4 _lastClearColor = Vector4.Zero;
        private float _lastClearDepth = 1.0f;
        private int _lastClearStencil = 0;

        internal void SetVertexAttributeArray(bool[] attrs)
        {
            int length = attrs.Length;
            for(int x = 0; x < length; x++)
            {
                bool enabled = _enabledVertexAttributes[x];
                if (attrs[x])
                {
                    if (!enabled)
                    {
                        _enabledVertexAttributes[x] = true;
                        GL.EnableVertexAttribArray(x);
                        GraphicsExtensions.CheckGLError();
                    }
                }
                else if (enabled)
                {
                    _enabledVertexAttributes[x] = false;
                    GL.DisableVertexAttribArray(x);
                    GraphicsExtensions.CheckGLError();
                }
            }
        }

        private void PlatformSetup()
        {
#if WINDOWS || LINUX || ANGLE
            GraphicsMode mode = GraphicsMode.Default;
            var wnd = (Game.Instance.Window as OpenTKGameWindow).Window.WindowInfo;

#if GLES
            // Create an OpenGL ES 2.0 context
            var flags = GraphicsContextFlags.Embedded;
            int major = 2;
            int minor = 0;
#else
            // Create an OpenGL compatibility context
            var flags = GraphicsContextFlags.Default;
            int major = 1;
            int minor = 0;
#endif

            if (Context == null || Context.IsDisposed)
            {
                var color = PresentationParameters.BackBufferFormat.GetColorFormat();
                var depth =
                    PresentationParameters.DepthStencilFormat == DepthFormat.None ? 0 :
                    PresentationParameters.DepthStencilFormat == DepthFormat.Depth16 ? 16 :
                    24;
                var stencil =
                    PresentationParameters.DepthStencilFormat == DepthFormat.Depth24Stencil8 ? 8 :
                    0;

                var samples = 0;
                if (Game.Instance.graphicsDeviceManager.PreferMultiSampling)
                {
                    // Use a default of 4x samples if PreferMultiSampling is enabled
                    // without explicitly setting the desired MultiSampleCount.
                    if (PresentationParameters.MultiSampleCount == 0)
                    {
                        PresentationParameters.MultiSampleCount = 4;
                    }

                    samples = PresentationParameters.MultiSampleCount;
                }

                mode = new GraphicsMode(color, depth, stencil, samples);
                try
                {
                    Context = new GraphicsContext(mode, wnd, major, minor, flags);
                }
                catch (Exception e)
                {
                    Game.Instance.Log("Failed to create OpenGL context, retrying. Error: " +
                        e.ToString());
                    major = 1;
                    minor = 0;
                    flags = GraphicsContextFlags.Default;
                    Context = new GraphicsContext(mode, wnd, major, minor, flags);
                }
            }
            Context.MakeCurrent(wnd);
            (Context as IGraphicsContextInternal).LoadAll();
            Context.SwapInterval = PresentationParameters.PresentationInterval.GetSwapInterval();

            // Provide the graphics context for background loading
            // Note: this context should use the same GraphicsMode,
            // major, minor version and flags parameters as the main
            // context. Otherwise, context sharing will very likely fail.
            if (Threading.BackgroundContext == null)
            {
                Threading.BackgroundContext = new GraphicsContext(mode, wnd, major, minor, flags);
                Threading.WindowInfo = wnd;
                Threading.BackgroundContext.MakeCurrent(null);
            }
            Context.MakeCurrent(wnd);
#endif

            MaxTextureSlots = 16;

            GL.GetInteger(GetPName.MaxTextureImageUnits, out MaxTextureSlots);
            GraphicsExtensions.CheckGLError();

            GL.GetInteger(GetPName.MaxVertexAttribs, out MaxVertexAttributes);
            GraphicsExtensions.CheckGLError();
            
            GL.GetInteger(GetPName.MaxTextureSize, out _maxTextureSize);
            GraphicsExtensions.CheckGLError();

			// Initialize draw buffer attachment array
			int maxDrawBuffers;
			GL.GetInteger(GetPName.MaxDrawBuffers, out maxDrawBuffers);
#if GLES
            _drawBuffers = new DrawBufferMode[maxDrawBuffers];
            for (int i = 0; i < maxDrawBuffers; i++)
                _drawBuffers[i] = DrawBufferMode.ColorAttachment0 + i;
#else
            _drawBuffers = new DrawBuffersEnum[maxDrawBuffers];
            for (int i = 0; i < maxDrawBuffers; i++)
                _drawBuffers[i] = DrawBuffersEnum.ColorAttachment0 + i;
#endif
            _extensions = GetGLExtensions();

            _enabledVertexAttributes = new bool[MaxVertexAttributes];
        }

        List<string> GetGLExtensions()
        {
            // Setup extensions.
            List<string> extensions = new List<string>();
            var extstring = GL.GetString(StringName.Extensions);
            GraphicsExtensions.CheckGLError();
            if (!string.IsNullOrEmpty(extstring))
            {
                extensions.AddRange(extstring.Split(' '));
#if ANDROID
                Android.Util.Log.Debug("MonoGame", "Supported extensions:");
#else
                System.Diagnostics.Debug.WriteLine("Supported extensions:");
#endif
                foreach (string extension in extensions)
#if ANDROID
                    Android.Util.Log.Debug("MonoGame", extension);
#else
                    System.Diagnostics.Debug.WriteLine(extension);
#endif
            }

            return extensions;
        }

        private void PlatformInitialize()
        {
            _viewport = new Viewport(0, 0, PresentationParameters.BackBufferWidth, PresentationParameters.BackBufferHeight);

            // Ensure the vertex attributes are reset
            for (int i = 0; i < _enabledVertexAttributes.Length; ++i)
            { _enabledVertexAttributes[i] = false; }

            // Free all the cached shader programs. 
            _programCache.Clear();
            _shaderProgram = null;

            if (GraphicsCapabilities.SupportsFramebufferObjectARB)
            {
                this.framebufferHelper = new FramebufferHelper(this);
            }
#if !(GLES || MONOMAC)
            else if (GraphicsCapabilities.SupportsFramebufferObjectEXT)
            {
                this.framebufferHelper = new FramebufferHelperEXT(this);
            }
#endif
            else
            {
                throw new PlatformNotSupportedException(
                    "MonoGame requires either ARB_framebuffer_object or EXT_framebuffer_object." +
                    "Try updating your graphics drivers.");
            }

            // Force reseting states
            this.BlendState.PlatformApplyState(this, true);
            this.DepthStencilState.PlatformApplyState(this, true);
            this.RasterizerState.PlatformApplyState(this, true);            
        }

        private RasterizerState clearRasterizerState = RasterizerState.CullCounterClockwise;
        private BlendState clearBlendState = BlendState.Opaque;
        private DepthStencilState clearDepthStencilState = new DepthStencilState { StencilEnable = true };

        public void DrawBuffers(bool color0, bool color1, bool color2, bool color3)
        {
#if GLES
            var none = DrawBufferMode.None;
            var colorAttachement0 = DrawBufferMode.ColorAttachment0;
#else
            var none = DrawBuffersEnum.None;
            var colorAttachement0 = DrawBuffersEnum.ColorAttachment0;
#endif
            this._drawBuffers[0] = color0 ? colorAttachement0 : none;
            this._drawBuffers[1] = (this._currentRenderTargetCount > 1) && color1 ? colorAttachement0 + 1 : none;
            this._drawBuffers[2] = (this._currentRenderTargetCount > 2) && color2 ? colorAttachement0 + 2 : none;
            this._drawBuffers[3] = (this._currentRenderTargetCount > 3) && color3 ? colorAttachement0 + 3 : none;
            GL.DrawBuffers(4, this._drawBuffers);
            GraphicsExtensions.CheckGLError();
        }

        public void PlatformClear(ClearOptions options, Vector4 color, float depth, int stencil)
        {
            // TODO: We need to figure out how to detect if we have a
            // depth stencil buffer or not, and clear options relating
            // to them if not attached.

            // Unlike with XNA and DirectX...  GL.Clear() obeys several
            // different render states:
            //
            //  - The color write flags.
            //  - The scissor rectangle.
            //  - The depth/stencil state.
            //
            // So overwrite these states with what is needed to perform
            // the clear correctly and restore it afterwards.
            //
		    var prevScissorRect = ScissorRectangle;
		    var prevDepthStencilState = DepthStencilState;
            var prevBlendState = BlendState;
            ScissorRectangle = _viewport.Bounds;
            // DepthStencilState.Default has the Stencil Test disabled; 
            // make sure stencil test is enabled before we clear since
            // some drivers won't clear with stencil test disabled
            DepthStencilState = this.clearDepthStencilState;
		    BlendState = BlendState.Opaque;
            PlatformApplyState(false);

            ClearBufferMask bufferMask = 0;
            if ((options & ClearOptions.Target) == ClearOptions.Target)
            {
                if (color != _lastClearColor)
                {
                    GL.ClearColor(color.X, color.Y, color.Z, color.W);
                    GraphicsExtensions.CheckGLError();
                    _lastClearColor = color;
                }
                bufferMask = bufferMask | ClearBufferMask.ColorBufferBit;
            }
			if ((options & ClearOptions.Stencil) == ClearOptions.Stencil)
            {
                if (stencil != _lastClearStencil)
                {
				    GL.ClearStencil(stencil);
                    GraphicsExtensions.CheckGLError();
                    _lastClearStencil = stencil;
                }
                bufferMask = bufferMask | ClearBufferMask.StencilBufferBit;
			}

			if ((options & ClearOptions.DepthBuffer) == ClearOptions.DepthBuffer) 
            {
                if (depth != _lastClearDepth)
                {
#if GLES
                    GL.ClearDepth (depth);
#else
                    GL.ClearDepth((double)depth);
#endif
                    GraphicsExtensions.CheckGLError();
                    _lastClearDepth = depth;
                }
				bufferMask = bufferMask | ClearBufferMask.DepthBufferBit;
			}


			GL.Clear(bufferMask);
            GraphicsExtensions.CheckGLError();
           		
            // Restore the previous render state.
		    ScissorRectangle = prevScissorRect;
		    DepthStencilState = prevDepthStencilState;
		    BlendState = prevBlendState;
        }

        private void PlatformDispose()
        {
            // Free all the cached shader programs.
            _programCache.Dispose();

            GraphicsDevice.AddDisposeAction(() =>
                                            {
#if WINDOWS || LINUX || ANGLE
                Context.Dispose();
                Context = null;

                if (Threading.BackgroundContext != null)
                {
                    Threading.BackgroundContext.Dispose();
                    Threading.BackgroundContext = null;
                    Threading.WindowInfo = null;
                }
#endif
            });
        }

        /// <summary>
        /// Adds a dispose action to the list of pending dispose actions. These are executed at the end of each call to Present().
        /// This allows GL resources to be disposed from other threads, such as the finalizer.
        /// </summary>
        /// <param name="disposeAction">The action to execute for the dispose.</param>
        static private void AddDisposeAction(Action disposeAction)
        {
            if (disposeAction == null)
                throw new ArgumentNullException("disposeAction");
            if (Threading.IsOnUIThread())
            {
                disposeAction();
            }
            else
            {
                lock (disposeActionsLock)
                {
                    disposeActions.Add(disposeAction);
                }
            }
        }

        public void PlatformPresent()
        {
#if WINDOWS || LINUX || ANGLE
            Context.SwapBuffers();
#endif
            GraphicsExtensions.CheckGLError();

            // Dispose of any GL resources that were disposed in another thread
            lock (disposeActionsLock)
            {
                if (disposeActions.Count > 0)
                {
                    foreach (var action in disposeActions)
                        action();
                    disposeActions.Clear();
                }
            }
        }

        private void PlatformSetViewport(ref Viewport value)
        {
            if (IsRenderTargetBound)
                GL.Viewport(value.X, value.Y, value.Width, value.Height);
            else
                GL.Viewport(value.X, PresentationParameters.BackBufferHeight - value.Y - value.Height, value.Width, value.Height);
            GraphicsExtensions.LogGLError("GraphicsDevice.Viewport_set() GL.Viewport");
#if GLES
            GL.DepthRange(value.MinDepth, value.MaxDepth);
#else
            GL.DepthRange((double)value.MinDepth, (double)value.MaxDepth);
#endif
            GraphicsExtensions.LogGLError("GraphicsDevice.Viewport_set() GL.DepthRange");
                
            // In OpenGL we have to re-apply the special "posFixup"
            // vertex shader uniform if the viewport changes.
            _vertexShaderDirty = true;

        }

        private void PlatformApplyDefaultRenderTarget()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, this.glFramebuffer);

            // Reset the raster state because we flip vertices
            // when rendering offscreen and hence the cull direction.
            _rasterizerStateDirty = true;

            // Textures will need to be rebound to render correctly in the new render target.
            Textures.Dirty();
        }

        private class RenderTargetBindingArrayComparer : IEqualityComparer<RenderTargetBinding[]>
        {
            public bool Equals(RenderTargetBinding[] first, RenderTargetBinding[] second)
            {
                if (object.ReferenceEquals(first, second))
                    return true;

                if (first == null || second == null)
                    return false;

                if (first.Length != second.Length)
                    return false;

                for (var i = 0; i < first.Length; ++i)
                {
                    if ((first[i].RenderTarget != second[i].RenderTarget) || (first[i].ArraySlice != second[i].ArraySlice) || (first[i].minLevel != second[i].minLevel))
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(RenderTargetBinding[] array)
            {
                if (array != null)
                {
                    unchecked
                    {
                        int hash = 17;
                        foreach (var item in array)
                        {
                            if (item.RenderTarget != null)
                                hash = hash * 23 + item.RenderTarget.GetHashCode();
                            hash = hash * 23 + item.ArraySlice.GetHashCode();
                        }
                        return hash;
                    }
                }
                return 0;
            }
        }

        // FBO cache, we create 1 FBO per RenderTargetBinding combination
        private Dictionary<RenderTargetBinding[], int> glFramebuffers = new Dictionary<RenderTargetBinding[], int>(new RenderTargetBindingArrayComparer());
        // FBO cache used to resolve MSAA rendertargets, we create 1 FBO per RenderTargetBinding combination
        private Dictionary<RenderTargetBinding[], int> glResolveFramebuffers = new Dictionary<RenderTargetBinding[], int>(new RenderTargetBindingArrayComparer());

        internal void PlatformCreateRenderTarget(Texture renderTarget, int width, int height, bool mipMap, SurfaceFormat preferredFormat, DepthFormat preferredDepthFormat, int preferredMultiSampleCount, RenderTargetUsage usage)
        {
            var color = 0;
            var depthStencil = 0;
            
            if (preferredMultiSampleCount > 0 && this.framebufferHelper.SupportsBlitFramebuffer)
            {
                GL.GenRenderbuffers(1, out color);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, color);

                var colorInternalFormat = All.Rgba8;
                switch (preferredFormat)
                {
                    case SurfaceFormat.Color: colorInternalFormat = All.Rgba8; break;
                    case SurfaceFormat.Bgra4444: colorInternalFormat = All.Rgba4; break;
                    // Formats below are only supported if EXT_color_buffer_half_float extension is present
                    case SurfaceFormat.HalfVector4: colorInternalFormat = All.Rgba16f; break;
                    case SurfaceFormat.HalfVector2: colorInternalFormat = All.Rg16f; break;
                    case SurfaceFormat.HalfSingle: colorInternalFormat = All.R16f; break;
                    default: throw new NotSupportedException();
                }
#if GLES
                GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, preferredMultiSampleCount, (RenderbufferInternalFormat)colorInternalFormat, width, height);
#else
                GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, preferredMultiSampleCount, (RenderbufferStorage)colorInternalFormat, width, height);
#endif
            }

            if (preferredDepthFormat != DepthFormat.None)
            {
                var depthInternalFormat = All.DepthComponent16;
                switch (preferredDepthFormat)
                {
                    case DepthFormat.Depth16: depthInternalFormat = All.DepthComponent16; break;
                    case DepthFormat.Depth24: depthInternalFormat = All.DepthComponent24; break;
                    case DepthFormat.Depth24Stencil8: depthInternalFormat = All.Depth24Stencil8; break;
                }

                if (depthInternalFormat != 0)
                {
                    GL.GenRenderbuffers(1, out depthStencil);
                    GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthStencil);
#if GLES
                    GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, preferredMultiSampleCount, (RenderbufferInternalFormat)depthInternalFormat, width, height);
#else
                    GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, preferredMultiSampleCount, (RenderbufferStorage)depthInternalFormat, width, height);
#endif
                }
            }

            if (renderTarget is RenderTargetCube)
            {
                var renderTargetCube = renderTarget as RenderTargetCube;
                if (color != 0)
                    renderTargetCube.glColorBuffer = color;
                else
                    renderTargetCube.glColorBuffer = renderTargetCube.glTexture;
                renderTargetCube.glDepthStencilBuffer = depthStencil;
            }
            else if (renderTarget is RenderTarget2D)
            {
                var renderTarget2D = renderTarget as RenderTarget2D;
                if (color != 0)
                    renderTarget2D.glColorBuffer = color;
                else
                    renderTarget2D.glColorBuffer = renderTarget2D.glTexture;
                renderTarget2D.glDepthStencilBuffer = depthStencil;
            }
            else
            {
                throw new NotSupportedException(); 
            }
        }

        internal void PlatformDeleteRenderTarget(Texture renderTarget)
        {
            var color = 0;
            var depthStencil = 0;
            var colorIsRenderbuffer = false;

            var renderTarget2D = renderTarget as RenderTarget2D;
            if (renderTarget2D != null)
            {
                color = renderTarget2D.glColorBuffer;
                depthStencil = renderTarget2D.glDepthStencilBuffer;
                colorIsRenderbuffer = color != renderTarget2D.glTexture;
            }

            if (color != 0)
            {
                if (colorIsRenderbuffer)
                    GL.DeleteRenderbuffers(1, ref color);
                if (depthStencil != 0)
                    GL.DeleteRenderbuffers(1, ref depthStencil);

                var bindingsToDelete = new List<RenderTargetBinding[]>();
                foreach (var bindings in this.glFramebuffers.Keys)
                {
                    foreach (var binding in bindings)
                    {
                        if (binding.RenderTarget == renderTarget)
                        {
                            bindingsToDelete.Add(bindings);
                            break;
                        }
                    }
                }

                foreach (var bindings in bindingsToDelete)
                {
                    var fbo = 0;
                    if (this.glFramebuffers.TryGetValue(bindings, out fbo))
                    {
                        GL.DeleteFramebuffers(1, ref fbo);
                        this.glFramebuffers.Remove(bindings);
                    }
                    if (this.glResolveFramebuffers.TryGetValue(bindings, out fbo))
                    {
                        GL.DeleteFramebuffers(1, ref fbo);
                        this.glResolveFramebuffers.Remove(bindings);
                    }
                }
            }
        }

        internal readonly FramebufferAttachment[] GLDepthStencilAttachements = { FramebufferAttachment.DepthAttachment, FramebufferAttachment.StencilAttachment };
        internal readonly FramebufferAttachment[] GLColorAttachements = { FramebufferAttachment.ColorAttachment0, FramebufferAttachment.ColorAttachment1, FramebufferAttachment.ColorAttachment2, FramebufferAttachment.ColorAttachment3 };

#if GLES
        private DrawBufferMode[] GLBlitAttachements = { DrawBufferMode.None, DrawBufferMode.None, DrawBufferMode.None, DrawBufferMode.None };
#else
        private DrawBuffersEnum[] GLBlitAttachements = { DrawBuffersEnum.None, DrawBuffersEnum.None, DrawBuffersEnum.None, DrawBuffersEnum.None };
#endif

        private void PlatformResolveRenderTargets()
        {
            if (this._currentRenderTargetCount == 0)
                return;

            // Always discard depth and stencil
            GL.InvalidateFramebuffer(FramebufferTarget.DrawFramebuffer, 2, GLDepthStencilAttachements);
            GraphicsExtensions.CheckGLError();

            var renderTargetBinding = this._currentRenderTargetBindings[0];
            var renderTarget = renderTargetBinding.RenderTarget as RenderTarget2D;

            if (renderTarget != null)
            {
                if (renderTarget.MultiSampleCount > 0 && this.framebufferHelper.SupportsBlitFramebuffer)
                {
                    var glResolveFramebuffer = 0;
                    if (!this.glResolveFramebuffers.TryGetValue(this._currentRenderTargetBindings, out glResolveFramebuffer))
                    {
                        GL.GenFramebuffers(1, out glResolveFramebuffer);
                        GraphicsExtensions.CheckGLError();
                        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, glResolveFramebuffer);
                        GraphicsExtensions.CheckGLError();
                        for (var i = 0; i < this._currentRenderTargetCount; ++i)
                        {
                            var rt = this._currentRenderTargetBindings[i].RenderTarget;
#if GLES
                        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferSlot.ColorAttachment0 + i, rt.glTarget, rt.glTexture, 0);
#else
                            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, rt.glTarget, rt.glTexture, 0);
#endif
                            GraphicsExtensions.CheckGLError();
                        }
                        this.glResolveFramebuffers.Add((RenderTargetBinding[])this._currentRenderTargetBindings.Clone(), glResolveFramebuffer);
                    }
                    else
                    {
                        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, glResolveFramebuffer);
                        GraphicsExtensions.CheckGLError();
                    }
                    // The only fragment operations which affect the resolve are the pixel ownership test, the scissor test, and dithering.
                    if (this._lastRasterizerState.ScissorTestEnable)
                    {
                        GL.Disable(EnableCap.ScissorTest);
                        GraphicsExtensions.CheckGLError();
                    }

                    for (var i = 0; i < this._currentRenderTargetCount; ++i)
                    {
                        renderTargetBinding = this._currentRenderTargetBindings[i];
                        renderTarget = renderTargetBinding.RenderTarget as RenderTarget2D;
                        var width = renderTarget.Width;
                        var height = renderTarget.Height;
                        GL.ReadBuffer(ReadBufferMode.ColorAttachment0 + i);
                        GraphicsExtensions.CheckGLError();
                        var drawbufferNone = GLBlitAttachements[i];
#if GLES
                    GLBlitAttachements[i] = DrawBufferMode.ColorAttachment0 + i;
#else
                        GLBlitAttachements[i] = DrawBuffersEnum.ColorAttachment0 + i;
#endif
                        GL.DrawBuffers(this._currentRenderTargetCount, GLBlitAttachements);
                        GraphicsExtensions.CheckGLError();
                        GLBlitAttachements[i] = drawbufferNone;
                        BlitFramebufferFilter blitFramebufferFilter = (BlitFramebufferFilter)0;
                        switch (renderTargetBinding.ResolveFilter)
                        {
                            case TextureFilter.Point:
                                blitFramebufferFilter = BlitFramebufferFilter.Nearest;
                                break;
                            case TextureFilter.Linear:
                                blitFramebufferFilter = BlitFramebufferFilter.Linear;
                                break;
                        }
                        GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, blitFramebufferFilter);
                        GraphicsExtensions.CheckGLError();
                    }
                    // Always discard multisampled renderbuffers
                    GL.InvalidateFramebuffer(FramebufferTarget.ReadFramebuffer, 4, GLColorAttachements);
                    GraphicsExtensions.CheckGLError();
                    if (this._lastRasterizerState.ScissorTestEnable)
                    {
                        GL.Enable(EnableCap.ScissorTest);
                        GraphicsExtensions.CheckGLError();
                    }
                }
            }
            for (var i = 0; i < this._currentRenderTargetCount; ++i)
            {
                renderTargetBinding = this._currentRenderTargetBindings[i];
                if (renderTargetBinding.RenderTarget is RenderTargetCube)
                {
                    var renderTargetCube = renderTargetBinding.RenderTarget as RenderTargetCube;
                    renderTargetCube.minLevelDirty = renderTargetBinding.minLevel;
                    renderTargetCube.maxLevelDirty = renderTargetBinding.maxLevel;
                }
                else if (renderTargetBinding.RenderTarget is RenderTarget2D)
                {
                    var renderTarget2D = renderTargetBinding.RenderTarget as RenderTarget2D;
                    renderTarget2D.minLevelDirty = renderTargetBinding.minLevel;
                    renderTarget2D.maxLevelDirty = renderTargetBinding.maxLevel;
                }
            }
        }

        private IRenderTarget PlatformApplyRenderTargets()
        {
            var glFramebuffer = 0;
            if (!this.glFramebuffers.TryGetValue(this._currentRenderTargetBindings, out glFramebuffer))
            {
                GL.GenFramebuffers(1, out glFramebuffer);
                GraphicsExtensions.CheckGLError();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, glFramebuffer);
                GraphicsExtensions.CheckGLError();
                var renderTargetBinding = this._currentRenderTargetBindings[0];
                int glDepthStencilBuffer = 0;
                DepthFormat depthStencilFormat;
                if (renderTargetBinding.RenderTarget is RenderTargetCube)
                {
                    var renderTarget = renderTargetBinding.RenderTarget as RenderTargetCube;
                    glDepthStencilBuffer = renderTarget.glDepthStencilBuffer;
                    depthStencilFormat = renderTarget.DepthStencilFormat;
                }
                else
                {
                    var renderTarget = renderTargetBinding.RenderTarget as RenderTarget2D;
                    glDepthStencilBuffer = renderTarget.glDepthStencilBuffer;
                    depthStencilFormat = renderTarget.DepthStencilFormat;
                }
                if (glDepthStencilBuffer != 0)
                {
#if GLES
                    var depthStencilAttachement = depthStencilFormat == DepthFormat.Depth24Stencil8 ? FramebufferSlot.DepthStencilAttachment : FramebufferSlot.DepthAttachment;
#else
                    var depthStencilAttachement = depthStencilFormat == DepthFormat.Depth24Stencil8 ? FramebufferAttachment.DepthStencilAttachment : FramebufferAttachment.DepthAttachment;
#endif
                    GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, depthStencilAttachement, RenderbufferTarget.Renderbuffer, glDepthStencilBuffer);
                    GraphicsExtensions.CheckGLError();
                }
                for (var i = 0; i < this._currentRenderTargetCount; ++i)
                {
                    renderTargetBinding = this._currentRenderTargetBindings[i];

                    int glColorBuffer;
                    int glTexture;
                    TextureTarget glTarget;

                    if (renderTargetBinding.RenderTarget is RenderTargetCube)
                    {
                        var renderTarget = renderTargetBinding.RenderTarget as RenderTargetCube;
                        glColorBuffer = renderTarget.glColorBuffer;
                        glTexture = renderTarget.glTexture;
                        glTarget = (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX +  renderTargetBinding.ArraySlice);
                    }
                    else
                    {
                        var renderTarget = renderTargetBinding.RenderTarget as RenderTarget2D;
                        glColorBuffer = renderTarget.glColorBuffer;
                        glTexture = renderTarget.glTexture;
                        glTarget = renderTarget.glTarget;
                    }

#if GLES
                    var colorAttachement = FramebufferSlot.ColorAttachment0 + i;
#else
                    var colorAttachement = FramebufferAttachment.ColorAttachment0 + i;
#endif
                    if (glColorBuffer != glTexture)
                    {
                        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, colorAttachement, RenderbufferTarget.Renderbuffer, glColorBuffer);
                        GraphicsExtensions.CheckGLError();
                    }
                    else
                    {
                        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, colorAttachement, glTarget, glTexture, renderTargetBinding.minLevel);
                        GraphicsExtensions.CheckGLError();
                    }
                }
#if DEBUG
                    this.framebufferHelper.CheckFramebufferStatus();
#endif
                this.glFramebuffers.Add((RenderTargetBinding[])_currentRenderTargetBindings.Clone(), glFramebuffer);
            }
            else
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, glFramebuffer);
                GraphicsExtensions.CheckGLError();
            }

            // Reset the drawbuffers mask (can be changed by calling SetRenderTargetMask) to ensure caller Clear() will work
            for (var i = 0; i < this._currentRenderTargetCount; ++i)
            {
#if GLES
                this._drawBuffers[i] = DrawBufferMode.ColorAttachment0 + i;
#else
                this._drawBuffers[i] = DrawBuffersEnum.ColorAttachment0 + i;
#endif
            }
            GL.DrawBuffers(this._currentRenderTargetCount, this._drawBuffers);
            GraphicsExtensions.CheckGLError();

            var prevBlendState = this.BlendState;
            var prevDepthStencilState = this.DepthStencilState;
            var prevRasterizerState = this.RasterizerState;
            this.BlendState = clearBlendState;
            this.DepthStencilState = clearDepthStencilState;
            this.RasterizerState = clearRasterizerState;
            this.PlatformApplyState(false);
            for (var i = 0; i < this._currentRenderTargetCount; ++i)
            {
                var renderTargetBinding = this._currentRenderTargetBindings[i];
                var renderTarget = renderTargetBinding.RenderTarget;

                RenderTargetUsage renderTargetUsage;
                DepthFormat renderTargetDepthFormat;
                if (renderTarget is RenderTargetCube)
                {
                    var renderTargetCube = renderTarget as RenderTargetCube;
                    renderTargetUsage = renderTargetCube.RenderTargetUsage;
                    renderTargetDepthFormat = renderTargetCube.DepthStencilFormat;
                }
                else
                {
                    var renderTarget2D = renderTarget as RenderTarget2D;
                    renderTargetUsage = renderTarget2D.RenderTargetUsage;
                    renderTargetDepthFormat = renderTarget2D.DepthStencilFormat;
                }

                if (renderTargetUsage != RenderTargetUsage.PreserveContents)
                {
                    var color = renderTargetBinding.ClearColor.ToVector4();
                    GL.ClearBuffer(ClearBuffer.Color, i, ref color.X);
                    GraphicsExtensions.CheckGLError();
                    if (renderTargetDepthFormat != DepthFormat.None)
                    {
                        var clearDepth = renderTargetBinding.ClearDepth;
                        if (renderTargetDepthFormat == DepthFormat.Depth24Stencil8)
                        {
                            var clearStencil = renderTargetBinding.ClearStencil;
                            GL.ClearBuffer(ClearBufferCombined.DepthStencil, i, clearDepth, clearStencil);
                            GraphicsExtensions.CheckGLError();
                        }
                        else
                        {
                            GL.ClearBuffer(ClearBuffer.Depth, i, ref clearDepth);
                            GraphicsExtensions.CheckGLError();
                        }
                    }
                }
            }

            this.BlendState = prevBlendState;
            this.DepthStencilState = prevDepthStencilState;
            this.RasterizerState = prevRasterizerState;

            // Reset the raster state because we flip vertices
            // when rendering offscreen and hence the cull direction.
            _rasterizerStateDirty = true;

            return _currentRenderTargetBindings[0].RenderTarget as IRenderTarget;
        }

        private static GLPrimitiveType PrimitiveTypeGL(PrimitiveType primitiveType)
        {
            switch (primitiveType)
            {
                case PrimitiveType.PointList:
                    return GLPrimitiveType.Points;
                case PrimitiveType.LineList:
                    return GLPrimitiveType.Lines;
                case PrimitiveType.LineStrip:
                    return GLPrimitiveType.LineStrip;
                case PrimitiveType.TriangleList:
                    return GLPrimitiveType.Triangles;
                case PrimitiveType.TriangleStrip:
                    return GLPrimitiveType.TriangleStrip;
            }

            throw new ArgumentException();
        }


        /// <summary>
        /// Activates the Current Vertex/Pixel shader pair into a program.         
        /// </summary>
        private void ActivateShaderProgram()
        {
            // Lookup the shader program.
            var shaderProgram = _programCache.GetProgram(VertexShader, PixelShader);
            if (shaderProgram.Program == -1)
                return;
            // Set the new program if it has changed.
            if (_shaderProgram != shaderProgram)
            {
                GL.UseProgram(shaderProgram.Program);
                GraphicsExtensions.CheckGLError();
                _shaderProgram = shaderProgram;
            }

            var posFixupLoc = shaderProgram.GetUniformLocation("posFixup");
            if (posFixupLoc == null)
                return;

            // Apply vertex shader fix:
            // The following two lines are appended to the end of vertex shaders
            // to account for rendering differences between OpenGL and DirectX:
            //
            // gl_Position.y = gl_Position.y * posFixup.y;
            // gl_Position.xy += posFixup.zw * gl_Position.ww;
            //
            // (the following paraphrased from wine, wined3d/state.c and wined3d/glsl_shader.c)
            //
            // - We need to flip along the y-axis in case of offscreen rendering.
            // - D3D coordinates refer to pixel centers while GL coordinates refer
            //   to pixel corners.
            // - D3D has a top-left filling convention. We need to maintain this
            //   even after the y-flip mentioned above.
            // In order to handle the last two points, we translate by
            // (63.0 / 128.0) / VPw and (63.0 / 128.0) / VPh. This is equivalent to
            // translating slightly less than half a pixel. We want the difference to
            // be large enough that it doesn't get lost due to rounding inside the
            // driver, but small enough to prevent it from interfering with any
            // anti-aliasing.
            //
            // OpenGL coordinates specify the center of the pixel while d3d coords specify
            // the corner. The offsets are stored in z and w in posFixup. posFixup.y contains
            // 1.0 or -1.0 to turn the rendering upside down for offscreen rendering. PosFixup.x
            // contains 1.0 to allow a mad.

            _posFixup[0] = 1.0f;
            _posFixup[1] = 1.0f;
            _posFixup[2] = (63.0f/64.0f)/Viewport.Width;
            _posFixup[3] = -(63.0f/64.0f)/Viewport.Height;

            //If we have a render target bound (rendering offscreen)
            if (IsRenderTargetBound)
            {
                //flip vertically
                _posFixup[1] *= -1.0f;
                _posFixup[3] *= -1.0f;
            }

            GL.Uniform4(posFixupLoc.location, 1, _posFixup);
            GraphicsExtensions.CheckGLError();
        }

        internal void PlatformApplyState(bool applyShaders)
        {
            Threading.EnsureUIThread();

            if ( _scissorRectangleDirty )
	        {
                var scissorRect = _scissorRectangle;
                if (!IsRenderTargetBound)
                    scissorRect.Y = _viewport.Height - scissorRect.Y - scissorRect.Height;
                GL.Scissor(scissorRect.X, scissorRect.Y, scissorRect.Width, scissorRect.Height);
                GraphicsExtensions.CheckGLError();
	            _scissorRectangleDirty = false;
	        }

            if (_blendStateDirty)
            {
                _blendState.PlatformApplyState(this);
                _blendStateDirty = false;
            }
	        if ( _depthStencilStateDirty )
            {
	            _depthStencilState.PlatformApplyState(this);
                _depthStencilStateDirty = false;
            }
	        if ( _rasterizerStateDirty )
            {
	            _rasterizerState.PlatformApplyState(this);
	            _rasterizerStateDirty = false;
            }

            // If we're not applying shaders then early out now.
            if (!applyShaders)
                return;

            if (_indexBufferDirty)
            {
                if (_indexBuffer != null)
                {
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer.ibo);
                    GraphicsExtensions.CheckGLError();
                }
                _indexBufferDirty = false;
            }

            if (_vertexBufferDirty)
            {
                if (_vertexBuffer != null)
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer.vbo);
                    GraphicsExtensions.CheckGLError();
                }
            }

            if (_vertexShader == null)
                throw new InvalidOperationException("A vertex shader must be set!");
            if (_pixelShader == null)
                throw new InvalidOperationException("A pixel shader must be set!");

            if (_vertexShaderDirty || _pixelShaderDirty)
            {
                ActivateShaderProgram();
                _vertexShaderDirty = _pixelShaderDirty = false;
            }

            _vertexConstantBuffers.SetConstantBuffers(this, _shaderProgram);
            _pixelConstantBuffers.SetConstantBuffers(this, _shaderProgram);

            Textures.SetTextures(this);
            SamplerStates.PlatformSetSamplers(this);
        }

        private void PlatformDrawIndexedPrimitives(PrimitiveType primitiveType, int baseVertex, int startIndex, int primitiveCount)
        {
            PlatformApplyState(true);

            var shortIndices = _indexBuffer.IndexElementSize == IndexElementSize.SixteenBits;

			var indexElementType = shortIndices ? DrawElementsType.UnsignedShort : DrawElementsType.UnsignedInt;
            var indexElementSize = shortIndices ? 2 : 4;
			var indexOffsetInBytes = (IntPtr)(startIndex * indexElementSize);
			var indexElementCount = GetElementCountArray(primitiveType, primitiveCount);
			var target = PrimitiveTypeGL(primitiveType);
			var vertexOffset = (IntPtr)(_vertexBuffer.VertexDeclaration.VertexStride * baseVertex);

			_vertexBuffer.VertexDeclaration.Apply(_vertexShader, _pixelShader, vertexOffset);

            GL.DrawElements(target,
                                     indexElementCount,
                                     indexElementType,
                                     indexOffsetInBytes);
            GraphicsExtensions.CheckGLError();
        }

        private void PlatformDrawUserPrimitives<T>(PrimitiveType primitiveType, T[] vertexData, int vertexOffset, VertexDeclaration vertexDeclaration, int vertexCount) where T : unmanaged
        {
            PlatformApplyState(true);

            // Unbind current VBOs.
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            _vertexBufferDirty = _indexBufferDirty = true;

            unsafe
            {
                fixed (void* vertexPtr = &vertexData[0])
                {
                    // Setup the vertex declaration to point at the VB data.
                    vertexDeclaration.GraphicsDevice = this;
                    vertexDeclaration.Apply(_vertexShader, _pixelShader, (IntPtr)vertexPtr);

                    //Draw
                    GL.DrawArrays(PrimitiveTypeGL(primitiveType),
                                  vertexOffset,
                                  vertexCount);
                    GraphicsExtensions.CheckGLError();
                }
            }

            //// Pin the buffers.
            //var vbHandle = GCHandle.Alloc(vertexData, GCHandleType.Pinned);

            

            //// Release the handles.
            //vbHandle.Free();
        }

        private void PlatformDrawUserPrimitives(PrimitiveType primitiveType, IntPtr vertexDataPtr, int vertexOffset, VertexDeclaration vertexDeclaration, int vertexCount)
        {
            PlatformApplyState(true);

            // Unbind current VBOs.
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            _vertexBufferDirty = _indexBufferDirty = true;

            var vertexAddr = (IntPtr)(vertexDataPtr.ToInt64() + vertexDeclaration.VertexStride * vertexOffset);

            // Setup the vertex declaration to point at the VB data.
            vertexDeclaration.GraphicsDevice = this;
            vertexDeclaration.Apply(_vertexShader, _pixelShader, vertexAddr);

            //Draw
            GL.DrawArrays(PrimitiveTypeGL(primitiveType), vertexOffset, vertexCount);
            GraphicsExtensions.CheckGLError();
        }

        private void PlatformDrawPrimitives(PrimitiveType primitiveType, int vertexStart, int vertexCount)
        {
            PlatformApplyState(true);

            _vertexBuffer.VertexDeclaration.Apply(_vertexShader, _pixelShader, IntPtr.Zero);

			GL.DrawArrays(PrimitiveTypeGL(primitiveType),
			              vertexStart,
			              vertexCount);
            GraphicsExtensions.CheckGLError();
        }

        private void PlatformDrawUserIndexedPrimitives<T>(PrimitiveType primitiveType, T[] vertexData, int vertexOffset, int numVertices, short[] indexData, int indexOffset, int primitiveCount, VertexDeclaration vertexDeclaration) where T : unmanaged
        {
            PlatformApplyState(true);

            // Unbind current VBOs.
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            _vertexBufferDirty = _indexBufferDirty = true;

            // Pin the buffers.
            var vbHandle = GCHandle.Alloc(vertexData, GCHandleType.Pinned);
            var ibHandle = GCHandle.Alloc(indexData, GCHandleType.Pinned);

            var vertexAddr = (IntPtr)(vbHandle.AddrOfPinnedObject().ToInt64() + vertexDeclaration.VertexStride * vertexOffset);

            // Setup the vertex declaration to point at the VB data.
            vertexDeclaration.GraphicsDevice = this;
            vertexDeclaration.Apply(_vertexShader, _pixelShader, vertexAddr);

            //Draw
            GL.DrawElements(    PrimitiveTypeGL(primitiveType),
                                GetElementCountArray(primitiveType, primitiveCount),
                                DrawElementsType.UnsignedShort,
                                (IntPtr)(ibHandle.AddrOfPinnedObject().ToInt64() + (indexOffset * sizeof(short))));
            GraphicsExtensions.CheckGLError();

            // Release the handles.
            ibHandle.Free();
            vbHandle.Free();
        }

        private void PlatformDrawUserIndexedPrimitives<T>(PrimitiveType primitiveType, T[] vertexData, int vertexOffset, int numVertices, int[] indexData, int indexOffset, int primitiveCount, VertexDeclaration vertexDeclaration) where T : unmanaged, IVertexType
        {
            PlatformApplyState(true);

            // Unbind current VBOs.
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            _vertexBufferDirty = _indexBufferDirty = true;

            // Pin the buffers.
            var vbHandle = GCHandle.Alloc(vertexData, GCHandleType.Pinned);
            var ibHandle = GCHandle.Alloc(indexData, GCHandleType.Pinned);

            var vertexAddr = (IntPtr)(vbHandle.AddrOfPinnedObject().ToInt64() + vertexDeclaration.VertexStride * vertexOffset);

            // Setup the vertex declaration to point at the VB data.
            vertexDeclaration.GraphicsDevice = this;
            vertexDeclaration.Apply(_vertexShader, _pixelShader, vertexAddr);

            //Draw
            GL.DrawElements(    PrimitiveTypeGL(primitiveType),
                                GetElementCountArray(primitiveType, primitiveCount),
                                DrawElementsType.UnsignedInt,
                                (IntPtr)(ibHandle.AddrOfPinnedObject().ToInt64() + (indexOffset * sizeof(int))));
            GraphicsExtensions.CheckGLError();

            // Release the handles.
            ibHandle.Free();
            vbHandle.Free();
        }

        private void PlatformDrawUserIndexedPrimitives(PrimitiveType primitiveType, IntPtr vertexDataPtr, int vertexOffset, int numVertices, IndexElementSize indexSize, IntPtr indexDataPtr, int indexOffset, int primitiveCount, VertexDeclaration vertexDeclaration)
        {
            PlatformApplyState(true);

            // Unbind current VBOs.
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GraphicsExtensions.CheckGLError();
            _vertexBufferDirty = _indexBufferDirty = true;

            var vertexAddr = (IntPtr)(vertexDataPtr.ToInt64() + vertexDeclaration.VertexStride * vertexOffset);

            // Setup the vertex declaration to point at the VB data.
            vertexDeclaration.GraphicsDevice = this;
            vertexDeclaration.Apply(_vertexShader, _pixelShader, vertexAddr);

            //Draw
            GL.DrawElements(PrimitiveTypeGL(primitiveType),
                                GetElementCountArray(primitiveType, primitiveCount),
                                indexSize == IndexElementSize.SixteenBits ? DrawElementsType.UnsignedShort : DrawElementsType.UnsignedInt,
                                (IntPtr)(indexDataPtr.ToInt64() + (indexOffset * (indexSize == IndexElementSize.SixteenBits ? 2 : 4))));
            GraphicsExtensions.CheckGLError();
        }

        private static GraphicsProfile PlatformGetHighestSupportedGraphicsProfile(GraphicsDevice graphicsDevice)
        {
           return GraphicsProfile.HiDef;
        }
    }
}
