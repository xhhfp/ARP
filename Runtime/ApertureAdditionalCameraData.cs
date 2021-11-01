using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace Portal.Rendering.Aperture
{
    /// <summary>
    /// Holds information about whether to override certain camera rendering options from the render pipeline asset.
    /// When set to <c>Off</c> option will be disabled regardless of what is set on the pipeline asset.
    /// When set to <c>On</c> option will be enabled regardless of what is set on the pipeline asset.
    /// When set to <c>UsePipelineSetting</c> value set in the <see cref="ApertureRenderPipelineAsset"/>.
    /// </summary>
    public enum CameraOverrideOption
    {
        Off,
        On,
        UsePipelineSettings,
    }

    //[Obsolete("Renderer override is no longer used, renderers are referenced by index on the pipeline asset.")]
    public enum RendererOverrideOption
    {
        Custom,
        UsePipelineSettings,
    }

    /// <summary>
    /// Holds information about the post-processing anti-aliasing mode.
    /// When set to <c>None</c> no post-processing anti-aliasing pass will be performed.
    /// When set to <c>Fast</c> a fast approximated anti-aliasing pass will render when resolving the camera to screen.
    /// When set to <c>SubpixelMorphologicalAntiAliasing</c> SMAA pass will render when resolving the camera to screen. You can choose the SMAA quality by setting <seealso cref="AntialiasingQuality"/>
    /// </summary>
    public enum AntialiasingMode
    {
        None,
        FastApproximateAntialiasing,
        SubpixelMorphologicalAntiAliasing,
    }

    /// <summary>
    /// Holds information about the render type of a camera. Options are Base or Overlay.
    /// Base rendering type allows the camera to render to either the screen or to a texture.
    /// Overlay rendering type allows the camera to render on top of a previous camera output, thus compositing camera results.
    /// </summary>
    public enum CameraRenderType
    {
        Base,
        Overlay,
    }

    /// <summary>
    /// Controls SMAA anti-aliasing quality.
    /// </summary>
    public enum AntialiasingQuality
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Contains extension methods for Camera class.
    /// </summary>
    public static class CameraExtensions
    {
        /// <summary>
        /// Aperture Render Pipeline exposes additional rendering data in a separate component.
        /// This method returns the additional data component for the given camera or create one if it doesn't exists yet.
        /// </summary>
        /// <param name="camera"></param>
        /// <returns>The <c>ApertureAdditinalCameraData</c> for this camera.</returns>
        /// <see cref="ApertureAdditionalCameraData"/>
        public static ApertureAdditionalCameraData GetApertureAdditionalCameraData(this Camera camera)
        {
            GameObject gameObject = camera.gameObject;
            bool componentExists = gameObject.TryGetComponent<ApertureAdditionalCameraData>(out var cameraData);
            if (!componentExists)
                cameraData = gameObject.AddComponent<ApertureAdditionalCameraData>();

            return cameraData;
        }
    }

    static class CameraTypeUtility
    {
        static string[] s_cameraTypeNames = Enum.GetNames(typeof(CameraRenderType)).ToArray();

        public static string GetName(this CameraRenderType type)
        {
            int typeInt = (int)type;
            if (typeInt < 0 || typeInt >= s_cameraTypeNames.Length)
                typeInt = (int)CameraRenderType.Base;
            return s_cameraTypeNames[typeInt];
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [ImageEffectAllowedInSceneView]
    public class ApertureAdditionalCameraData : MonoBehaviour, ISerializationCallbackReceiver
    {
        [FormerlySerializedAs("renderShadows"), SerializeField]
        private bool _renderShadows = true;

        [SerializeField]
        private CameraOverrideOption _requiresDepthTextureOption = CameraOverrideOption.UsePipelineSettings;

        [SerializeField]
        private CameraOverrideOption _requiresOpaqueTextureOption = CameraOverrideOption.UsePipelineSettings;

        [SerializeField]
        private CameraRenderType _cameraType = CameraRenderType.Base;
        [SerializeField]
        private List<Camera> _cameras = new List<Camera>();
        [SerializeField]
        private int _rendererIndex = -1;

        [SerializeField]
        private LayerMask _volumeLayerMask = 1; // "Default"
        [SerializeField]
        private Transform _volumeTrigger = null;

        [SerializeField]
        private bool _renderPostProcessing = false;
        [SerializeField]
        private AntialiasingMode _antialiasing = AntialiasingMode.None;
        [SerializeField]
        private AntialiasingQuality _antialiasingQuality = AntialiasingQuality.High;
        [SerializeField]
        private bool _stopNaN = false;
        [SerializeField]
        private bool _dithering = false;
        [SerializeField]
        private bool _clearDepth = true;

        [NonSerialized] Camera _camera;
        // Deprecated:
        [FormerlySerializedAs("requiresDepthTexture"), SerializeField]
        bool _requiresDepthTexture = false;

        [FormerlySerializedAs("requiresColorTexture"), SerializeField]
        bool _requiresColorTexture = false;

        [HideInInspector] [SerializeField] float _version = 2;

        public float Version => _version;

        //static ApertureAdditionalCameraData s_defaultAdditionalCameraData = null;
        internal static ApertureAdditionalCameraData s_defaultAdditionalCameraData
        {
            get
            {
                if (s_defaultAdditionalCameraData == null)
                    s_defaultAdditionalCameraData = new ApertureAdditionalCameraData();

                return s_defaultAdditionalCameraData;
            }
            private set { }
        }

#if UNITY_EDITOR
        internal new Camera camera
#else
        internal Camera camera
#endif
        {
            get
            {
                if (!_camera)
                {
                    gameObject.TryGetComponent<Camera>(out _camera);
                }
                return _camera;
            }
        }


        /// <summary>
        /// Controls if this camera should render shadows.
        /// </summary>
        public bool RenderShadows
        {
            get => _renderShadows;
            set => _renderShadows = value;
        }

        /// <summary>
        /// Controls if a camera should render depth.
        /// The depth is available to be bound in shaders as _CameraDepthTexture.
        /// <seealso cref="CameraOverrideOption"/>
        /// </summary>
        public CameraOverrideOption RequiresDepthOption
        {
            get => _requiresDepthTextureOption;
            set => _requiresDepthTextureOption = value;
        }

        /// <summary>
        /// Controls if a camera should copy the color contents of a camera after rendering opaques.
        /// The color texture is available to be bound in shaders as _CameraOpaqueTexture.
        /// </summary>
        public CameraOverrideOption RequiresColorOption
        {
            get => _requiresOpaqueTextureOption;
            set => _requiresOpaqueTextureOption = value;
        }

        /// <summary>
        /// Returns the camera renderType.
        /// <see cref="CameraRenderType"/>.
        /// </summary>
        public CameraRenderType RenderType
        {
            get => _cameraType;
            set => _cameraType = value;
        }

        /// <summary>
        /// Returns the camera stack. Only valid for Base cameras.
        /// Overlay cameras have no stack and will return null.
        /// <seealso cref="CameraRenderType"/>.
        /// </summary>
        public List<Camera> CameraStack
        {
            get
            {
                if (RenderType != CameraRenderType.Base)
                {
                    Camera camera = gameObject.GetComponent<Camera>();
                    Debug.LogWarning(string.Format("{0}: This camera is of {1} type. Only Base cameras can have a camera stack.", camera.name, RenderType));
                    return null;
                }

                if (ScriptableRenderer.SupportedRenderingFeatures.CameraStacking == false)
                {
                    Camera camera = gameObject.GetComponent<Camera>();
                    Debug.LogWarning(string.Format("{0}: This camera has a ScriptableRenderer that doesn't support camera stacking. Camera stack is null.", camera.name));
                    return null;
                }
                return _cameras;
            }
        }

        internal void UpdateCameraStack()
        {
#if UNITY_EDITOR
            Undo.RecordObject(this, "Update camera stack");
#endif
            int prev = _cameras.Count;
            _cameras.RemoveAll(cam => cam == null);
            int curr = _cameras.Count;
            int removedCamsCount = prev - curr;
            if (removedCamsCount != 0)
            {
                Debug.LogWarning(name + ": " + removedCamsCount + " camera overlay" + (removedCamsCount > 1 ? "s" : "") + " no longer exists and will be removed from the camera stack.");
            }
        }

        /// <summary>
        /// If true, this camera will clear depth value before rendering. Only valid for Overlay cameras.
        /// </summary>
        public bool ClearDepth
        {
            get => _clearDepth;
        }

        /// <summary>
        /// Returns true if this camera needs to render depth information in a texture.
        /// If enabled, depth texture is available to be bound and read from shaders as _CameraDepthTexture after rendering skybox.
        /// </summary>
        public bool RequiresDepthTexture
        {
            get
            {
                if (_requiresDepthTextureOption == CameraOverrideOption.UsePipelineSettings)
                {
                    return ApertureRenderPipeline.asset.SupportsCameraDepthTexture;
                }
                else
                {
                    return _requiresDepthTextureOption == CameraOverrideOption.On;
                }
            }
            set { _requiresDepthTextureOption = (value) ? CameraOverrideOption.On : CameraOverrideOption.Off; }
        }

        /// <summary>
        /// Returns true if this camera requires to color information in a texture.
        /// If enabled, color texture is available to be bound and read from shaders as _CameraOpaqueTexture after rendering skybox.
        /// </summary>
        public bool RequiresColorTexture
        {
            get
            {
                if (_requiresOpaqueTextureOption == CameraOverrideOption.UsePipelineSettings)
                {
                    return ApertureRenderPipeline.asset.SupportsCameraOpaqueTexture;
                }
                else
                {
                    return _requiresOpaqueTextureOption == CameraOverrideOption.On;
                }
            }
            set { _requiresOpaqueTextureOption = (value) ? CameraOverrideOption.On : CameraOverrideOption.Off; }
        }

        /// <summary>
        /// Returns the <see cref="Aperture.ScriptableRenderer"/> that is used to render this camera.
        /// </summary>
        public ScriptableRenderer ScriptableRenderer
        {
            get
            {
                if (ApertureRenderPipeline.asset is null)
                    return null;
                if (!ApertureRenderPipeline.asset.ValidateRendererData(_rendererIndex))
                {
                    int defaultIndex = ApertureRenderPipeline.asset._defaultRendererIndex;
                    Debug.LogWarning(
                        $"Renderer at <b>index {_rendererIndex.ToString()}</b> is missing for camera <b>" +
                        $"{camera.name}</b>, falling back to Default Renderer. <b>{ApertureRenderPipeline.asset._rendererDataList[defaultIndex].name}</b>",
                        ApertureRenderPipeline.asset);
                    return ApertureRenderPipeline.asset.GetRenderer(defaultIndex);
                }
                return ApertureRenderPipeline.asset.GetRenderer(_rendererIndex);
            }
        }

        /// <summary>
        /// Use this to set this Camera's current <see cref="Aperture.ScriptableRenderer"/> to one listed on the Render Pipeline Asset. Takes an index that maps to the list on the Render Pipeline Asset.
        /// </summary>
        /// <param name="index">The index that maps to the RendererData list on the currently assigned Render Pipeline Asset</param>
        public void SetRenderer(int index)
        {
            _rendererIndex = index;
        }

        public LayerMask VolumeLayerMask
        {
            get => _volumeLayerMask;
            set => _volumeLayerMask = value;
        }

        public Transform VolumeTrigger
        {
            get => _volumeTrigger;
            set => _volumeTrigger = value;
        }

        /// <summary>
        /// Returns true if this camera should render post-processing.
        /// </summary>
        public bool RenderPostProcessing
        {
            get => _renderPostProcessing;
            set => _renderPostProcessing = value;
        }

        /// <summary>
        /// Returns the current anti-aliasing mode used by this camera.
        /// <see cref="AntialiasingMode"/>.
        /// </summary>
        public AntialiasingMode Antialiasing
        {
            get => _antialiasing;
            set => _antialiasing = value;
        }

        /// <summary>
        /// Returns the current anti-aliasing quality used by this camera.
        /// <seealso cref="AntialiasingQuality"/>.
        /// </summary>
        public AntialiasingQuality AntialiasingQuality
        {
            get => _antialiasingQuality;
            set => _antialiasingQuality = value;
        }

        public bool StopNaN
        {
            get => _stopNaN;
            set => _stopNaN = value;
        }

        public bool Dithering
        {
            get => _dithering;
            set => _dithering = value;
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            if (Version <= 1)
            {
                _requiresDepthTextureOption = (_requiresDepthTexture) ? CameraOverrideOption.On : CameraOverrideOption.Off;
                _requiresOpaqueTextureOption = (_requiresColorTexture) ? CameraOverrideOption.On : CameraOverrideOption.Off;
            }
        }

        public void OnDrawGizmos()
        {
            string path = "Asset/Aperture RP/Editor/Gizmos/";
            string gizmoName = "";
            Color tint = Color.white;

            if (_cameraType == CameraRenderType.Base)
            {
                gizmoName = $"{path}Camera_Base.png";
            }
            else if (_cameraType == CameraRenderType.Overlay)
            {
                gizmoName = $"{path}Camera_Overlay.png";
            }


#if UNITY_EDITOR
            if (Selection.activeObject == gameObject)
            {
                // Get the preferences selection color
                tint = SceneView.selectedOutlineColor;
            }
#endif
            if (!string.IsNullOrEmpty(gizmoName))
            {
                Gizmos.DrawIcon(transform.position, gizmoName, true, tint);
            }

            if (RenderPostProcessing)
            {
                Gizmos.DrawIcon(transform.position, $"{path}Camera_PostProcessing.png", true, tint);
            }
        }
    }
}
