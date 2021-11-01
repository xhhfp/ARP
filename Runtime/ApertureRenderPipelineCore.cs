using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Portal.Rendering.Aperture
{
    public struct RenderingData
    {
        public CullingResults CullResults;
        public CameraData CameraData;
        public LightData LightData;
        public ShadowData ShadowData;
        public PostProcessingData PostProcessingData;
        public bool SupportsDynamicBatching;
        public PerObjectData PerObjectData;
        public bool PostProcessingEnabled;
    }
    public struct CameraData
    {
        // Internal camera data as we are not yet sure how to expose View in stereo context.
        // We might change this API soon.
        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projectionMatrix;

        internal void SetViewAndProjectionMatrix(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
        {
            _viewMatrix = viewMatrix;
            _projectionMatrix = projectionMatrix;
        }

        public Matrix4x4 GetViewMatrix(int viewIndex = 0)
        {
            return _viewMatrix;
        }

        public Matrix4x4 GetProjectionMatrix(int viewIndex = 0)
        {
            return _projectionMatrix;
        }

        /// <summary>
        /// Returns the camera GPU projection matrix. This contains platform specific changes to handle y-flip and reverse z.
        /// Similar to <c>GL.GetGPUProjectionMatrix</c> but queries URP internal state to know if the pipeline is rendering to render texture.
        /// For more info on platform differences regarding camera projection check: https://docs.unity3d.com/Manual/SL-PlatformDifferences.html
        /// </summary>
        /// <seealso cref="GL.GetGPUProjectionMatrix(Matrix4x4, bool)"/>
        /// <returns></returns>
        public Matrix4x4 GetGPUProjectionMatrix(int viewIndex = 0)
        {
            return GL.GetGPUProjectionMatrix(GetProjectionMatrix(viewIndex), IsCameraProjectionMatrixFlipped());
        }

        public Camera Camera;
        public CameraRenderType RenderType;
        public RenderTexture TargetTexture;
        public RenderTextureDescriptor CameraTargetDescriptor;
        internal Rect _pixelRect;
        internal int _pixelWidth;
        internal int _pixelHeight;
        internal float _aspectRatio;
        public float RenderScale;
        public bool ClearDepth;
        public CameraType CameraType;
        public bool IsDefaultViewport;
        public bool IsHdrEnabled;
        public bool RequiresDepthTexture;
        public bool RequiresOpaqueTexture;

        internal bool _requireSrgbConversion
        {
            get
            {
                return Display.main.requiresSrgbBlitToBackbuffer;
            }
        }

        /// <summary>
        /// True if the camera rendering is for the scene window in the editor
        /// </summary>
        public bool IsSceneViewCamera => CameraType == CameraType.SceneView;

        /// <summary>
        /// True if the camera rendering is for the preview window in the editor
        /// </summary>
        public bool IsPreviewCamera => CameraType == CameraType.Preview;

        /// <summary>
        /// True if the camera device projection matrix is flipped. This happens when the pipeline is rendering
        /// to a render texture in non OpenGL platforms. If you are doing a custom Blit pass to copy camera textures
        /// (_CameraColorTexture, _CameraDepthAttachment) you need to check this flag to know if you should flip the
        /// matrix when rendering with for cmd.Draw* and reading from camera textures.
        /// </summary>
        public bool IsCameraProjectionMatrixFlipped()
        {
            // Users only have access to CameraData on URP rendering scope. The current renderer should never be null.
            ScriptableRenderer renderer = ScriptableRenderer.s_current;
            Debug.Assert(renderer != null, "IsCameraProjectionMatrixFlipped is being called outside camera rendering scope.");

            if (renderer != null)
            {
                bool renderingToBackBufferTarget = renderer.CameraColorTarget == BuiltinRenderTextureType.CameraTarget;
                bool renderingToTexture = !renderingToBackBufferTarget || TargetTexture != null;
                return SystemInfo.graphicsUVStartsAtTop && renderingToTexture;
            }

            return true;
        }

        public SortingCriteria DefaultOpaqueSortFlags;

        public float MaxShadowDistance;
        public bool PostProcessEnabled;

        public IEnumerator<Action<RenderTargetIdentifier, CommandBuffer>> CaptureActions;

        public LayerMask VolumeLayerMask;
        public Transform VolumeTrigger;

        public bool IsStopNaNEnabled;
        public bool IsDitheringEnabled;
        public AntialiasingMode Antialiasing;
        public AntialiasingQuality AntialiasingQuality;

        public ScriptableRenderer Renderer;

        public bool ResolveFinalTarget;
    }

    public struct LightData
    {
        public int mainLightIndex;
        public int additionalLightsCount;
        public int maxPerObjectAdditionalLightsCount;
        public NativeArray<VisibleLight> visibleLights;
        public bool shadeAdditionalLightsPerVertex;
        public bool supportsMixedLighting;
    }

    public struct ShadowData
    {
        public bool supportsMainLightShadows;
        public int mainLightShadowmapWidth;
        public int mainLightShadowmapHeight;
        public int mainLightShadowCascadesCount;
        public Vector3 mainLightShadowCascadesSplit;
        public bool supportsAdditionalLightShadows;
        public int additionalLightsShadowmapWidth;
        public int additionalLightsShadowmapHeight;
        public bool supportsSoftShadows;
        public int shadowmapDepthBufferBits;
        public List<Vector4> bias;
        public List<int> resolution;
    }

    internal enum ARPProfileId
    {
        // CPU
        UniversalRenderTotal,
        UpdateVolumeFramework,
        RenderCameraStack,

        // GPU
        AdditionalLightsShadow,
        ColorGradingLUT,
        CopyColor,
        CopyDepth,
        DepthNormalPrepass,
        DepthPrepass,

        // DrawObjectsPass
        DrawOpaqueObjects,
        DrawTransparentObjects,

        // RenderObjectsPass
        //RenderObjects,

        MainLightShadow,
        ResolveShadows,
        SSAO,

        // PostProcessPass
        StopNaNs,
        SMAA,
        GaussianDepthOfField,
        BokehDepthOfField,
        MotionBlur,
        PaniniProjection,
        UberPostProcess,
        Bloom,

        FinalBlit
    }

    internal static class ShaderPropertyId
    {
        public static readonly int GlossyEnvironmentColor = Shader.PropertyToID("_GlossyEnvironmentColor");
        public static readonly int SubtractiveShadowColor = Shader.PropertyToID("_SubtractiveShadowColor");

        public static readonly int AmbientSkyColor = Shader.PropertyToID("unity_AmbientSky");
        public static readonly int AmbientEquatorColor = Shader.PropertyToID("unity_AmbientEquator");
        public static readonly int AmbientGroundColor = Shader.PropertyToID("unity_AmbientGround");

        public static readonly int Time = Shader.PropertyToID("_Time");
        public static readonly int SinTime = Shader.PropertyToID("_SinTime");
        public static readonly int CosTime = Shader.PropertyToID("_CosTime");
        public static readonly int DeltaTime = Shader.PropertyToID("unity_DeltaTime");
        public static readonly int TimeParameters = Shader.PropertyToID("_TimeParameters");

        public static readonly int ScaledScreenParams = Shader.PropertyToID("_ScaledScreenParams");
        public static readonly int WorldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
        public static readonly int ScreenParams = Shader.PropertyToID("_ScreenParams");
        public static readonly int ProjectionParams = Shader.PropertyToID("_ProjectionParams");
        public static readonly int ZBufferParams = Shader.PropertyToID("_ZBufferParams");
        public static readonly int OrthoParams = Shader.PropertyToID("unity_OrthoParams");

        public static readonly int InverseViewMatrix = Shader.PropertyToID("unity_MatrixInvV");
        public static readonly int InverseProjectionMatrix = Shader.PropertyToID("unity_MatrixInvP");
        public static readonly int InverseViewAndProjectionMatrix = Shader.PropertyToID("unity_MatrixInvVP");

        public static readonly int WorldToCameraMatrix = Shader.PropertyToID("unity_WorldToCamera");
        public static readonly int CameraToWorldMatrix = Shader.PropertyToID("unity_CameraToWorld");


        public static readonly int ScaleBiasRt = Shader.PropertyToID("_ScaleBiasRt"); 
    }
    public struct PostProcessingData
    {
        public ColorGradingMode gradingMode;
        public int lutSize;
        /// <summary>
        /// True if fast approximation functions are used when converting between the sRGB and Linear color spaces, false otherwise.
        /// </summary>
        public bool useFastSRGBLinearConversion;
    }
    public sealed partial class ApertureRenderPipeline
    {
        private static List<Vector4> s_shadowBiasData = new List<Vector4>();
        private static List<int> s_shadowResolutionData = new List<int>();
        public static ApertureRenderPipelineAsset asset
        {
            get => GraphicsSettings.currentRenderPipeline as ApertureRenderPipelineAsset;
        }

        static RenderTextureDescriptor CreateRenderTextureDescriptor(Camera camera, float renderScale,
    bool isHdrEnabled, int msaaSamples, bool needsAlpha, bool requiresOpaqueTexture)
        {
            RenderTextureDescriptor desc;
            GraphicsFormat renderTextureFormatDefault = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);

            if (camera.targetTexture == null)
            {
                desc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight);
                desc.width = (int)((float)desc.width * renderScale);
                desc.height = (int)((float)desc.height * renderScale);


                GraphicsFormat hdrFormat;
                if (!needsAlpha && RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.B10G11R11_UFloatPack32, FormatUsage.Linear | FormatUsage.Render))
                    hdrFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                else if (RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Linear | FormatUsage.Render))
                    hdrFormat = GraphicsFormat.R16G16B16A16_SFloat;
                else
                    hdrFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.HDR); // This might actually be a LDR format on old devices.

                desc.graphicsFormat = isHdrEnabled ? hdrFormat : renderTextureFormatDefault;
                desc.depthBufferBits = 32;
                desc.msaaSamples = msaaSamples;
                desc.sRGB = (QualitySettings.activeColorSpace == ColorSpace.Linear);
            }
            else
            {
                desc = camera.targetTexture.descriptor;
                desc.width = camera.pixelWidth;
                desc.height = camera.pixelHeight;
                if (camera.cameraType == CameraType.SceneView && !isHdrEnabled)
                {
                    desc.graphicsFormat = renderTextureFormatDefault;
                }
                // SystemInfo.SupportsRenderTextureFormat(camera.targetTexture.descriptor.colorFormat)
                // will assert on R8_SINT since it isn't a valid value of RenderTextureFormat.
                // If this is fixed then we can implement debug statement to the user explaining why some
                // RenderTextureFormats available resolves in a black render texture when no warning or error
                // is given.
            }

            desc.enableRandomWrite = false;
            desc.bindMS = false;
            desc.useDynamicScale = camera.allowDynamicResolution;

            // check that the requested MSAA samples count is supported by the current platform. If it's not supported,
            // replace the requested desc.msaaSamples value with the actual value the engine falls back to
            desc.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc);

            // if the target platform doesn't support storing multisampled RTs and we are doing a separate opaque pass, using a Load load action on the subsequent passes
            // will result in loading Resolved data, which on some platforms is discarded, resulting in losing the results of the previous passes.
            // As a workaround we disable MSAA to make sure that the results of previous passes are stored. (fix for Case 1247423).
            if (!SystemInfo.supportsStoreAndResolveAction && requiresOpaqueTexture)
                desc.msaaSamples = 1;

            return desc;
        }
    }
    public static class ShaderKeywordStrings
    {
        public static readonly string MainLightShadows = "_MAIN_LIGHT_SHADOWS";
        public static readonly string MainLightShadowCascades = "_MAIN_LIGHT_SHADOWS_CASCADE";
        public static readonly string MainLightShadowScreen = "_MAIN_LIGHT_SHADOWS_SCREEN";
        public static readonly string CastingPunctualLightShadow = "_CASTING_PUNCTUAL_LIGHT_SHADOW"; // This is used during shadow map generation to differentiate between directional and punctual light shadows, as they use different formulas to apply Normal Bias
        public static readonly string AdditionalLightsVertex = "_ADDITIONAL_LIGHTS_VERTEX";
        public static readonly string AdditionalLightsPixel = "_ADDITIONAL_LIGHTS";
        public static readonly string AdditionalLightShadows = "_ADDITIONAL_LIGHT_SHADOWS";
        public static readonly string SoftShadows = "_SHADOWS_SOFT";
        public static readonly string MixedLightingSubtractive = "_MIXED_LIGHTING_SUBTRACTIVE"; // Backward compatibility
        public static readonly string LightmapShadowMixing = "LIGHTMAP_SHADOW_MIXING";
        public static readonly string ShadowsShadowMask = "SHADOWS_SHADOWMASK";

        public static readonly string DepthNoMsaa = "_DEPTH_NO_MSAA";
        public static readonly string DepthMsaa2 = "_DEPTH_MSAA_2";
        public static readonly string DepthMsaa4 = "_DEPTH_MSAA_4";
        public static readonly string DepthMsaa8 = "_DEPTH_MSAA_8";

        public static readonly string LinearToSRGBConversion = "_LINEAR_TO_SRGB_CONVERSION";
        internal static readonly string UseFastSRGBLinearConversion = "_USE_FAST_SRGB_LINEAR_CONVERSION";

        public static readonly string SmaaLow = "_SMAA_PRESET_LOW";
        public static readonly string SmaaMedium = "_SMAA_PRESET_MEDIUM";
        public static readonly string SmaaHigh = "_SMAA_PRESET_HIGH";
        public static readonly string PaniniGeneric = "_GENERIC";
        public static readonly string PaniniUnitDistance = "_UNIT_DISTANCE";
        public static readonly string BloomLQ = "_BLOOM_LQ";
        public static readonly string BloomHQ = "_BLOOM_HQ";
        public static readonly string BloomLQDirt = "_BLOOM_LQ_DIRT";
        public static readonly string BloomHQDirt = "_BLOOM_HQ_DIRT";
        public static readonly string UseRGBM = "_USE_RGBM";
        public static readonly string Distortion = "_DISTORTION";
        public static readonly string ChromaticAberration = "_CHROMATIC_ABERRATION";
        public static readonly string HDRGrading = "_HDR_GRADING";
        public static readonly string TonemapACES = "_TONEMAP_ACES";
        public static readonly string TonemapNeutral = "_TONEMAP_NEUTRAL";
        public static readonly string FilmGrain = "_FILM_GRAIN";
        public static readonly string Fxaa = "_FXAA";
        public static readonly string Dithering = "_DITHERING";
        public static readonly string ScreenSpaceOcclusion = "_SCREEN_SPACE_OCCLUSION";

        public static readonly string HighQualitySampling = "_HIGH_QUALITY_SAMPLING";

        public static readonly string DOWNSAMPLING_SIZE_2 = "DOWNSAMPLING_SIZE_2";
        public static readonly string DOWNSAMPLING_SIZE_4 = "DOWNSAMPLING_SIZE_4";
        public static readonly string DOWNSAMPLING_SIZE_8 = "DOWNSAMPLING_SIZE_8";
        public static readonly string DOWNSAMPLING_SIZE_16 = "DOWNSAMPLING_SIZE_16";
        public static readonly string _SPOT = "_SPOT";
        public static readonly string _DIRECTIONAL = "_DIRECTIONAL";
        public static readonly string _POINT = "_POINT";
        public static readonly string _DEFERRED_ADDITIONAL_LIGHT_SHADOWS = "_DEFERRED_ADDITIONAL_LIGHT_SHADOWS";
        public static readonly string _GBUFFER_NORMALS_OCT = "_GBUFFER_NORMALS_OCT";
        public static readonly string _DEFERRED_MIXED_LIGHTING = "_DEFERRED_MIXED_LIGHTING";
        public static readonly string LIGHTMAP_ON = "LIGHTMAP_ON";
        public static readonly string _ALPHATEST_ON = "_ALPHATEST_ON";
        public static readonly string DIRLIGHTMAP_COMBINED = "DIRLIGHTMAP_COMBINED";
        public static readonly string _DETAIL_MULX2 = "_DETAIL_MULX2";
        public static readonly string _DETAIL_SCALED = "_DETAIL_SCALED";
        public static readonly string _CLEARCOAT = "_CLEARCOAT";
        public static readonly string _CLEARCOATMAP = "_CLEARCOATMAP";

        // XR
        public static readonly string UseDrawProcedural = "_USE_DRAW_PROCEDURAL";
    }
}

