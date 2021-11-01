
using UnityEngine;
using UnityEngine.Rendering;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
#endif
using System;

namespace Portal.Rendering.Aperture
{
    public enum ShadowQuality
    {
        Disabled,
        HardShadows,
        SoftShadows,
    }

    public enum ShadowResolution
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }

    public enum MsaaQuality
    {
        Disabled = 1,
        _2x = 2,
        _4x = 4,
        _8x = 8
    }

    public enum Downsampling
    {
        None,
        _2xBilinear,
        _4xBox,
        _4xBilinear
    }

    internal enum DefaultMaterialType
    {
        Standard,
        Particle,
        Terrain,
        Sprite,
        UnityBuiltinDefault
    }

    public enum LightRenderingMode
    {
        Disabled = 0,
        PerVertex = 2,
        PerPixel = 1,
    }

    public enum ShaderVariantLogLevel
    {
        Disabled,
        OnlyUniversalRPShaders,
        AllShaders,
    }

    public enum RendererType
    {
        Custom,
        ForwardRenderer,
        _2DRenderer,
    }

    public enum ColorGradingMode
    {
        LowDynamicRange,
        HighDynamicRange
    }
    [ExcludeFromPreset]
    public partial class ApertureRenderPipelineAsset : RenderPipelineAsset
    {
        private Shader _defaultShader;
        private ScriptableRenderer[] _renderers = new ScriptableRenderer[1];

        // Default values set when a new RenderPipeline asset is created
        [SerializeField] private int _assetVersion = 1;
        [SerializeField] private int _assetPreviousVersion = 1;

        // Renderer settings
        [SerializeField] internal ScriptableRendererData[] _rendererDataList = new ScriptableRendererData[1];
        [SerializeField] internal int _defaultRendererIndex = 0;

        // General settings
        [SerializeField] private bool _requireDepthTexture = false;
        [SerializeField] private bool _requireOpaqueTexture = false;

        // Quality settings
        [SerializeField] private bool _supportsHDR = true;
        [SerializeField] private MsaaQuality _msaa = MsaaQuality.Disabled;
        [SerializeField] private float _renderScale = 1.0f;

        // Main directional light Settings
        [SerializeField] private LightRenderingMode _mainLightRenderingMode = LightRenderingMode.PerPixel;
        [SerializeField] private bool _mainLightShadowsSupported = true;
        [SerializeField] private ShadowResolution _mainLightShadowmapResolution = ShadowResolution._2048;

        // Additional lights settings
        [SerializeField] private LightRenderingMode _additionalLightsRenderingMode = LightRenderingMode.PerPixel;
        [SerializeField] private int _additionalLightsPerObjectLimit = 4;
        [SerializeField] private bool _additionalLightShadowsSupported = false;
        [SerializeField] private ShadowResolution _additionalLightsShadowmapResolution = ShadowResolution._2048;

        [SerializeField] private int _additionalLightsShadowResolutionTierLow = AdditionalLightsDefaultShadowResolutionTierLow;
        [SerializeField] private int _additionalLightsShadowResolutionTierMedium = AdditionalLightsDefaultShadowResolutionTierMedium;
        [SerializeField] private int _additionalLightsShadowResolutionTierHigh = AdditionalLightsDefaultShadowResolutionTierHigh;

        // Shadows Settings
        [SerializeField] private float _shadowDistance = 50.0f;
        [SerializeField] private int _shadowCascadeCount = 1;
        [SerializeField] private float _cascade2Split = 0.25f;
        [SerializeField] private Vector2 _cascade3Split = new Vector2(0.1f, 0.3f);
        [SerializeField] private Vector3 _cascade4Split = new Vector3(0.067f, 0.2f, 0.467f);
        [SerializeField] private float _shadowDepthBias = 1.0f;
        [SerializeField] private float _shadowNormalBias = 1.0f;
        [SerializeField] private bool _softShadowsSupported = false;

        // Advanced settings
        [SerializeField] private bool _useSRPBatcher = true;
        [SerializeField] private bool _supportsDynamicBatching = false;
        [SerializeField] private bool _mixedLightingSupported = true;

        // Post-processing settings
        [SerializeField] private ColorGradingMode _colorGradingMode = ColorGradingMode.LowDynamicRange;
        [SerializeField] private int _colorGradingLutSize = 32;
        [SerializeField] private bool _useFastSRGBLinearConversion = false;

        public const int MIN_LUT_SIZE = 16;
        public const int MAX_LUT_SIZE = 65;

        internal const int SHADOW_CASCADE_MIN_COUNT = 1;
        internal const int SHADOW_CASCADE_MAX_COUNT = 4;

        public static readonly int AdditionalLightsDefaultShadowResolutionTierLow = 256;
        public static readonly int AdditionalLightsDefaultShadowResolutionTierMedium = 512;
        public static readonly int AdditionalLightsDefaultShadowResolutionTierHigh = 1024;

        [NonSerialized]
        internal ApertureRenderPipelineEditorResources _editorResourcesAsset;
        public static ApertureRenderPipelineAsset Create(ScriptableRendererData rendererData = null)
        {
            // Create Aperture RP Asset
            ApertureRenderPipelineAsset instance = CreateInstance<ApertureRenderPipelineAsset>();
            if (rendererData != null)
                instance._rendererDataList[0] = rendererData;
            else
                instance._rendererDataList[0] = CreateInstance<ForwardRendererData>();

            instance._editorResourcesAsset = instance.editorResources;

            return instance;
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812")]
        internal class CreateAperturePipelineAsset : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                //Create asset
                AssetDatabase.CreateAsset(Create(CreateRendererAsset(pathName, RendererType.ForwardRenderer)), pathName);
            }
        }
        [MenuItem("Assets/Create/Rendering/Aperture Render Pipeline/Pipeline Asset (Forward Renderer)", priority = CoreUtils.assetCreateMenuPriority1)]
        static void CreateAperturePipeline()
        {
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<CreateAperturePipelineAsset>(),
                "ApertureRenderPipelineAsset.asset", null, null);
        }

        internal static ScriptableRendererData CreateRendererAsset(string path, RendererType type, bool relativePath = true)
        {
            ScriptableRendererData data = CreateRendererData(type);
            string dataPath;
            if (relativePath)
                dataPath =
                    $"{Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path))}_Renderer{Path.GetExtension(path)}";
            else
                dataPath = path;
            AssetDatabase.CreateAsset(data, dataPath);
            return data;
        }

        static ScriptableRendererData CreateRendererData(RendererType type)
        {
            switch (type)
            {
                case RendererType.ForwardRenderer:
                    return CreateInstance<ForwardRendererData>();
                default:
                    return CreateInstance<ForwardRendererData>();
            }
        }

        ApertureRenderPipelineEditorResources editorResources
        {
            get
            {
                if (_editorResourcesAsset != null && !_editorResourcesAsset.Equals(null))
                    return _editorResourcesAsset;

                return null;
                //not support init by guid now

                //string resourcePath = AssetDatabase.GUIDToAssetPath(editorResourcesGUID);
                //var objs = InternalEditorUtility.LoadSerializedFileAndForget(resourcePath);
                //_EditorResourcesAsset = objs != null && objs.Length > 0 ? objs.First() as ApertureRenderPipelineEditorResources : null;
                //return _EditorResourcesAsset;
            }
        }
        protected override RenderPipeline CreatePipeline()
        {
            if (_rendererDataList == null)
                _rendererDataList = new ScriptableRendererData[1];

            // If no default data we can't create pipeline instance
            if (_rendererDataList[_defaultRendererIndex] == null)
            {
                // If previous version and current version are miss-matched then we are waiting for the upgrader to kick in
                if (_assetPreviousVersion != _assetVersion)
                    return null;

                Debug.LogError(
                    $"Default Renderer is missing, make sure there is a Renderer assigned as the default on the current Aperture RP asset:{ApertureRenderPipeline.asset.name}",
                    this);
                return null;
            }

            CreateRenderers();
            return new ApertureRenderPipeline();
        }

        void CreateRenderers()
        {
            DestroyRenderers();

            if (_renderers == null || _renderers.Length != _rendererDataList.Length)
                _renderers = new ScriptableRenderer[_rendererDataList.Length];

            for (int i = 0; i < _rendererDataList.Length; ++i)
            {
                if (_rendererDataList[i] != null)
                    _renderers[i] = _rendererDataList[i].InternalCreateRenderer();
            }
        }

        void DestroyRenderers()
        {
            if (_renderers == null)
                return;

            for (int i = 0; i < _renderers.Length; i++)
                DestroyRenderer(ref _renderers[i]);
        }

        void DestroyRenderer(ref ScriptableRenderer renderer)
        {
            if (renderer != null)
            {
                renderer.Dispose();
                renderer = null;
            }
        }

        public ScriptableRenderer GetRenderer(int index)
        {
            if (index == -1)
                index = _defaultRendererIndex;

            if (index >= _rendererDataList.Length || index < 0 || _rendererDataList[index] == null)
            {
                Debug.LogWarning(
                    $"Renderer at index {index.ToString()} is missing, falling back to Default Renderer {_rendererDataList[_defaultRendererIndex].name}",
                    this);
                index = _defaultRendererIndex;
            }

            // RendererData list differs from RendererList. Create RendererList.
            if (_renderers == null || _renderers.Length < _rendererDataList.Length)
                CreateRenderers();

            // This renderer data is outdated or invalid, we recreate the renderer
            // so we construct all render passes with the updated data
            if (_rendererDataList[index].isInvalidated || _renderers[index] == null)
            {
                DestroyRenderer(ref _renderers[index]);
                _renderers[index] = _rendererDataList[index].InternalCreateRenderer();
            }

            return _renderers[index];
        }

        public ScriptableRenderer ScriptableRenderer
        {
            get
            {
                if (_rendererDataList?.Length > _defaultRendererIndex && _rendererDataList[_defaultRendererIndex] == null)
                {
                    Debug.LogError("Default renderer is missing from the current Pipeline Asset.", this);
                    return null;
                }

                if (ScriptableRendererData.isInvalidated || _renderers[_defaultRendererIndex] == null)
                {
                    DestroyRenderer(ref _renderers[_defaultRendererIndex]);
                    _renderers[_defaultRendererIndex] = ScriptableRendererData.InternalCreateRenderer();
                }

                return _renderers[_defaultRendererIndex];
            }
        }

        internal ScriptableRendererData ScriptableRendererData
        {
            get
            {
                if (_rendererDataList[_defaultRendererIndex] == null)
                    CreatePipeline();

                return _rendererDataList[_defaultRendererIndex];
            }
        }
        public bool SupportsCameraDepthTexture
        {
            get { return _requireDepthTexture; }
            set { _requireDepthTexture = value; }
        }

        public bool SupportsCameraOpaqueTexture
        {
            get { return _requireOpaqueTexture; }
            set { _requireOpaqueTexture = value; }
        }

        public bool SupportsHDR
        {
            get { return _supportsHDR; }
            set { _supportsHDR = value; }
        }

        public bool SupportsMainLightShadows
        {
            get { return _mainLightShadowsSupported; }
        }

        public LightRenderingMode MainLightRenderingMode
        {
            get { return _mainLightRenderingMode; }
        }

        public bool SupportsAdditionalLightShadows
        {
            get { return _additionalLightShadowsSupported; }
        }

        public LightRenderingMode AdditionalLightsRenderingMode
        {
            get { return _additionalLightsRenderingMode; }
        }

        /// <summary>
        /// Controls the maximum distance at which shadows are visible.
        /// </summary>
        public float ShadowDistance
        {
            get { return _shadowDistance; }
            set { _shadowDistance = Mathf.Max(0.0f, value); }
        }

        /// <summary>
        /// Returns true Soft Shadows are supported, false otherwise.
        /// </summary>
        public bool SupportsSoftShadows
        {
            get { return _softShadowsSupported; }
        }


        public bool SupportsDynamicBatching
        {
            get { return _supportsDynamicBatching; }
            set { _supportsDynamicBatching = value; }
        }

        public int MainLightShadowmapResolution
        {
            get { return (int)_mainLightShadowmapResolution; }
        }

        public int MaxAdditionalLightsCount
        {
            get { return _additionalLightsPerObjectLimit; }
            set { _additionalLightsPerObjectLimit = ValidatePerObjectLights(value); }
        }

        public bool SupportsMixedLighting
        {
            get { return _mixedLightingSupported; }
        }

        public int AdditionalLightsShadowmapResolution
        {
            get { return (int)_additionalLightsShadowmapResolution; }
        }

        /// <summary>
        /// Returns the number of shadow cascades.
        /// </summary>
        public int ShadowCascadeCount
        {
            get { return _shadowCascadeCount; }
            set
            {
                if (value < SHADOW_CASCADE_MIN_COUNT || value > SHADOW_CASCADE_MAX_COUNT)
                {
                    throw new ArgumentException($"Value ({value}) needs to be between {SHADOW_CASCADE_MIN_COUNT} and {SHADOW_CASCADE_MAX_COUNT}.");
                }
                _shadowCascadeCount = value;
            }
        }

        /// <summary>
        /// Returns the split value.
        /// </summary>
        /// <returns>Returns a Float with the split value.</returns>
        public float Cascade2Split
        {
            get { return _cascade2Split; }
        }

        /// <summary>
        /// Returns the split values.
        /// </summary>
        /// <returns>Returns a Vector2 with the split values.</returns>
        public Vector2 Cascade3Split
        {
            get { return _cascade3Split; }
        }

        /// <summary>
        /// Returns the split values.
        /// </summary>
        /// <returns>Returns a Vector3 with the split values.</returns>
        public Vector3 Cascade4Split
        {
            get { return _cascade4Split; }
        }

        public float ShadowDepthBias
        {
            get { return _shadowDepthBias; }
            set { _shadowDepthBias = ValidateShadowBias(value); }
        }

        /// <summary>
        /// Controls the distance at which the shadow casting surfaces are shrunk along the surface normal.
        /// </summary>
        public float ShadowNormalBias
        {
            get { return _shadowNormalBias; }
            set { _shadowNormalBias = ValidateShadowBias(value); }
        }

        public int MsaaSampleCount
        {
            get { return (int)_msaa; }
            set { _msaa = (MsaaQuality)value; }
        }

        public bool UseSRPBatcher
        {
            get { return _useSRPBatcher; }
            set { _useSRPBatcher = value; }
        }

        public float RenderScale
        {
            get { return _renderScale; }
            set { _renderScale = ValidateRenderScale(value); }
        }
        public ColorGradingMode ColorGradingMode
        {
            get { return _colorGradingMode; }
            set { _colorGradingMode = value; }
        }

        public int ColorGradingLutSize
        {
            get { return _colorGradingLutSize; }
            set { _colorGradingLutSize = Mathf.Clamp(value, MIN_LUT_SIZE, MAX_LUT_SIZE); }
        }

        /// <summary>
        /// Returns true if fast approximation functions are used when converting between the sRGB and Linear color spaces, false otherwise.
        /// </summary>
        public bool UseFastSRGBLinearConversion
        {
            get { return _useFastSRGBLinearConversion; }
        }


        /// <summary>
        /// Returns the additional light shadow resolution defined for tier "Low" in the UniversalRenderPipeline asset.
        /// </summary>
        public int AdditionalLightsShadowResolutionTierLow
        {
            get { return (int)_additionalLightsShadowResolutionTierLow; }
        }

        /// <summary>
        /// Returns the additional light shadow resolution defined for tier "Medium" in the UniversalRenderPipeline asset.
        /// </summary>
        public int AdditionalLightsShadowResolutionTierMedium
        {
            get { return (int)_additionalLightsShadowResolutionTierMedium; }
        }

        /// <summary>
        /// Returns the additional light shadow resolution defined for tier "High" in the UniversalRenderPipeline asset.
        /// </summary>
        public int AdditionalLightsShadowResolutionTierHigh
        {
            get { return (int)_additionalLightsShadowResolutionTierHigh; }
        }

        internal int GetAdditionalLightsShadowResolution(int additionalLightsShadowResolutionTier)
        {
            if (additionalLightsShadowResolutionTier <= ApertureAdditionalLightData.AdditionalLightsShadowResolutionTierLow /* 0 */)
                return AdditionalLightsShadowResolutionTierLow;

            if (additionalLightsShadowResolutionTier == ApertureAdditionalLightData.AdditionalLightsShadowResolutionTierMedium /* 1 */)
                return AdditionalLightsShadowResolutionTierMedium;

            if (additionalLightsShadowResolutionTier >= ApertureAdditionalLightData.AdditionalLightsShadowResolutionTierHigh /* 2 */)
                return AdditionalLightsShadowResolutionTierHigh;

            return AdditionalLightsShadowResolutionTierMedium;
        }

        /// <summary>
        /// Check to see if the RendererData list contains valid RendererData references.
        /// </summary>
        /// <param name="partial">This bool controls whether to test against all or any, if false then there has to be no invalid RendererData</param>
        /// <returns></returns>
        internal bool ValidateRendererDataList(bool partial = false)
        {
            var emptyEntries = 0;
            for (int i = 0; i < _rendererDataList.Length; i++) emptyEntries += ValidateRendererData(i) ? 0 : 1;
            if (partial)
                return emptyEntries == 0;
            return emptyEntries != _rendererDataList.Length;
        }

        internal bool ValidateRendererData(int index)
        {
            // Check to see if you are asking for the default renderer
            if (index == -1) index = _defaultRendererIndex;
            return index < _rendererDataList.Length ? _rendererDataList[index] != null : false;
        }
        float ValidateRenderScale(float value)
        {
            return Mathf.Max(ApertureRenderPipeline.MinRenderScale, Mathf.Min(value, ApertureRenderPipeline.MaxRenderScale));
        }

        float ValidateShadowBias(float value)
        {
            return Mathf.Max(0.0f, Mathf.Min(value, ApertureRenderPipeline.MaxShadowBias));
        }

        int ValidatePerObjectLights(int value)
        {
            return System.Math.Max(0, System.Math.Min(value, ApertureRenderPipeline.MaxPerObjectLights));
        }

    }
}
