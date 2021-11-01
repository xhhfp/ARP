using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Portal.Rendering.Aperture
{
    /// <summary>
    ///  Class <c>ScriptableRenderer</c> implements a rendering strategy. It describes how culling and lighting works and
    /// the effects supported.
    ///
    ///  A renderer can be used for all cameras or be overridden on a per-camera basis. It will implement light culling and setup
    /// and describe a list of <c>ScriptableRenderPass</c> to execute in a frame. The renderer can be extended to support more effect with additional
    ///  <c>ScriptableRendererFeature</c>. Resources for the renderer are serialized in <c>ScriptableRendererData</c>.
    ///
    /// he renderer resources are serialized in <c>ScriptableRendererData</c>.
    /// <seealso cref="ScriptableRendererData"/>
    /// <seealso cref="ScriptableRendererFeature"/>
    /// <seealso cref="ScriptableRenderPass"/>
    /// </summary>
    public abstract class ScriptableRenderer : IDisposable
    {

        private static class Profiling
        {
            private const string K_NAME = nameof(ScriptableRenderer);
            public static readonly ProfilingSampler setPerCameraShaderVariables = new ProfilingSampler($"{K_NAME}.{nameof(SetPerCameraShaderVariables)}");
            public static readonly ProfilingSampler sortRenderPasses = new ProfilingSampler($"Sort Render Passes");
            public static readonly ProfilingSampler setupLights = new ProfilingSampler($"{K_NAME}.{nameof(SetupLights)}");
            public static readonly ProfilingSampler setupCamera = new ProfilingSampler($"Setup Camera Parameters");
            public static readonly ProfilingSampler addRenderPasses = new ProfilingSampler($"{K_NAME}.{nameof(AddRenderPasses)}");
            public static readonly ProfilingSampler clearRenderingState = new ProfilingSampler($"{K_NAME}.{nameof(ClearRenderingState)}");
            public static readonly ProfilingSampler internalStartRendering = new ProfilingSampler($"{K_NAME}.{nameof(InternalStartRendering)}");
            public static readonly ProfilingSampler internalFinishRendering = new ProfilingSampler($"{K_NAME}.{nameof(InternalFinishRendering)}");

            public static class RenderBlock
            {
                private const string K_NAME = nameof(RenderPassBlock);
                public static readonly ProfilingSampler beforeRendering = new ProfilingSampler($"{K_NAME}.{nameof(RenderPassBlock.BeforeRendering)}");
                public static readonly ProfilingSampler mainRenderingOpaque = new ProfilingSampler($"{K_NAME}.{nameof(RenderPassBlock.MainRenderingOpaque)}");
                public static readonly ProfilingSampler mainRenderingTransparent = new ProfilingSampler($"{K_NAME}.{nameof(RenderPassBlock.MainRenderingTransparent)}");
                public static readonly ProfilingSampler afterRendering = new ProfilingSampler($"{K_NAME}.{nameof(RenderPassBlock.AfterRendering)}");
            }

            public static class RenderPass
            {
                private const string K_NAME = nameof(ScriptableRenderPass);
                public static readonly ProfilingSampler configure = new ProfilingSampler($"{K_NAME}.{nameof(ScriptableRenderPass.Configure)}");
            }
        }

        public class RenderingFeatures
        {
            /// <summary>
            /// This setting controls if the camera editor should display the camera stack category.
            /// Renderers that don't support camera stacking will only render camera of type CameraRenderType.Base
            /// <see cref="CameraRenderType"/>
            /// <seealso cref="ApertureAdditionalCameraData.CameraStack"/>
            /// </summary>
            public bool CameraStacking { get; set; } = false;

            /// <summary>
            /// This setting controls if the Aperture Render Pipeline asset should expose MSAA option.
            /// </summary>
            public bool Msaa { get; set; } = true;
        }

        /// <summary>
        /// The renderer we are currently rendering with, for low-level render control only.
        /// <c>current</c> is null outside rendering scope.
        /// Similar to https://docs.unity3d.com/ScriptReference/Camera-current.html
        /// </summary>
        internal static ScriptableRenderer s_current = null;

        /// <summary>
        /// Set camera matrices. This method will set <c>UNITY_MATRIX_V</c>, <c>UNITY_MATRIX_P</c>, <c>UNITY_MATRIX_VP</c> to camera matrices.
        /// Additionally this will also set <c>unity_CameraProjection</c> and <c>unity_CameraProjection</c>.
        /// If <c>setInverseMatrices</c> is set to true this function will also set <c>UNITY_MATRIX_I_V</c> and <c>UNITY_MATRIX_I_VP</c>.
        /// This function has no effect when rendering in stereo. When in stereo rendering you cannot override camera matrices.
        /// If you need to set general purpose view and projection matrices call <see cref="SetViewAndProjectionMatrices(CommandBuffer, Matrix4x4, Matrix4x4, bool)"/> instead.
        /// </summary>
        /// <param name="cmd">CommandBuffer to submit data to GPU.</param>
        /// <param name="cameraData">CameraData containing camera matrices information.</param>
        /// <param name="setInverseMatrices">Set this to true if you also need to set inverse camera matrices.</param>
        public static void SetCameraMatrices(CommandBuffer cmd, ref CameraData cameraData, bool setInverseMatrices)
        {
            Matrix4x4 viewMatrix = cameraData.GetViewMatrix();
            Matrix4x4 projectionMatrix = cameraData.GetProjectionMatrix();

            // TODO: Investigate why SetViewAndProjectionMatrices is causing y-flip / winding order issue
            // for now using cmd.SetViewProjecionMatrices
            //SetViewAndProjectionMatrices(cmd, viewMatrix, cameraData.GetDeviceProjectionMatrix(), setInverseMatrices);
            cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

            if (setInverseMatrices)
            {
                Matrix4x4 gpuProjectionMatrix = cameraData.GetGPUProjectionMatrix();
                //Matrix4x4 viewAndProjectionMatrix = gpuProjectionMatrix * viewMatrix;
                Matrix4x4 inverseViewMatrix = Matrix4x4.Inverse(viewMatrix);
                Matrix4x4 inverseProjectionMatrix = Matrix4x4.Inverse(gpuProjectionMatrix);
                Matrix4x4 inverseViewProjection = inverseViewMatrix * inverseProjectionMatrix;

                // There's an inconsistency in handedness between unity_matrixV and unity_WorldToCamera
                // Unity changes the handedness of unity_WorldToCamera (see Camera::CalculateMatrixShaderProps)
                // we will also change it here to avoid breaking existing shaders. (case 1257518)
                // _Q:Why left mul Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f))
                Matrix4x4 worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)) * viewMatrix;
                Matrix4x4 cameraToWorldMatrix = worldToCameraMatrix.inverse;
                cmd.SetGlobalMatrix(ShaderPropertyId.WorldToCameraMatrix, worldToCameraMatrix);
                cmd.SetGlobalMatrix(ShaderPropertyId.CameraToWorldMatrix, cameraToWorldMatrix);

                cmd.SetGlobalMatrix(ShaderPropertyId.InverseViewMatrix, inverseViewMatrix);
                cmd.SetGlobalMatrix(ShaderPropertyId.InverseProjectionMatrix, inverseProjectionMatrix);
                cmd.SetGlobalMatrix(ShaderPropertyId.InverseViewAndProjectionMatrix, inverseViewProjection);
            }
        }

        /// <summary>
        /// Set camera and screen shader variables as described in https://docs.unity3d.com/Manual/SL-UnityShaderVariables.html
        /// </summary>
        /// <param name="cmd">CommandBuffer to submit data to GPU.</param>
        /// <param name="cameraData">CameraData containing camera matrices information.</param>
        void SetPerCameraShaderVariables(CommandBuffer cmd, ref CameraData cameraData)
        {
            using ProfilingScope profScope = new ProfilingScope(cmd, Profiling.setPerCameraShaderVariables);

            Camera camera = cameraData.Camera;

            Rect pixelRect = cameraData._pixelRect;
            float renderScale = cameraData.IsSceneViewCamera ? 1f : cameraData.RenderScale;
            float scaledCameraWidth = (float)pixelRect.width * renderScale;
            float scaledCameraHeight = (float)pixelRect.height * renderScale;
            float cameraWidth = (float)pixelRect.width;
            float cameraHeight = (float)pixelRect.height;

            if (camera.allowDynamicResolution)
            {
                scaledCameraWidth *= ScalableBufferManager.widthScaleFactor;
                scaledCameraHeight *= ScalableBufferManager.heightScaleFactor;
            }

            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;
            float invNear = Mathf.Approximately(near, 0.0f) ? 0.0f : 1.0f / near;
            float invFar = Mathf.Approximately(far, 0.0f) ? 0.0f : 1.0f / far;
            float isOrthographic = camera.orthographic ? 1.0f : 0.0f;

            // From http://www.humus.name/temp/Linearize%20depth.txt
            // But as depth component textures on OpenGL always return in 0..1 range (as in D3D), we have to use
            // the same constants for both D3D and OpenGL here.
            // OpenGL would be this:
            // zc0 = (1.0 - far / near) / 2.0;
            // zc1 = (1.0 + far / near) / 2.0;
            // D3D is this:
            float zc0 = 1.0f - far * invNear;
            float zc1 = far * invNear;

            Vector4 zBufferParams = new Vector4(zc0, zc1, zc0 * invFar, zc1 * invFar);

            if (SystemInfo.usesReversedZBuffer)
            {
                zBufferParams.y += zBufferParams.x;
                zBufferParams.x = -zBufferParams.x;
                zBufferParams.w += zBufferParams.z;
                zBufferParams.z = -zBufferParams.z;
            }

            // Projection flip sign logic is very deep in GfxDevice::SetInvertProjectionMatrix
            // For now we don't deal with _ProjectionParams.x and let SetupCameraProperties handle it.
            // We need to enable this when we remove SetupCameraProperties
            // float projectionFlipSign = ???
            // Vector4 projectionParams = new Vector4(projectionFlipSign, near, far, 1.0f * invFar);
            // cmd.SetGlobalVector(ShaderPropertyId.projectionParams, projectionParams);

            Vector4 orthoParams = new Vector4(camera.orthographicSize * cameraData._aspectRatio, camera.orthographicSize, 0.0f, isOrthographic);

            // Camera and Screen variables as described in https://docs.unity3d.com/Manual/SL-UnityShaderVariables.html
            cmd.SetGlobalVector(ShaderPropertyId.WorldSpaceCameraPos, camera.transform.position);
            cmd.SetGlobalVector(ShaderPropertyId.ScreenParams, new Vector4(cameraWidth, cameraHeight, 1.0f + 1.0f / cameraWidth, 1.0f + 1.0f / cameraHeight));
            cmd.SetGlobalVector(ShaderPropertyId.ScaledScreenParams, new Vector4(scaledCameraWidth, scaledCameraHeight, 1.0f + 1.0f / scaledCameraWidth, 1.0f + 1.0f / scaledCameraHeight));
            cmd.SetGlobalVector(ShaderPropertyId.ZBufferParams, zBufferParams);
            cmd.SetGlobalVector(ShaderPropertyId.OrthoParams, orthoParams);
        }

        /// <summary>
        /// Set shader time variables as described in https://docs.unity3d.com/Manual/SL-UnityShaderVariables.html
        /// </summary>
        /// <param name="cmd">CommandBuffer to submit data to GPU.</param>
        /// <param name="time">Time.</param>
        /// <param name="deltaTime">Delta time.</param>
        /// <param name="smoothDeltaTime">Smooth delta time.</param>
        void SetShaderTimeValues(CommandBuffer cmd, float time, float deltaTime, float smoothDeltaTime)
        {
            float timeEights = time / 8f;
            float timeFourth = time / 4f;
            float timeHalf = time / 2f;

            // Time values
            Vector4 timeVector = time * new Vector4(1f / 20f, 1f, 2f, 3f);
            Vector4 sinTimeVector = new Vector4(Mathf.Sin(timeEights), Mathf.Sin(timeFourth), Mathf.Sin(timeHalf), Mathf.Sin(time));
            Vector4 cosTimeVector = new Vector4(Mathf.Cos(timeEights), Mathf.Cos(timeFourth), Mathf.Cos(timeHalf), Mathf.Cos(time));
            Vector4 deltaTimeVector = new Vector4(deltaTime, 1f / deltaTime, smoothDeltaTime, 1f / smoothDeltaTime);
            Vector4 timeParametersVector = new Vector4(time, Mathf.Sin(time), Mathf.Cos(time), 0.0f);

            cmd.SetGlobalVector(ShaderPropertyId.Time, timeVector);
            cmd.SetGlobalVector(ShaderPropertyId.SinTime, sinTimeVector);
            cmd.SetGlobalVector(ShaderPropertyId.CosTime, cosTimeVector);
            cmd.SetGlobalVector(ShaderPropertyId.DeltaTime, deltaTimeVector);
            cmd.SetGlobalVector(ShaderPropertyId.TimeParameters, timeParametersVector);
        }
        /// <summary>
        /// Override to provide a custom profiling name
        /// </summary>
        protected ProfilingSampler _profilingExecute { get; set; }
        /// <summary>
        /// Configures the supported features for this renderer. When creating custom renderers
        /// for Aperture Render Pipeline you can choose to opt-in or out for specific features.
        /// </summary>

        public RenderTargetIdentifier CameraColorTarget
        {
            get
            {
                if (!(_isPipelineExecuting || _isCameraColorTargetValid))
                {
                    UnityEngine.Debug.LogWarning("You can only call cameraColorTarget inside the scope of a ScriptableRenderPass. Otherwise the pipeline camera target texture might have not been created or might have already been disposed.");
                }
                return _cameraColorTarget;
            }
        }

        public RenderTargetIdentifier CameraDepthTarget
        {
            get
            {
                if (!_isPipelineExecuting)
                {
                    UnityEngine.Debug.LogWarning("You can only call cameraDepthTarget inside the scope of a ScriptableRenderPass. Otherwise the pipeline camera target texture might have not been created or might have already been disposed.");
                }
                return _cameraDepthTarget;
            }
        }

        static class RenderPassBlock
        {
            // Executes render passes that are inputs to the main rendering
            // but don't depend on camera state. They all render in monoscopic mode. f.ex, shadow maps.
            public static readonly int BeforeRendering = 0;

            // Main bulk of render pass execution. They required camera state to be properly set
            // and when enabled they will render in stereo.
            public static readonly int MainRenderingOpaque = 1;
            public static readonly int MainRenderingTransparent = 2;

            // Execute after Post-processing.
            public static readonly int AfterRendering = 3;
        }

        private const int RENDER_PASS_BLOCK_COUNT = 4;

        private List<ScriptableRenderPass> _activeRenderPassQueue = new List<ScriptableRenderPass>(32);
        private List<ScriptableRendererFeature> _rendererFeatures = new List<ScriptableRendererFeature>(10);

        private RenderTargetIdentifier _cameraColorTarget;
        private RenderTargetIdentifier _cameraDepthTarget;

        private bool _firstTimeCameraColorTargetIsBound = true; // flag used to track when _cameraColorTarget should be cleared (if necessary), as well as other special actions only performed the first time _cameraColorTarget is bound as a render target
        private bool _firstTimeCameraDepthTargetIsBound = true; // flag used to track when _cameraDepthTarget should be cleared (if necessary), the first time _cameraDepthTarget is bound as a render target

        private static RenderTargetIdentifier[] s_activeColorAttachments = new RenderTargetIdentifier[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        private static RenderTargetIdentifier s_activeDepthAttachment;
        // CommandBuffer.SetRenderTarget(RenderTargetIdentifier[] colors, RenderTargetIdentifier depth, int mipLevel, CubemapFace cubemapFace, int depthSlice);
        // called from CoreUtils.SetRenderTarget will issue a warning assert from native c++ side if "colors" array contains some invalid RTIDs.
        // To avoid that warning assert we trim the RenderTargetIdentifier[] arrays we pass to CoreUtils.SetRenderTarget.
        // To avoid re-allocating a new array every time we do that, we re-use one of these arrays:
        private static RenderTargetIdentifier[][] s_trimmedColorAttachmentCopies = new RenderTargetIdentifier[][]
        {
            new RenderTargetIdentifier[0],                          // m_TrimmedColorAttachmentCopies[0] is an array of 0 RenderTargetIdentifier - only used to make indexing code easier to read
            new RenderTargetIdentifier[] {0},                        // m_TrimmedColorAttachmentCopies[1] is an array of 1 RenderTargetIdentifier
            new RenderTargetIdentifier[] {0, 0},                     // m_TrimmedColorAttachmentCopies[2] is an array of 2 RenderTargetIdentifiers
            new RenderTargetIdentifier[] {0, 0, 0},                  // m_TrimmedColorAttachmentCopies[3] is an array of 3 RenderTargetIdentifiers
            new RenderTargetIdentifier[] {0, 0, 0, 0},               // m_TrimmedColorAttachmentCopies[4] is an array of 4 RenderTargetIdentifiers
            new RenderTargetIdentifier[] {0, 0, 0, 0, 0},            // m_TrimmedColorAttachmentCopies[5] is an array of 5 RenderTargetIdentifiers
            new RenderTargetIdentifier[] {0, 0, 0, 0, 0, 0},         // m_TrimmedColorAttachmentCopies[6] is an array of 6 RenderTargetIdentifiers
            new RenderTargetIdentifier[] {0, 0, 0, 0, 0, 0, 0},      // m_TrimmedColorAttachmentCopies[7] is an array of 7 RenderTargetIdentifiers
            new RenderTargetIdentifier[] {0, 0, 0, 0, 0, 0, 0, 0 },  // m_TrimmedColorAttachmentCopies[8] is an array of 8 RenderTargetIdentifiers
        };

        protected List<ScriptableRendererFeature> rendererFeatures
        {
            get => _rendererFeatures;
        }

        // The pipeline can only guarantee the camera target texture are valid when the pipeline is executing.
        // Trying to access the camera target before or after might be that the pipeline texture have already been disposed.
        private bool _isPipelineExecuting = false;
        // This should be removed when early camera color target assignment is removed.
        internal bool _isCameraColorTargetValid = false;


        public RenderingFeatures SupportedRenderingFeatures { get; set; } = new RenderingFeatures();

        public ScriptableRenderer(ScriptableRendererData data)
        {
            _profilingExecute = new ProfilingSampler($"{nameof(ScriptableRenderer)}.{nameof(ScriptableRenderer.Execute)}: {data.name}");

            foreach (ScriptableRendererFeature feature in data.RendererFeatures)
            {
                if (feature == null)
                    continue;

                feature.Create();
                _rendererFeatures.Add(feature);
            }
            Clear(CameraRenderType.Base);
            _activeRenderPassQueue.Clear();
        }

        public void Dispose()
        {
            for (int i = 0; i < _rendererFeatures.Count; ++i)
            {
                if (rendererFeatures[i] == null)
                    continue;

                rendererFeatures[i].Dispose();
            }

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {

        }

        internal static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier colorAttachment, RenderTargetIdentifier DepthAttachment, ClearFlag ClearFlag, Color ClearColor)
        {
            s_activeColorAttachments[0] = colorAttachment;
            for (int i = 1; i < s_activeColorAttachments.Length; ++i)
                s_activeColorAttachments[i] = 0;

            s_activeDepthAttachment = DepthAttachment;

            RenderBufferLoadAction colorLoadAction = ((uint)ClearFlag & (uint)ClearFlag.Color) != 0 ?
                RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load;

            RenderBufferLoadAction depthLoadAction = ((uint)ClearFlag & (uint)ClearFlag.Depth) != 0 ?
                RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load;

            SetRenderTarget(cmd, colorAttachment, colorLoadAction, RenderBufferStoreAction.Store,
                DepthAttachment, depthLoadAction, RenderBufferStoreAction.Store, ClearFlag, ClearColor);
        }
        static void SetRenderTarget(
            CommandBuffer cmd,
            RenderTargetIdentifier colorAttachment,
            RenderBufferLoadAction colorLoadAction,
            RenderBufferStoreAction colorStoreAction,
            ClearFlag ClearFlags,
            Color ClearColor)
        {
            CoreUtils.SetRenderTarget(cmd, colorAttachment, colorLoadAction, colorStoreAction, ClearFlags, ClearColor);
        }

        static void SetRenderTarget(
            CommandBuffer cmd,
            RenderTargetIdentifier colorAttachment,
            RenderBufferLoadAction colorLoadAction,
            RenderBufferStoreAction colorStoreAction,
            RenderTargetIdentifier DepthAttachment,
            RenderBufferLoadAction depthLoadAction,
            RenderBufferStoreAction depthStoreAction,
            ClearFlag ClearFlags,
            Color ClearColor)
        {
            // _Q:Why depth can not clear when it is camera target but color can
            if (DepthAttachment == BuiltinRenderTextureType.CameraTarget)
            {
                SetRenderTarget(cmd, colorAttachment, colorLoadAction, colorStoreAction, ClearFlags, ClearColor);
            }
            else
            {
                CoreUtils.SetRenderTarget(cmd, colorAttachment, colorLoadAction, colorStoreAction,
                    DepthAttachment, depthLoadAction, depthStoreAction, ClearFlags, ClearColor);
            }
        }

        static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier[] ColorAttachments, RenderTargetIdentifier DepthAttachment, ClearFlag ClearFlag, Color ClearColor)
        {
            s_activeColorAttachments = ColorAttachments;
            s_activeDepthAttachment = DepthAttachment;

            CoreUtils.SetRenderTarget(cmd, ColorAttachments, DepthAttachment, ClearFlag, ClearColor);
        }

        public abstract void Setup(ScriptableRenderContext context, ref RenderingData renderingData);

        /// <summary>
        /// Override this method to implement the lighting setup for the renderer. You can use this to
        /// compute and upload light CBUFFER for example.
        /// </summary>
        /// <param name="context">Use this render context to issue any draw commands during execution.</param>
        /// <param name="renderingData">Current render state information.</param>
        public virtual void SetupLights(ScriptableRenderContext context, ref RenderingData renderingData)
        {
        }

        /// <summary>
        /// Override this method to configure the culling parameters for the renderer. You can use this to configure if
        /// lights should be culled per-object or the maximum shadow distance for example.
        /// </summary>
        /// <param name="cullingParameters">Use this to change culling parameters used by the render pipeline.</param>
        /// <param name="cameraData">Current render state information.</param>
        public virtual void SetupCullingParameters(ref ScriptableCullingParameters cullingParameters,
            ref CameraData cameraData)
        {
        }

        /// <summary>
        /// Execute the enqueued render passes. This automatically handles editor and stereo rendering.
        /// </summary>
        /// <param name="context">Use this render context to issue any draw commands during execution.</param>
        /// <param name="renderingData">Current render state information.</param>
        ///         
        public virtual void FinishRendering(CommandBuffer cmd)
        {
        }
        /// <summary>
        /// Configures the camera target.
        /// </summary>
        /// <param name="colorTarget">Camera color target. Pass BuiltinRenderTextureType.CameraTarget if rendering to backbuffer.</param>
        /// <param name="depthTarget">Camera depth target. Pass BuiltinRenderTextureType.CameraTarget if color has depth or rendering to backbuffer.</param>
        public void ConfigureCameraTarget(RenderTargetIdentifier colorTarget, RenderTargetIdentifier depthTarget)
        {
            _cameraColorTarget = colorTarget;
            _cameraDepthTarget = depthTarget;
        }

        /// <summary>
        /// Enqueues a render pass for execution.
        /// </summary>
        /// <param name="pass">Render pass to be enqueued.</param>
        public void EnqueuePass(ScriptableRenderPass pass)
        {
            _activeRenderPassQueue.Add(pass);
        }

        /// <summary>
        /// Calls <c>AddRenderPasses</c> for each feature added to this renderer.
        /// <seealso cref="ScriptableRendererFeature.AddRenderPasses(ScriptableRenderer, ref RenderingData)"/>
        /// </summary>
        /// <param name="renderingData"></param>
        protected void AddRenderPasses(ref RenderingData renderingData)
        {
            using var profScope = new ProfilingScope(null, Profiling.addRenderPasses);

            // Add render passes from custom renderer features
            for (int i = 0; i < rendererFeatures.Count; ++i)
            {
                if (!rendererFeatures[i].IsActive)
                {
                    continue;
                }
                rendererFeatures[i].AddRenderPasses(this, ref renderingData);
            }

            // Remove any null render pass that might have been added by user by mistake
            int count = _activeRenderPassQueue.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                if (_activeRenderPassQueue[i] == null)
                    _activeRenderPassQueue.RemoveAt(i);
            }
        }
        public void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            _isPipelineExecuting = true;
            ref CameraData cameraData = ref renderingData.CameraData;
            Camera camera = cameraData.Camera;

            CommandBuffer cmd = CommandBufferPool.Get();

            // TODO: move skybox code from C++ to URP in order to remove the call to context.Submit() inside DrawSkyboxPass
            // Until then, we can't use nested profiling scopes with XR multipass
            CommandBuffer cmdScope =  cmd;

            using (new ProfilingScope(cmdScope, _profilingExecute))
            {
                InternalStartRendering(context, ref renderingData);

                // Cache the time for after the call to `SetupCameraProperties` and set the time variables in shader
                // For now we set the time variables per camera, as we plan to remove `SetupCameraProperties`.
                // Setting the time per frame would take API changes to pass the variable to each camera render.
                // Once `SetupCameraProperties` is gone, the variable should be set higher in the call-stack.
#if UNITY_EDITOR
                float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
#else
                float time = Time.time;
#endif
                float deltaTime = Time.deltaTime;
                float smoothDeltaTime = Time.smoothDeltaTime;

                // Initialize Camera Render State
                ClearRenderingState(cmd);
                SetPerCameraShaderVariables(cmd, ref cameraData);
                SetShaderTimeValues(cmd, time, deltaTime, smoothDeltaTime);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                using (new ProfilingScope(cmd, Profiling.sortRenderPasses))
                {
                    // Sort the render pass queue
                    SortStable(_activeRenderPassQueue);
                }

                using var renderBlocks = new RenderBlocks(_activeRenderPassQueue);

                using (new ProfilingScope(cmd, Profiling.setupLights))
                {
                    SetupLights(context, ref renderingData);
                }

                using (new ProfilingScope(cmd, Profiling.RenderBlock.beforeRendering))
                {
                    // Before Render Block. This render blocks always execute in mono rendering.
                    // Camera is not setup. Lights are not setup.
                    // Used to render input textures like shadowmaps.
                    ExecuteBlock(RenderPassBlock.BeforeRendering, in renderBlocks, context, ref renderingData);
                }

                using (new ProfilingScope(cmd, Profiling.setupCamera))
                {
                    // This is still required because of the following reasons:
                    // - Camera billboard properties.
                    // - Camera frustum planes: unity_CameraWorldClipPlanes[6]
                    // - _ProjectionParams.x logic is deep inside GfxDevice
                    // NOTE: The only reason we have to call this here and not at the beginning (before shadows)
                    // is because this need to be called for each eye in multi pass VR.
                    // The side effect is that this will override some shader properties we already setup and we will have to
                    // reset them.
                    context.SetupCameraProperties(camera);
                    SetCameraMatrices(cmd, ref cameraData, true);

                    // Reset shader time variables as they were overridden in SetupCameraProperties. If we don't do it we might have a mismatch between shadows and main rendering
                    SetShaderTimeValues(cmd, time, deltaTime, smoothDeltaTime);

#if VISUAL_EFFECT_GRAPH_0_0_1_OR_NEWER
                    //Triggers dispatch per camera, all global parameters should have been setup at this stage.
                    VFX.VFXManager.ProcessCameraCommand(camera, cmd);
#endif
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                //BeginXRRendering(cmd, context, ref renderingData.cameraData);

                // In the opaque and transparent blocks the main rendering executes.

                // Opaque blocks...
                if (renderBlocks.GetLength(RenderPassBlock.MainRenderingOpaque) > 0)
                {
                    using var profScope = new ProfilingScope(cmd, Profiling.RenderBlock.mainRenderingOpaque);
                    ExecuteBlock(RenderPassBlock.MainRenderingOpaque, in renderBlocks, context, ref renderingData);
                }

                // Transparent blocks...
                if (renderBlocks.GetLength(RenderPassBlock.MainRenderingTransparent) > 0)
                {
                    using var profScope = new ProfilingScope(cmd, Profiling.RenderBlock.mainRenderingTransparent);
                    ExecuteBlock(RenderPassBlock.MainRenderingTransparent, in renderBlocks, context, ref renderingData);
                }

                // Draw Gizmos...
                //DrawGizmos(context, camera, GizmoSubset.PreImageEffects);

                // In this block after rendering drawing happens, e.g, post processing, video player capture.
                if (renderBlocks.GetLength(RenderPassBlock.AfterRendering) > 0)
                {
                    using var profScope = new ProfilingScope(cmd, Profiling.RenderBlock.afterRendering);
                    ExecuteBlock(RenderPassBlock.AfterRendering, in renderBlocks, context, ref renderingData);
                }

                DrawWireOverlay(context, camera);
                //DrawGizmos(context, camera, GizmoSubset.PostImageEffects);

                InternalFinishRendering(context, cameraData.ResolveFinalTarget);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /// <summary>
        /// Returns a clear flag based on CameraClearFlags.
        /// </summary>
        /// <param name="cameraClearFlags">Camera clear flags.</param>
        /// <returns>A clear flag that tells if color and/or depth should be cleared.</returns>
        protected static ClearFlag GetCameraClearFlag(ref CameraData cameraData)
        {
            CameraClearFlags cameraClearFlags = cameraData.Camera.clearFlags;

            // Universal RP doesn't support CameraClearFlags.DepthOnly and CameraClearFlags.Nothing.
            // CameraClearFlags.DepthOnly has the same effect of CameraClearFlags.SolidColor
            // CameraClearFlags.Nothing clears Depth on PC/Desktop and in mobile it clears both
            // depth and color.
            // CameraClearFlags.Skybox clears depth only.

            // Implementation details:
            // Camera clear flags are used to initialize the attachments on the first render pass.
            // ClearFlag is used together with Tile Load action to figure out how to clear the camera render target.
            // In Tile Based GPUs ClearFlag.Depth + RenderBufferLoadAction.DontCare becomes DontCare load action.
            // While ClearFlag.All + RenderBufferLoadAction.DontCare become Clear load action.
            // In mobile we force ClearFlag.All as DontCare doesn't have noticeable perf. difference from Clear
            // and this avoid tile clearing issue when not rendering all pixels in some GPUs.
            // In desktop/consoles there's actually performance difference between DontCare and Clear.

            // RenderBufferLoadAction.DontCare in PC/Desktop behaves as not clearing screen
            // RenderBufferLoadAction.DontCare in Vulkan/Metal behaves as DontCare load action
            // RenderBufferLoadAction.DontCare in GLES behaves as glInvalidateBuffer

            // Overlay cameras composite on top of previous ones. They don't clear color.
            // For overlay cameras we check if depth should be cleared on not.
            if (cameraData.RenderType == CameraRenderType.Overlay)
                return (cameraData.ClearDepth) ? ClearFlag.Depth : ClearFlag.None;

            // Always clear on first render pass in mobile as it's same perf of DontCare and avoid tile clearing issues.
            if (Application.isMobilePlatform)
                return ClearFlag.All;

            if ((cameraClearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null) ||
                cameraClearFlags == CameraClearFlags.Nothing)
                return ClearFlag.Depth;

            return ClearFlag.All;
        }

        void ClearRenderingState(CommandBuffer cmd)
        {
            using var profScope = new ProfilingScope(cmd, Profiling.clearRenderingState);

            // Reset per-camera shader keywords. They are enabled depending on which render passes are executed.
            cmd.DisableShaderKeyword(ShaderKeywordStrings.MainLightShadows);
            cmd.DisableShaderKeyword(ShaderKeywordStrings.MainLightShadowCascades);
            cmd.DisableShaderKeyword(ShaderKeywordStrings.MainLightShadowScreen);
            cmd.DisableShaderKeyword(ShaderKeywordStrings.AdditionalLightsVertex);
            cmd.DisableShaderKeyword(ShaderKeywordStrings.AdditionalLightsPixel);
            cmd.DisableShaderKeyword(ShaderKeywordStrings.AdditionalLightShadows);
            cmd.DisableShaderKeyword(ShaderKeywordStrings.SoftShadows);
            cmd.DisableShaderKeyword(ShaderKeywordStrings.MixedLightingSubtractive); // Backward compatibility
            cmd.DisableShaderKeyword(ShaderKeywordStrings.LightmapShadowMixing);
            cmd.DisableShaderKeyword(ShaderKeywordStrings.ShadowsShadowMask);
            cmd.DisableShaderKeyword(ShaderKeywordStrings.LinearToSRGBConversion);
        }

        internal void Clear(CameraRenderType cameraType)
        {
            s_activeColorAttachments[0] = BuiltinRenderTextureType.CameraTarget;
            for (int i = 1; i < s_activeColorAttachments.Length; ++i)
                s_activeColorAttachments[i] = 0;

            s_activeDepthAttachment = BuiltinRenderTextureType.CameraTarget;

            _firstTimeCameraColorTargetIsBound = cameraType == CameraRenderType.Base;
            _firstTimeCameraDepthTargetIsBound = true;

            _cameraColorTarget = BuiltinRenderTextureType.CameraTarget;
            _cameraDepthTarget = BuiltinRenderTextureType.CameraTarget;
        }

        void ExecuteBlock(int blockIndex, in RenderBlocks renderBlocks,
    ScriptableRenderContext context, ref RenderingData renderingData, bool submit = false)
        {
            foreach (int currIndex in renderBlocks.GetRange(blockIndex))
            {
                ScriptableRenderPass renderPass = _activeRenderPassQueue[currIndex];
                ExecuteRenderPass(context, renderPass, ref renderingData);
            }

            if (submit)
                context.Submit();
        }

        void ExecuteRenderPass(ScriptableRenderContext context, ScriptableRenderPass renderPass, ref RenderingData renderingData)
        {
            using var profScope = new ProfilingScope(null, renderPass._ProfilingSampler);

            ref CameraData cameraData = ref renderingData.CameraData;

            CommandBuffer cmd = CommandBufferPool.Get();

            // Track CPU only as GPU markers for this scope were "too noisy".
            using (new ProfilingScope(cmd, Profiling.RenderPass.configure))
            {
                renderPass.Configure(cmd, cameraData.CameraTargetDescriptor);
                SetRenderPassAttachments(cmd, renderPass, ref cameraData);
            }

            // Also, we execute the commands recorded at this point to ensure SetRenderTarget is called before RenderPass.Execute
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            renderPass.Execute(context, ref renderingData);
        }

        void SetRenderPassAttachments(CommandBuffer cmd, ScriptableRenderPass renderPass, ref CameraData cameraData)
        {
            Camera camera = cameraData.Camera;
            ClearFlag cameraClearFlag = GetCameraClearFlag(ref cameraData);

            // Invalid configuration - use current attachment setup
            // Note: we only check color buffers. This is only technically correct because for shadowmaps and depth only passes
            // we bind depth as color and Unity handles it underneath. so we never have a situation that all color buffers are null and depth is bound.
            uint validColorBuffersCount = RenderingUtils.GetValidColorBufferCount(renderPass.ColorAttachments);
            if (validColorBuffersCount == 0)
                return;
            // _Note: MRT can return many render results by a pass
            // We use a different code path for MRT since it calls a different version of API SetRenderTarget
            if (RenderingUtils.IsMRT(renderPass.ColorAttachments))
            {
                // In the MRT path we assume that all color attachments are REAL color attachments,
                // and that the depth attachment is a REAL depth attachment too.


                // Determine what attachments need to be cleared. ----------------

                bool needCustomCameraColorClear = false;
                bool needCustomCameraDepthClear = false;

                int cameraColorTargetIndex = RenderingUtils.IndexOf(renderPass.ColorAttachments, _cameraColorTarget);
                if (cameraColorTargetIndex != -1 && (_firstTimeCameraColorTargetIsBound))
                {
                    _firstTimeCameraColorTargetIsBound = false; // register that we did clear the camera target the first time it was bound

                    // Overlay cameras composite on top of previous ones. They don't clear.
                    // MTT: Commented due to not implemented yet
                    //                    if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
                    //                        ClearFlag = ClearFlag.None;

                    // We need to specifically clear the camera color target.
                    // But there is still a chance we don't need to issue individual clear() on each render-targets if they all have the same clear parameters.
                    needCustomCameraColorClear = (cameraClearFlag & ClearFlag.Color) != (renderPass.ClearFlag & ClearFlag.Color)
                        || CoreUtils.ConvertSRGBToActiveColorSpace(camera.backgroundColor) != renderPass.ClearColor;
                }

                // Note: if we have to give up the assumption that no depthTarget can be included in the MRT ColorAttachments, we might need something like this:
                // int cameraTargetDepthIndex = IndexOf(renderPass.ColorAttachments, _cameraDepthTarget);
                // if( !renderTargetAlreadySet && cameraTargetDepthIndex != -1 && _firstTimeCameraDepthTargetIsBound)
                // { ...
                // }

                if (renderPass.DepthAttachment == CameraDepthTarget && _firstTimeCameraDepthTargetIsBound)
                {
                    _firstTimeCameraDepthTargetIsBound = false;
                    needCustomCameraDepthClear = (cameraClearFlag & ClearFlag.Depth) != (renderPass.ClearFlag & ClearFlag.Depth);
                }

                // Perform all clear operations needed. ----------------
                // We try to minimize calls to SetRenderTarget().

                // We get here only if cameraColorTarget needs to be handled separately from the rest of the color attachments.
                if (needCustomCameraColorClear)
                {
                    // Clear camera color render-target separately from the rest of the render-targets.

                    if ((cameraClearFlag & ClearFlag.Color) != 0)
                        SetRenderTarget(cmd, renderPass.ColorAttachments[cameraColorTargetIndex], renderPass.DepthAttachment, ClearFlag.Color, CoreUtils.ConvertSRGBToActiveColorSpace(camera.backgroundColor));

                    if ((renderPass.ClearFlag & ClearFlag.Color) != 0)
                    {
                        uint otherTargetsCount = RenderingUtils.CountDistinct(renderPass.ColorAttachments, _cameraColorTarget);
                        var nonCameraAttachments = s_trimmedColorAttachmentCopies[otherTargetsCount];
                        int writeIndex = 0;
                        for (int readIndex = 0; readIndex < renderPass.ColorAttachments.Length; ++readIndex)
                        {
                            if (renderPass.ColorAttachments[readIndex] != _cameraColorTarget && renderPass.ColorAttachments[readIndex] != 0)
                            {
                                nonCameraAttachments[writeIndex] = renderPass.ColorAttachments[readIndex];
                                ++writeIndex;
                            }
                        }

                        if (writeIndex != otherTargetsCount)
                            UnityEngine.Debug.LogError("writeIndex and otherTargetsCount values differed. writeIndex:" + writeIndex + " otherTargetsCount:" + otherTargetsCount);
                        SetRenderTarget(cmd, nonCameraAttachments, _cameraDepthTarget, ClearFlag.Color, renderPass.ClearColor);
                    }
                }

                // Bind all attachments, clear color only if there was no custom behaviour for cameraColorTarget, clear depth as needed.
                ClearFlag finalClearFlag = ClearFlag.None;
                finalClearFlag |= needCustomCameraDepthClear ? (cameraClearFlag & ClearFlag.Depth) : (renderPass.ClearFlag & ClearFlag.Depth);
                finalClearFlag |= needCustomCameraColorClear ? 0 : (renderPass.ClearFlag & ClearFlag.Color);

                // Only setup render target if current render pass attachments are different from the active ones.
                if (!RenderingUtils.SequenceEqual(renderPass.ColorAttachments, s_activeColorAttachments) || renderPass.DepthAttachment != s_activeDepthAttachment || finalClearFlag != ClearFlag.None)
                {
                    int lastValidRTindex = RenderingUtils.LastValid(renderPass.ColorAttachments);
                    if (lastValidRTindex >= 0)
                    {
                        int rtCount = lastValidRTindex + 1;
                        var trimmedAttachments = s_trimmedColorAttachmentCopies[rtCount];
                        for (int i = 0; i < rtCount; ++i)
                            trimmedAttachments[i] = renderPass.ColorAttachments[i];
                        SetRenderTarget(cmd, trimmedAttachments, renderPass.DepthAttachment, finalClearFlag, renderPass.ClearColor);
                    }
                }
            }
            else
            {
                // Currently in non-MRT case, color attachment can actually be a depth attachment.

                RenderTargetIdentifier passColorAttachment = renderPass.ColorAttachment;
                RenderTargetIdentifier passDepthAttachment = renderPass.DepthAttachment;

                // When render pass doesn't call ConfigureTarget we assume it's expected to render to camera target
                // which might be backbuffer or the framebuffer render textures.
                if (!renderPass._overrideCameraTarget)
                {
                    // Default render pass attachment for passes before main rendering is current active
                    // early return so we don't change current render target setup.
                    if (renderPass.RenderPassEvent < RenderPassEvent.BeforeRenderingOpaques)
                        return;

                    // Otherwise default is the pipeline camera target.
                    passColorAttachment = _cameraColorTarget;
                    passDepthAttachment = _cameraDepthTarget;
                }

                ClearFlag finalClearFlag = ClearFlag.None;
                Color finalClearColor;

                if (passColorAttachment == _cameraColorTarget && (_firstTimeCameraColorTargetIsBound))
                {
                    _firstTimeCameraColorTargetIsBound = false; // register that we did clear the camera target the first time it was bound

                    finalClearFlag |= (cameraClearFlag & ClearFlag.Color);
                    finalClearColor = CoreUtils.ConvertSRGBToActiveColorSpace(camera.backgroundColor);

                    if (_firstTimeCameraDepthTargetIsBound)
                    {
                        // _cameraColorTarget can be an opaque pointer to a RenderTexture with depth-surface.
                        // We cannot infer this information here, so we must assume both camera color and depth are first-time bound here (this is the legacy behaviour).
                        _firstTimeCameraDepthTargetIsBound = false;
                        finalClearFlag |= (cameraClearFlag & ClearFlag.Depth);
                    }
                }
                else
                {
                    finalClearFlag |= (renderPass.ClearFlag & ClearFlag.Color);
                    finalClearColor = renderPass.ClearColor;
                }

                if ((_cameraDepthTarget != BuiltinRenderTextureType.CameraTarget) && (passDepthAttachment == _cameraDepthTarget || passColorAttachment == _cameraDepthTarget) && _firstTimeCameraDepthTargetIsBound)
                {
                    _firstTimeCameraDepthTargetIsBound = false;

                    finalClearFlag |= (cameraClearFlag & ClearFlag.Depth);
                }
                else
                    finalClearFlag |= (renderPass.ClearFlag & ClearFlag.Depth);

                // Only setup render target if current render pass attachments are different from the active ones
                if (passColorAttachment != s_activeColorAttachments[0] || passDepthAttachment != s_activeDepthAttachment || finalClearFlag != ClearFlag.None)
                {
                    SetRenderTarget(cmd, passColorAttachment, passDepthAttachment, finalClearFlag, finalClearColor);
                }
            }
        }

        void InternalStartRendering(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, Profiling.internalStartRendering))
            {
                for (int i = 0; i < _activeRenderPassQueue.Count; ++i)
                {
                    _activeRenderPassQueue[i].OnCameraSetup(cmd, ref renderingData);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        [Conditional("UNITY_EDITOR")]
        void DrawWireOverlay(ScriptableRenderContext context, Camera camera)
        {
            context.DrawWireOverlay(camera);
        }

        void InternalFinishRendering(ScriptableRenderContext context, bool resolveFinalTarget)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, Profiling.internalFinishRendering))
            {
                for (int i = 0; i < _activeRenderPassQueue.Count; ++i)
                    _activeRenderPassQueue[i].OnCameraCleanup(cmd);

                // Happens when rendering the last camera in the camera stack.
                if (resolveFinalTarget)
                {
                    for (int i = 0; i < _activeRenderPassQueue.Count; ++i)
                        _activeRenderPassQueue[i].OnFinishCameraStackRendering(cmd);

                    FinishRendering(cmd);

                    // We finished camera stacking and released all intermediate pipeline textures.
                    _isPipelineExecuting = false;
                }
                _activeRenderPassQueue.Clear();
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        internal static void SortStable(List<ScriptableRenderPass> list)
        {
            int j;
            for (int i = 1; i < list.Count; ++i)
            {
                ScriptableRenderPass curr = list[i];

                j = i - 1;
                for (; j >= 0 && curr < list[j]; --j)
                    list[j + 1] = list[j];

                list[j + 1] = curr;
            }
        }

        internal struct RenderBlocks : IDisposable
        {
            private NativeArray<RenderPassEvent> _blockEventLimits;
            private NativeArray<int> _blockRanges;
            private NativeArray<int> _blockRangeLengths;
            public RenderBlocks(List<ScriptableRenderPass> activeRenderPassQueue)
            {
                // Upper limits for each block. Each block will contains render passes with events below the limit.
                _blockEventLimits = new NativeArray<RenderPassEvent>(RENDER_PASS_BLOCK_COUNT, Allocator.Temp);
                _blockRanges = new NativeArray<int>(_blockEventLimits.Length + 1, Allocator.Temp);
                _blockRangeLengths = new NativeArray<int>(_blockRanges.Length, Allocator.Temp);

                _blockEventLimits[RenderPassBlock.BeforeRendering] = RenderPassEvent.BeforeRenderingPrePasses;
                _blockEventLimits[RenderPassBlock.MainRenderingOpaque] = RenderPassEvent.AfterRenderingOpaques;
                _blockEventLimits[RenderPassBlock.MainRenderingTransparent] = RenderPassEvent.AfterRenderingPostProcessing;
                _blockEventLimits[RenderPassBlock.AfterRendering] = (RenderPassEvent)Int32.MaxValue;

                // blockRanges[0] is always 0
                // blockRanges[i] is the index of the first RenderPass found in m_ActiveRenderPassQueue that has a ScriptableRenderPass.renderPassEvent higher than blockEventLimits[i] (i.e, should be executed after blockEventLimits[i])
                // blockRanges[blockEventLimits.Length] is m_ActiveRenderPassQueue.Count
                FillBlockRanges(activeRenderPassQueue);
                _blockEventLimits.Dispose();

                for (int i = 0; i < _blockRanges.Length - 1; i++)
                {
                    _blockRangeLengths[i] = _blockRanges[i + 1] - _blockRanges[i];
                }
            }

            //  RAII like Dispose pattern implementation for 'using' keyword
            public void Dispose()
            {
                _blockRangeLengths.Dispose();
                _blockRanges.Dispose();
            }

            // Fill in render pass indices for each block. End index is startIndex + 1.
            void FillBlockRanges(List<ScriptableRenderPass> activeRenderPassQueue)
            {
                int currRangeIndex = 0;
                int currRenderPass = 0;
                _blockRanges[currRangeIndex++] = 0;

                // For each block, it finds the first render pass index that has an event
                // higher than the block limit.
                for (int i = 0; i < _blockEventLimits.Length - 1; ++i)
                {
                    while (currRenderPass < activeRenderPassQueue.Count &&
                           activeRenderPassQueue[currRenderPass].RenderPassEvent < _blockEventLimits[i])
                        currRenderPass++;

                    _blockRanges[currRangeIndex++] = currRenderPass;
                }

                _blockRanges[currRangeIndex] = activeRenderPassQueue.Count;
            }

            public int GetLength(int index)
            {
                return _blockRangeLengths[index];
            }

            // Minimal foreach support
            public struct BlockRange : IDisposable
            {
                private int _current;
                private int _end;
                public BlockRange(int begin, int end)
                {
                    UnityEngine.Assertions.Assert.IsTrue(begin <= end);
                    _current = begin < end ? begin : end;
                    _end = end >= begin ? end : begin;
                    _current -= 1;
                }

                public BlockRange GetEnumerator() { return this; }
                public bool MoveNext() { return ++_current < _end; }
                public int Current { get => _current; }
                public void Dispose() { }
            }

            public BlockRange GetRange(int index)
            {
                return new BlockRange(_blockRanges[index], _blockRanges[index + 1]);
            }
        }
    }
}

