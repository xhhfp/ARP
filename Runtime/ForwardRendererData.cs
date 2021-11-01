
using System;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;
using UnityEngine.Rendering;

namespace Portal.Rendering.Aperture
{
    [Serializable, ReloadGroup, ExcludeFromPreset]
    public class ForwardRendererData : ScriptableRendererData
    {
#if UNITY_EDITOR
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812")]
        internal class CreateForwardRendererAsset : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                ForwardRendererData instance = CreateInstance<ForwardRendererData>();
                instance.PostProcessData = PostProcessData.GetDefaultPostProcessData();
                AssetDatabase.CreateAsset(instance, pathName);
                //ResourceReloader.ReloadAllNullIn(instance, ApertureRenderPipelineAsset.packagePath);
                Selection.activeObject = instance;
            }
        }

        [MenuItem("Assets/Create/Rendering/Aperture Render Pipeline/Forward Renderer", priority = CoreUtils.assetCreateMenuPriority2)]
        static void CreateForwardRendererData()
        {
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<CreateForwardRendererAsset>(), "CustomForwardRendererData.asset", null, null);
        }

#endif

        [Serializable, ReloadGroup]
        public sealed class ShaderResources
        {

        }

        public PostProcessData PostProcessData = null;

        public ShaderResources shaders = null;

        [SerializeField] private LayerMask _opaqueLayerMask = -1;
        [SerializeField] private LayerMask _transparentLayerMask = -1;
        [SerializeField] private StencilStateData _defaultStencilState = new StencilStateData() { passOperation = StencilOp.Replace }; // This default state is compatible with deferred renderer.
        [SerializeField] private RenderingMode _renderingMode = RenderingMode.Forward;
        [SerializeField] private bool _accurateGbufferNormals = false;

        protected override ScriptableRenderer Create()
        {
            if (!Application.isPlaying)
            {
                ReloadAllNullProperties();
            }
            return new ForwardRenderer(this);
        }

        /// <summary>
        /// Use this to configure how to filter opaque objects.
        /// </summary>
        public LayerMask opaqueLayerMask
        {
            get => _opaqueLayerMask;
            set
            {
                SetDirty();
                _opaqueLayerMask = value;
            }
        }

        /// <summary>
        /// Use this to configure how to filter transparent objects.
        /// </summary>
        public LayerMask transparentLayerMask
        {
            get => _transparentLayerMask;
            set
            {
                SetDirty();
                _transparentLayerMask = value;
            }
        }

        /// <summary>
        /// Rendering mode.
        /// </summary>
        public RenderingMode RenderingMode
        {
            get => _renderingMode;
            set
            {
                SetDirty();
                _renderingMode = value;
            }
        }

        /// <summary>
        /// Use Octaedron Octahedron normal vector encoding for gbuffer normals.
        /// The overhead is negligible from desktop GPUs, while it should be avoided for mobile GPUs.
        /// </summary>
        public bool accurateGbufferNormals
        {
            get => _accurateGbufferNormals;
            set
            {
                SetDirty();
                _accurateGbufferNormals = value;
            }
        }

        public StencilStateData defaultStencilState
        {
            get => _defaultStencilState;
            set
            {
                SetDirty();
                _defaultStencilState = value;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (shaders == null)
                return;

            ReloadAllNullProperties();
        }

        private void ReloadAllNullProperties()
        {
//#if UNITY_EDITOR
//            ResourceReloader.TryReloadAllNullIn(this, ApertureRenderPipelineAsset.packagePath);
//#endif
        }
    }

}
