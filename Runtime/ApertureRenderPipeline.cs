using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Lightmapping = UnityEngine.Experimental.GlobalIllumination.Lightmapping;

namespace Portal.Rendering.Aperture
{
    /// <summary>
    /// 
    /// </summary>
    public partial class ApertureRenderPipeline : RenderPipeline
    {
        private static class Profiling
        {
            private static Dictionary<int, ProfilingSampler> s_hashSamplerCache = new Dictionary<int, ProfilingSampler>();
            public static readonly ProfilingSampler UnknownSampler = new ProfilingSampler("Unknown");

            // Specialization for camera loop to avoid allocations.
            public static ProfilingSampler TryGetOrAddCameraSampler(Camera camera)
            {
#if UNIVERSAL_PROFILING_NO_ALLOC
                return unknownSampler;
#else
                ProfilingSampler ps = null;
                int cameraId = camera.GetHashCode();
                bool exists = s_hashSamplerCache.TryGetValue(cameraId, out ps);
                if (!exists)
                {
                    // NOTE: camera.name allocates!
                    ps = new ProfilingSampler($"{nameof(ApertureRenderPipeline)}.{nameof(RenderSingleCamera)}: {camera.name}");
                    s_hashSamplerCache.Add(cameraId, ps);
                }
                return ps;
#endif
            }

            public static class Pipeline
            {
                // TODO: Would be better to add Profiling name hooks into RenderPipeline.cs, requires changes outside of Universal.
                public static readonly ProfilingSampler beginContextRendering = new ProfilingSampler($"{nameof(RenderPipeline)}.{nameof(BeginContextRendering)}");
                public static readonly ProfilingSampler endContextRendering = new ProfilingSampler($"{nameof(RenderPipeline)}.{nameof(EndContextRendering)}");
                public static readonly ProfilingSampler beginCameraRendering = new ProfilingSampler($"{nameof(RenderPipeline)}.{nameof(BeginCameraRendering)}");
                public static readonly ProfilingSampler endCameraRendering = new ProfilingSampler($"{nameof(RenderPipeline)}.{nameof(EndCameraRendering)}");

                const string K_NAME = nameof(ApertureRenderPipeline);
                public static readonly ProfilingSampler initializeCameraData = new ProfilingSampler($"{K_NAME}.{nameof(InitializeCameraData)}");
                public static readonly ProfilingSampler initializeStackedCameraData = new ProfilingSampler($"{K_NAME}.{nameof(InitializeStackedCameraData)}");
                public static readonly ProfilingSampler initializeAdditionalCameraData = new ProfilingSampler($"{K_NAME}.{nameof(InitializeAdditionalCameraData)}");
                public static readonly ProfilingSampler initializeRenderingData = new ProfilingSampler($"{K_NAME}.{nameof(InitializeRenderingData)}");
                public static readonly ProfilingSampler initializeShadowData = new ProfilingSampler($"{K_NAME}.{nameof(InitializeShadowData)}");
                public static readonly ProfilingSampler initializeLightData = new ProfilingSampler($"{K_NAME}.{nameof(InitializeLightData)}");
                public static readonly ProfilingSampler getPerObjectLightFlags = new ProfilingSampler($"{K_NAME}.{nameof(GetPerObjectLightFlags)}");
                public static readonly ProfilingSampler getMainLightIndex = new ProfilingSampler($"{K_NAME}.{nameof(GetMainLightIndex)}");
                public static readonly ProfilingSampler setupPerFrameShaderConstants = new ProfilingSampler($"{K_NAME}.{nameof(SetupPerFrameShaderConstants)}");

                public static class Renderer
                {
                    const string K_NAME = nameof(ScriptableRenderer);
                    public static readonly ProfilingSampler setupCullingParameters = new ProfilingSampler($"{K_NAME}.{nameof(ScriptableRenderer.SetupCullingParameters)}");
                    public static readonly ProfilingSampler setup = new ProfilingSampler($"{K_NAME}.{nameof(ScriptableRenderer.Setup)}");
                };

                public static class Context
                {
                    const string K_NAME = nameof(Context);
                    public static readonly ProfilingSampler submit = new ProfilingSampler($"{K_NAME}.{nameof(ScriptableRenderContext.Submit)}");
                };
            };
        }

        public static float MaxShadowBias
        {
            get => 10.0f;
        }

        public static float MinRenderScale
        {
            get => 0.1f;
        }

        public static float MaxRenderScale
        {
            get => 2.0f;
        }

        // Amount of Lights that can be shaded per object (in the for loop in the shader)
        public static int MaxPerObjectLights
        {
            // No support to bitfield mask and int[] in gles2. Can't index fast more than 4 lights.
            // Check Lighting.hlsl for more details.
            get => (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2) ? 4 : 8;
        }

        // These limits have to match same limits in Input.hlsl
        internal const int MAX_VISIBLE_ADDITIONAL_LIGHTS_MOBILE_SHADER_LEVEL_LESS_THAN_45 = 16;
        internal const int MAX_VISIBLE_ADDITIONAL_LIGHTS_MOBILE = 32;
        internal const int MAX_VISIBLE_ADDITIONAL_LIGHTS_NO_MOBILE = 256;

        public static int MaxVisibleAdditionalLights
        {
            get
            {
                bool isMobile = Application.isMobilePlatform;
                if (isMobile && (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && Graphics.minOpenGLESVersion <= OpenGLESVersion.OpenGLES30)))
                    return MAX_VISIBLE_ADDITIONAL_LIGHTS_MOBILE_SHADER_LEVEL_LESS_THAN_45;

                // GLES can be selected as platform on Windows (not a mobile platform) but uniform buffer size so we must use a low light count.
                return (isMobile || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3)
                    ? MAX_VISIBLE_ADDITIONAL_LIGHTS_MOBILE : MAX_VISIBLE_ADDITIONAL_LIGHTS_NO_MOBILE;
            }
        }
        public ApertureRenderPipeline()
        {
            SetSupportedRenderingFeatures();
            RenderingUtils.ClearSystemInfoCache();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
        protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
        {
            Render(renderContext, new List<Camera>(cameras));
        }


        protected override void Render(ScriptableRenderContext renderContext, List<Camera> cameras)
        {
            using ProfilingScope profScope = new ProfilingScope(null, ProfilingSampler.Get(ARPProfileId.UniversalRenderTotal));

            using (new ProfilingScope(null, Profiling.Pipeline.beginContextRendering))
            {
                BeginContextRendering(renderContext, cameras);
            }

            GraphicsSettings.lightsUseLinearIntensity = (QualitySettings.activeColorSpace == ColorSpace.Linear);
            GraphicsSettings.useScriptableRenderPipelineBatching = asset.UseSRPBatcher;
            SetupPerFrameShaderConstants();
            SortCameras(cameras);
            for (int i = 0; i < cameras.Count; ++i)
            {
                Camera camera = cameras[i];

                using (new ProfilingScope(null, Profiling.Pipeline.beginCameraRendering))
                {
                    BeginCameraRendering(renderContext, camera);
                }

                if (IsGameCamera(camera))
                {
                    RenderCameraStack(renderContext, camera);
                }
                else
                {
                    RenderSingleCamera(renderContext, camera);
                }

                using (new ProfilingScope(null, Profiling.Pipeline.endCameraRendering))
                {
                    EndCameraRendering(renderContext, camera);
                }
            }
        }
        /// <summary>
        // Renders a camera stack. This method calls RenderSingleCamera for each valid camera in the stack.
        // The last camera resolves the final target to screen.
        /// </summary>
        /// <param name="context">Render context used to record commands during execution.</param>
        /// <param name="camera">Camera to render.</param>
        static void RenderCameraStack(ScriptableRenderContext context, Camera baseCamera)
        {
            using ProfilingScope ProfilingScope = new ProfilingScope(null, ProfilingSampler.Get(ARPProfileId.RenderCameraStack));
            RenderSingleCamera(context, baseCamera);
        }

        /// <summary>
        /// Standalone camera rendering. Use this to render procedural cameras.
        /// This method doesn't call <c>BeginCameraRendering</c> and <c>EndCameraRendering</c> callbacks.
        /// </summary>
        /// <param name="context">Render context used to record commands during execution.</param>
        /// <param name="camera">Camera to render.</param>
        /// <seealso cref="ScriptableRenderContext"/>
        /// 
        public static void RenderSingleCamera(ScriptableRenderContext context, Camera camera)
        {
            ApertureAdditionalCameraData additionalCameraData = null;
            if (IsGameCamera(camera))
                camera.gameObject.TryGetComponent(out additionalCameraData);

            if (additionalCameraData != null && additionalCameraData.RenderType != CameraRenderType.Base)
            {
                Debug.LogWarning("Only Base cameras can be rendered with standalone RenderSingleCamera. Camera will be skipped.");
                return;
            }

            InitializeCameraData(camera, additionalCameraData, true, out CameraData cameraData);
#if ADAPTIVE_PERFORMANCE_2_0_0_OR_NEWER
                        if (asset.useAdaptivePerformance)
                            ApplyAdaptivePerformance(ref cameraData);
#endif
            RenderSingleCamera(context, cameraData, cameraData.PostProcessEnabled);
        }

        /// <summary>
        /// Renders a single camera. This method will do culling, setup and execution of the renderer.
        /// </summary>
        /// <param name="context">Render context used to record commands during execution.</param>
        /// <param name="cameraData">Camera rendering data. This might contain data inherited from a base camera.</param>
        /// <param name="anyPostProcessingEnabled">True if at least one camera has post-processing enabled in the stack, false otherwise.</param>
        static void RenderSingleCamera(ScriptableRenderContext context, CameraData cameraData, bool anyPostProcessingEnabled)
        {
            Camera camera = cameraData.Camera;
            ScriptableRenderer renderer = cameraData.Renderer;
            if (renderer == null)
            {
                Debug.LogWarning(string.Format("Trying to render {0} with an invalid renderer. Camera rendering will be skipped.", camera.name));
                return;
            }

            if (!TryGetCullingParameters(cameraData, out var cullingParameters))
                return;

            ScriptableRenderer.s_current = renderer;
            bool isSceneViewCamera = cameraData.IsSceneViewCamera;

            // NOTE: Do NOT mix ProfilingScope with named CommandBuffers i.e. CommandBufferPool.Get("name").
            // Currently there's an issue which results in mismatched markers.
            // The named CommandBuffer will close its "profiling scope" on execution.
            // That will orphan ProfilingScope markers as the named CommandBuffer markers are their parents.
            // Resulting in following pattern:
            // exec(cmd.start, scope.start, cmd.end) and exec(cmd.start, scope.end, cmd.end)
            CommandBuffer cmd = CommandBufferPool.Get();

            // TODO: move skybox code from C++ to URP in order to remove the call to context.Submit() inside DrawSkyboxPass
            // Until then, we can't use nested profiling scopes with XR multipass
            CommandBuffer cmdScope = cmd;

            ProfilingSampler sampler = Profiling.TryGetOrAddCameraSampler(camera);
            using (new ProfilingScope(cmdScope, sampler)) // Enqueues a "BeginSample" command into the CommandBuffer cmd
            {
                renderer.Clear(cameraData.RenderType);

                using (new ProfilingScope(cmd, Profiling.Pipeline.Renderer.setupCullingParameters))
                {
                    renderer.SetupCullingParameters(ref cullingParameters, ref cameraData);
                }

                context.ExecuteCommandBuffer(cmd); // Send all the commands enqueued so far in the CommandBuffer cmd, to the ScriptableRenderContext context
                cmd.Clear();

#if UNITY_EDITOR
                // Emit scene view UI
                if (isSceneViewCamera)
                {
                    ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
                }
#endif

                var cullResults = context.Cull(ref cullingParameters);
                InitializeRenderingData(asset, ref cameraData, ref cullResults, anyPostProcessingEnabled, out RenderingData renderingData);

#if ADAPTIVE_PERFORMANCE_2_0_0_OR_NEWER
                if (asset.useAdaptivePerformance)
                    ApplyAdaptivePerformance(ref renderingData);
#endif

                using (new ProfilingScope(cmd, Profiling.Pipeline.Renderer.setup))
                {
                    renderer.Setup(context, ref renderingData);
                }

                // Timing scope inside
                renderer.Execute(context, ref renderingData);
            } // When ProfilingSample goes out of scope, an "EndSample" command is enqueued into CommandBuffer cmd

            context.ExecuteCommandBuffer(cmd); // Sends to ScriptableRenderContext all the commands enqueued since cmd.Clear, i.e the "EndSample" command
            CommandBufferPool.Release(cmd);

            using (new ProfilingScope(cmd, Profiling.Pipeline.Context.submit))
            {
                context.Submit(); // Actually execute the commands that we previously sent to the ScriptableRenderContext context
            }

            ScriptableRenderer.s_current = null;
        }

        private Comparison<Camera> _cameraComparison = (camera1, camera2) => { return (int)camera1.depth - (int)camera2.depth; };
        void SortCameras(List<Camera> cameras)
        {
            if (cameras.Count > 1)
                cameras.Sort(_cameraComparison);
        }

        static bool TryGetCullingParameters(CameraData cameraData, out ScriptableCullingParameters cullingParams)
        {

            return cameraData.Camera.TryGetCullingParameters(false, out cullingParams);
        }
        static void InitializeCameraData(Camera camera, ApertureAdditionalCameraData additionalCameraData, bool resolveFinalTarget, out CameraData cameraData)
        {
            using ProfilingScope profScope = new ProfilingScope(null, Profiling.Pipeline.initializeCameraData);

            cameraData = new CameraData();
            InitializeStackedCameraData(camera, additionalCameraData, ref cameraData);
            InitializeAdditionalCameraData(camera, additionalCameraData, resolveFinalTarget, ref cameraData);

            ///////////////////////////////////////////////////////////////////
            // Descriptor settings                                            /
            ///////////////////////////////////////////////////////////////////

            ScriptableRenderer renderer = additionalCameraData?.ScriptableRenderer;
            bool rendererSupportsMSAA = renderer != null && renderer.SupportedRenderingFeatures.Msaa;

            int msaaSamples = 1;
            if (camera.allowMSAA && asset.MsaaSampleCount > 1 && rendererSupportsMSAA)
                msaaSamples = (camera.targetTexture != null) ? camera.targetTexture.antiAliasing : asset.MsaaSampleCount;

            bool needsAlphaChannel = Graphics.preserveFramebufferAlpha;
            cameraData.CameraTargetDescriptor = CreateRenderTextureDescriptor(camera, cameraData.RenderScale,
                cameraData.IsHdrEnabled, msaaSamples, needsAlphaChannel, cameraData.RequiresOpaqueTexture);
        }

        /// <summary>
        /// Initialize camera data settings common for all cameras in the stack. Overlay cameras will inherit
        /// settings from base camera.
        /// </summary>
        /// <param name="baseCamera">Base camera to inherit settings from.</param>
        /// <param name="baseAdditionalCameraData">Component that contains additional base camera data.</param>
        /// <param name="cameraData">Camera data to initialize setttings.</param>
        static void InitializeStackedCameraData(Camera baseCamera, ApertureAdditionalCameraData baseAdditionalCameraData, ref CameraData cameraData)
        {
            using ProfilingScope profScope = new ProfilingScope(null, Profiling.Pipeline.initializeStackedCameraData);

            var settings = asset;
            cameraData.TargetTexture = baseCamera.targetTexture;
            cameraData.CameraType = baseCamera.cameraType;
            bool isSceneViewCamera = cameraData.IsSceneViewCamera;

            ///////////////////////////////////////////////////////////////////
            // Environment and Post-processing settings                       /
            ///////////////////////////////////////////////////////////////////
            if (isSceneViewCamera)
            {
                cameraData.VolumeLayerMask = 1; // "Default"
                cameraData.VolumeTrigger = null;
                cameraData.IsStopNaNEnabled = false;
                cameraData.IsDitheringEnabled = false;
                cameraData.Antialiasing = AntialiasingMode.None;
                cameraData.AntialiasingQuality = AntialiasingQuality.High;
#if ENABLE_VR && ENABLE_XR_MODULE
                cameraData.xrRendering = false;
#endif
            }
            else if (baseAdditionalCameraData != null)
            {
                cameraData.VolumeLayerMask = baseAdditionalCameraData.VolumeLayerMask;
                cameraData.VolumeTrigger = baseAdditionalCameraData.VolumeTrigger == null ? baseCamera.transform : baseAdditionalCameraData.VolumeTrigger;
                cameraData.IsStopNaNEnabled = baseAdditionalCameraData.StopNaN && SystemInfo.graphicsShaderLevel >= 35;
                cameraData.IsDitheringEnabled = baseAdditionalCameraData.Dithering;
                cameraData.Antialiasing = baseAdditionalCameraData.Antialiasing;
                cameraData.AntialiasingQuality = baseAdditionalCameraData.AntialiasingQuality;
            }
            else
            {
                cameraData.VolumeLayerMask = 1; // "Default"
                cameraData.VolumeTrigger = null;
                cameraData.IsStopNaNEnabled = false;
                cameraData.IsDitheringEnabled = false;
                cameraData.Antialiasing = AntialiasingMode.None;
                cameraData.AntialiasingQuality = AntialiasingQuality.High;
            }

            ///////////////////////////////////////////////////////////////////
            // Settings that control output of the camera                     /
            ///////////////////////////////////////////////////////////////////

            cameraData.IsHdrEnabled = baseCamera.allowHDR && settings.SupportsHDR;

            Rect cameraRect = baseCamera.rect;
            cameraData._pixelRect = baseCamera.pixelRect;
            cameraData._pixelWidth = baseCamera.pixelWidth;
            cameraData._pixelHeight = baseCamera.pixelHeight;
            cameraData._aspectRatio = (float)cameraData._pixelWidth / (float)cameraData._pixelHeight;
            cameraData.IsDefaultViewport = (!(Math.Abs(cameraRect.x) > 0.0f || Math.Abs(cameraRect.y) > 0.0f ||
                Math.Abs(cameraRect.width) < 1.0f || Math.Abs(cameraRect.height) < 1.0f));

            // Discard variations lesser than kRenderScaleThreshold.
            // Scale is only enabled for gameview.
            const float kRenderScaleThreshold = 0.05f;
            cameraData.RenderScale = (Mathf.Abs(1.0f - settings.RenderScale) < kRenderScaleThreshold) ? 1.0f : settings.RenderScale;

            SortingCriteria commonOpaqueFlags = SortingCriteria.CommonOpaque;
            SortingCriteria noFrontToBackOpaqueFlags = SortingCriteria.SortingLayer | SortingCriteria.RenderQueue | SortingCriteria.OptimizeStateChanges | SortingCriteria.CanvasOrder;
            bool hasHSRGPU = SystemInfo.hasHiddenSurfaceRemovalOnGPU;
            bool canSkipFrontToBackSorting = (baseCamera.opaqueSortMode == OpaqueSortMode.Default && hasHSRGPU) || baseCamera.opaqueSortMode == OpaqueSortMode.NoDistanceSort;

            cameraData.DefaultOpaqueSortFlags = canSkipFrontToBackSorting ? noFrontToBackOpaqueFlags : commonOpaqueFlags;
            cameraData.CaptureActions = CameraCaptureBridge.GetCaptureActions(baseCamera);
        }

        /// <summary>
        /// Initialize settings that can be different for each camera in the stack.
        /// </summary>
        /// <param name="camera">Camera to initialize settings from.</param>
        /// <param name="additionalCameraData">Additional camera data component to initialize settings from.</param>
        /// <param name="resolveFinalTarget">True if this is the last camera in the stack and rendering should resolve to camera target.</param>
        /// <param name="cameraData">Settings to be initilized.</param>
        static void InitializeAdditionalCameraData(Camera camera, ApertureAdditionalCameraData additionalCameraData, bool resolveFinalTarget, ref CameraData cameraData)
        {
            using ProfilingScope profScope = new ProfilingScope(null, Profiling.Pipeline.initializeAdditionalCameraData);

            ApertureRenderPipelineAsset settings = asset;
            cameraData.Camera = camera;

            bool anyShadowsEnabled = settings.SupportsMainLightShadows || settings.SupportsAdditionalLightShadows;
            cameraData.MaxShadowDistance = Mathf.Min(settings.ShadowDistance, camera.farClipPlane);
            cameraData.MaxShadowDistance = (anyShadowsEnabled && cameraData.MaxShadowDistance >= camera.nearClipPlane) ? cameraData.MaxShadowDistance : 0.0f;

            // Getting the background color from preferences to add to the preview camera
#if UNITY_EDITOR
            if (cameraData.Camera.cameraType == CameraType.Preview)
            {
                camera.backgroundColor = CoreRenderPipelinePreferences.previewBackgroundColor;
            }
#endif

            bool isSceneViewCamera = cameraData.IsSceneViewCamera;
            if (isSceneViewCamera)
            {
                cameraData.RenderType = CameraRenderType.Base;
                cameraData.ClearDepth = true;
                cameraData.PostProcessEnabled = CoreUtils.ArePostProcessesEnabled(camera);
                cameraData.RequiresDepthTexture = settings.SupportsCameraDepthTexture;
                cameraData.RequiresOpaqueTexture = settings.SupportsCameraOpaqueTexture;
                cameraData.Renderer = asset.ScriptableRenderer;
            }
            else if (additionalCameraData != null)
            {
                cameraData.RenderType = additionalCameraData.RenderType;
                cameraData.ClearDepth = (additionalCameraData.RenderType != CameraRenderType.Base) ? additionalCameraData.ClearDepth : true;
                cameraData.PostProcessEnabled = additionalCameraData.RenderPostProcessing;
                cameraData.MaxShadowDistance = (additionalCameraData.RenderShadows) ? cameraData.MaxShadowDistance : 0.0f;
                cameraData.RequiresDepthTexture = additionalCameraData.RequiresDepthTexture;
                cameraData.RequiresOpaqueTexture = additionalCameraData.RequiresColorTexture;
                cameraData.Renderer = additionalCameraData.ScriptableRenderer;
            }
            else
            {
                cameraData.RenderType = CameraRenderType.Base;
                cameraData.ClearDepth = true;
                cameraData.PostProcessEnabled = false;
                cameraData.RequiresDepthTexture = settings.SupportsCameraDepthTexture;
                cameraData.RequiresOpaqueTexture = settings.SupportsCameraOpaqueTexture;
                cameraData.Renderer = asset.ScriptableRenderer;
            }

            // Disables post if GLes2
            cameraData.PostProcessEnabled &= SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2;

            cameraData.RequiresDepthTexture |= isSceneViewCamera || CheckPostProcessForDepth(cameraData);
            cameraData.ResolveFinalTarget = resolveFinalTarget;

            // Disable depth and color copy. We should add it in the renderer instead to avoid performance pitfalls
            // of camera stacking breaking render pass execution implicitly.
            bool isOverlayCamera = (cameraData.RenderType == CameraRenderType.Overlay);
            if (isOverlayCamera)
            {
                cameraData.RequiresDepthTexture = false;
                cameraData.RequiresOpaqueTexture = false;
            }

            Matrix4x4 projectionMatrix = camera.projectionMatrix;

            // Overlay cameras inherit viewport from base.
            // If the viewport is different between them we might need to patch the projection to adjust aspect ratio
            // matrix to prevent squishing when rendering objects in overlay cameras.
            if (isOverlayCamera && !camera.orthographic && cameraData._pixelRect != camera.pixelRect)
            {
                // m00 = (cotangent / aspect), therefore m00 * aspect gives us cotangent.
                float cotangent = camera.projectionMatrix.m00 * camera.aspect;

                // Get new m00 by dividing by base camera aspectRatio.
                float newCotangent = cotangent / cameraData._aspectRatio;
                projectionMatrix.m00 = newCotangent;
            }

            cameraData.SetViewAndProjectionMatrix(camera.worldToCameraMatrix, projectionMatrix);
        }

        static void InitializeRenderingData(ApertureRenderPipelineAsset settings, ref CameraData cameraData, ref CullingResults cullResults,
    bool anyPostProcessingEnabled, out RenderingData renderingData)
        {
            using ProfilingScope profScope = new ProfilingScope(null, Profiling.Pipeline.initializeRenderingData);

            var visibleLights = cullResults.visibleLights;

            int mainLightIndex = GetMainLightIndex(settings, visibleLights);
            bool mainLightCastShadows = false;
            bool additionalLightsCastShadows = false;

            if (cameraData.MaxShadowDistance > 0.0f)
            {
                mainLightCastShadows = (mainLightIndex != -1 && visibleLights[mainLightIndex].light != null &&
                    visibleLights[mainLightIndex].light.shadows != LightShadows.None);

                // If additional lights are shaded per-pixel they cannot cast shadows
                if (settings.AdditionalLightsRenderingMode == LightRenderingMode.PerPixel)
                {
                    for (int i = 0; i < visibleLights.Length; ++i)
                    {
                        if (i == mainLightIndex)
                            continue;

                        Light light = visibleLights[i].light;

                        // ApertureRP doesn't support additional directional light shadows yet
                        if ((visibleLights[i].lightType == LightType.Spot || visibleLights[i].lightType == LightType.Point) && light != null && light.shadows != LightShadows.None)
                        {
                            additionalLightsCastShadows = true;
                            break;
                        }
                    }
                }
            }

            renderingData.CullResults = cullResults;
            renderingData.CameraData = cameraData;
            InitializeLightData(settings, visibleLights, mainLightIndex, out renderingData.LightData);
            InitializeShadowData(settings, visibleLights, mainLightCastShadows, additionalLightsCastShadows && !renderingData.LightData.shadeAdditionalLightsPerVertex, out renderingData.ShadowData);
            InitializePostProcessingData(settings, out renderingData.PostProcessingData);
            renderingData.SupportsDynamicBatching = settings.SupportsDynamicBatching;
            renderingData.PerObjectData = GetPerObjectLightFlags(renderingData.LightData.additionalLightsCount);
            renderingData.PerObjectData = GetPerObjectLightFlags(0);
            renderingData.PostProcessingEnabled = anyPostProcessingEnabled;
        }
        static void InitializeShadowData(ApertureRenderPipelineAsset settings, NativeArray<VisibleLight> visibleLights, bool mainLightCastShadows, bool additionalLightsCastShadows, out ShadowData shadowData)
        {
            using ProfilingScope profScope = new ProfilingScope(null, Profiling.Pipeline.initializeShadowData);

            s_shadowBiasData.Clear();
            s_shadowResolutionData.Clear();

            for (int i = 0; i < visibleLights.Length; ++i)
            {
                Light light = visibleLights[i].light;
                ApertureAdditionalLightData data = null;
                if (light != null)
                {
                    light.gameObject.TryGetComponent(out data);
                }

                if (data && !data.UsePipelineSettings)
                    s_shadowBiasData.Add(new Vector4(light.shadowBias, light.shadowNormalBias, 0.0f, 0.0f));
                else
                    s_shadowBiasData.Add(new Vector4(settings.ShadowDepthBias, settings.ShadowNormalBias, 0.0f, 0.0f));

                if (data && (data.AdditionalLightsShadowResolutionTier == ApertureAdditionalLightData.AdditionalLightsShadowResolutionTierCustom))
                {
                    s_shadowResolutionData.Add((int)light.shadowResolution); // native code does not clamp light.shadowResolution between -1 and 3
                }
                else if (data && (data.AdditionalLightsShadowResolutionTier != ApertureAdditionalLightData.AdditionalLightsShadowResolutionTierCustom))
                {
                    int resolutionTier = Mathf.Clamp(data.AdditionalLightsShadowResolutionTier, ApertureAdditionalLightData.AdditionalLightsShadowResolutionTierLow, ApertureAdditionalLightData.AdditionalLightsShadowResolutionTierHigh);
                    s_shadowResolutionData.Add(settings.GetAdditionalLightsShadowResolution(resolutionTier));
                }
                else
                {
                    s_shadowResolutionData.Add(settings.GetAdditionalLightsShadowResolution(ApertureAdditionalLightData.AdditionalLightsShadowDefaultResolutionTier));
                }
            }

            shadowData.bias = s_shadowBiasData;
            shadowData.resolution = s_shadowResolutionData;
            shadowData.supportsMainLightShadows = SystemInfo.supportsShadows && settings.SupportsMainLightShadows && mainLightCastShadows;

            shadowData.mainLightShadowCascadesCount = settings.ShadowCascadeCount;
            shadowData.mainLightShadowmapWidth = settings.MainLightShadowmapResolution;
            shadowData.mainLightShadowmapHeight = settings.MainLightShadowmapResolution;

            switch (shadowData.mainLightShadowCascadesCount)
            {
                case 1:
                    shadowData.mainLightShadowCascadesSplit = new Vector3(1.0f, 0.0f, 0.0f);
                    break;

                case 2:
                    shadowData.mainLightShadowCascadesSplit = new Vector3(settings.Cascade2Split, 1.0f, 0.0f);
                    break;

                case 3:
                    shadowData.mainLightShadowCascadesSplit = new Vector3(settings.Cascade3Split.x, settings.Cascade3Split.y, 0.0f);
                    break;

                default:
                    shadowData.mainLightShadowCascadesSplit = settings.Cascade4Split;
                    break;
            }

            shadowData.supportsAdditionalLightShadows = SystemInfo.supportsShadows && settings.SupportsAdditionalLightShadows && additionalLightsCastShadows;
            shadowData.additionalLightsShadowmapWidth = shadowData.additionalLightsShadowmapHeight = settings.AdditionalLightsShadowmapResolution;
            shadowData.supportsSoftShadows = settings.SupportsSoftShadows && (shadowData.supportsMainLightShadows || shadowData.supportsAdditionalLightShadows);
            shadowData.shadowmapDepthBufferBits = 16;
        }

        static void InitializePostProcessingData(ApertureRenderPipelineAsset settings, out PostProcessingData postProcessingData)
        {
            postProcessingData.gradingMode = settings.SupportsHDR
                ? settings.ColorGradingMode
                : ColorGradingMode.LowDynamicRange;

            postProcessingData.lutSize = settings.ColorGradingLutSize;
            postProcessingData.useFastSRGBLinearConversion = settings.UseFastSRGBLinearConversion;
        }

        static void InitializeLightData(ApertureRenderPipelineAsset settings, NativeArray<VisibleLight> visibleLights, int mainLightIndex, out LightData lightData)
        {
            using ProfilingScope profScope = new ProfilingScope(null, Profiling.Pipeline.initializeLightData);

            int maxPerObjectAdditionalLights = ApertureRenderPipeline.MaxPerObjectLights;
            int maxVisibleAdditionalLights = ApertureRenderPipeline.MaxVisibleAdditionalLights;

            lightData.mainLightIndex = mainLightIndex;

            if (settings.AdditionalLightsRenderingMode != LightRenderingMode.Disabled)
            {
                lightData.additionalLightsCount =
                    Math.Min((mainLightIndex != -1) ? visibleLights.Length - 1 : visibleLights.Length,
                        maxVisibleAdditionalLights);
                lightData.maxPerObjectAdditionalLightsCount = Math.Min(settings.MaxAdditionalLightsCount, maxPerObjectAdditionalLights);
            }
            else
            {
                lightData.additionalLightsCount = 0;
                lightData.maxPerObjectAdditionalLightsCount = 0;
            }

            lightData.shadeAdditionalLightsPerVertex = settings.AdditionalLightsRenderingMode == LightRenderingMode.PerVertex;
            lightData.visibleLights = visibleLights;
            lightData.supportsMixedLighting = settings.SupportsMixedLighting;
        }

        public static bool IsGameCamera(Camera camera)
        {
            if (camera == null)
                throw new ArgumentNullException("camera");

            return camera.cameraType == CameraType.Game || camera.cameraType == CameraType.VR;
        }

        static bool CheckPostProcessForDepth(in CameraData cameraData)
        {
            if (!cameraData.PostProcessEnabled)
                return false;

            if (cameraData.Antialiasing == AntialiasingMode.SubpixelMorphologicalAntiAliasing)
                return true;

            //var stack = VolumeManager.instance.stack;

            //if (stack.GetComponent<DepthOfField>().IsActive())
            //    return true;

            //if (stack.GetComponent<MotionBlur>().IsActive())
            //    return true;

            return false;
        }

        static PerObjectData GetPerObjectLightFlags(int additionalLightsCount)
        {
            using ProfilingScope profScope = new ProfilingScope(null, Profiling.Pipeline.getPerObjectLightFlags);

            var configuration = PerObjectData.ReflectionProbes | PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.LightData | PerObjectData.OcclusionProbe | PerObjectData.ShadowMask;

            if (additionalLightsCount > 0)
            {
                configuration |= PerObjectData.LightData;

                // In this case we also need per-object indices (unity_LightIndices)
                if (!RenderingUtils.useStructuredBuffer)
                    configuration |= PerObjectData.LightIndices;
            }

            return configuration;
        }

        // Main Light is always a directional light
        static int GetMainLightIndex(ApertureRenderPipelineAsset settings, NativeArray<VisibleLight> visibleLights)
        {
            using ProfilingScope profScope = new ProfilingScope(null, Profiling.Pipeline.getMainLightIndex);

            int totalVisibleLights = visibleLights.Length;

            if (totalVisibleLights == 0 || settings.MainLightRenderingMode != LightRenderingMode.PerPixel)
                return -1;

            Light sunLight = RenderSettings.sun;
            int brightestDirectionalLightIndex = -1;
            float brightestLightIntensity = 0.0f;
            for (int i = 0; i < totalVisibleLights; ++i)
            {
                VisibleLight currVisibleLight = visibleLights[i];
                Light currLight = currVisibleLight.light;

                // Particle system lights have the light property as null. We sort lights so all particles lights
                // come last. Therefore, if first light is particle light then all lights are particle lights.
                // In this case we either have no main light or already found it.
                if (currLight == null)
                    break;

                if (currVisibleLight.lightType == LightType.Directional)
                {
                    // Sun source needs be a directional light
                    if (currLight == sunLight)
                        return i;

                    // In case no sun light is present we will return the brightest directional light
                    if (currLight.intensity > brightestLightIntensity)
                    {
                        brightestLightIntensity = currLight.intensity;
                        brightestDirectionalLightIndex = i;
                    }
                }
            }

            return brightestDirectionalLightIndex;
        }

        static void SetSupportedRenderingFeatures()
        {
#if UNITY_EDITOR
            SupportedRenderingFeatures.active = new SupportedRenderingFeatures()
            {
                reflectionProbeModes = SupportedRenderingFeatures.ReflectionProbeModes.None,
                defaultMixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.Subtractive,
                mixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.Subtractive | SupportedRenderingFeatures.LightmapMixedBakeModes.IndirectOnly | SupportedRenderingFeatures.LightmapMixedBakeModes.Shadowmask,
                lightmapBakeTypes = LightmapBakeType.Baked | LightmapBakeType.Mixed,
                lightmapsModes = LightmapsMode.CombinedDirectional | LightmapsMode.NonDirectional,
                lightProbeProxyVolumes = false,
                motionVectors = false,
                receiveShadows = false,
                reflectionProbes = true,
                particleSystemInstancing = true
            };
            SceneViewDrawMode.SetupDrawMode();
#endif
        }

        static void SetupPerFrameShaderConstants()
        {
            using var profScope = new ProfilingScope(null, Profiling.Pipeline.setupPerFrameShaderConstants);

            // When glossy reflections are OFF in the shader we set a constant color to use as indirect specular
            SphericalHarmonicsL2 ambientSH = RenderSettings.ambientProbe;
            Color linearGlossyEnvColor = new Color(ambientSH[0, 0], ambientSH[1, 0], ambientSH[2, 0]) * RenderSettings.reflectionIntensity;
            Color glossyEnvColor = CoreUtils.ConvertLinearToActiveColorSpace(linearGlossyEnvColor);
            Shader.SetGlobalVector(ShaderPropertyId.GlossyEnvironmentColor, glossyEnvColor);

            // Ambient
            Shader.SetGlobalVector(ShaderPropertyId.AmbientSkyColor, CoreUtils.ConvertSRGBToActiveColorSpace(RenderSettings.ambientSkyColor));
            Shader.SetGlobalVector(ShaderPropertyId.AmbientEquatorColor, CoreUtils.ConvertSRGBToActiveColorSpace(RenderSettings.ambientEquatorColor));
            Shader.SetGlobalVector(ShaderPropertyId.AmbientGroundColor, CoreUtils.ConvertSRGBToActiveColorSpace(RenderSettings.ambientGroundColor));

            // Used when subtractive mode is selected
            Shader.SetGlobalVector(ShaderPropertyId.SubtractiveShadowColor, CoreUtils.ConvertSRGBToActiveColorSpace(RenderSettings.subtractiveShadowColor));

            // Required for 2D Unlit Shadergraph master node as it doesn't currently support hidden properties.
            //Shader.SetGlobalColor(ShaderPropertyId.rendererColor, Color.white);
        }

    }


}

