using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
//using Portal.Rendering.Aperture.Internal;
namespace Portal.Rendering.Aperture
{
    public enum RenderingMode
    {
        Forward,
        //Deferred
    };
    public sealed class ForwardRenderer : ScriptableRenderer
    {
        private DrawObjectsPass _renderOpaqueForwardPass;
        private DrawSkyboxPass _drawSkyboxPass;
        private DrawObjectsPass _renderTransparentForwardPass;

        private RenderTargetHandle _activeCameraColorAttachment;
        private RenderTargetHandle _activeCameraDepthAttachment;
        private RenderTargetHandle _cameraColorAttachment;
        private RenderTargetHandle _cameraDepthAttachment;

        StencilState _defaultStencilState;
        public ForwardRenderer(ForwardRendererData data) : base(data)
        {
            StencilStateData stencilData = data.defaultStencilState;
            _defaultStencilState = StencilState.defaultValue;
            _defaultStencilState.enabled = stencilData.overrideStencilState;
            _defaultStencilState.SetCompareFunction(stencilData.stencilCompareFunction);
            _defaultStencilState.SetPassOperation(stencilData.passOperation);
            _defaultStencilState.SetFailOperation(stencilData.failOperation);
            _defaultStencilState.SetZFailOperation(stencilData.zFailOperation);

            // Always create this pass even in deferred because we use it for wireframe rendering in the Editor or offscreen depth texture rendering.
            _renderOpaqueForwardPass = new DrawObjectsPass(ARPProfileId.DrawOpaqueObjects, true, RenderPassEvent.BeforeRenderingOpaques, RenderQueueRange.opaque, data.opaqueLayerMask, _defaultStencilState, stencilData.stencilReference);
            _drawSkyboxPass = new DrawSkyboxPass(RenderPassEvent.BeforeRenderingSkybox);
#if ADAPTIVE_PERFORMANCE_2_1_0_OR_NEWER
            if (!ApertureRenderPipeline.asset.useAdaptivePerformance || AdaptivePerformance.AdaptivePerformanceRenderSettings.SkipTransparentObjects == false)
#endif
            {
                _renderTransparentForwardPass = new DrawObjectsPass(ARPProfileId.DrawTransparentObjects, false, RenderPassEvent.BeforeRenderingTransparents, RenderQueueRange.transparent, data.transparentLayerMask, _defaultStencilState, stencilData.stencilReference);
            }
            _cameraColorAttachment.Init("_CameraColorTexture");
            _cameraDepthAttachment.Init("_CameraDepthAttachment");

        }
        /// <inheritdoc />
        public override void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.CameraData.Camera;
            ref CameraData cameraData = ref renderingData.CameraData;
            RenderTextureDescriptor cameraTargetDescriptor = renderingData.CameraData.CameraTargetDescriptor;

            // Special path for depth only offscreen cameras. Only write opaques + transparents.
            bool isOffscreenDepthTexture = cameraData.TargetTexture != null && cameraData.TargetTexture.format == RenderTextureFormat.Depth;
            if (isOffscreenDepthTexture)
            {
                ConfigureCameraTarget(BuiltinRenderTextureType.CameraTarget, BuiltinRenderTextureType.CameraTarget);
                AddRenderPasses(ref renderingData);
                EnqueuePass(_renderOpaqueForwardPass);
                EnqueuePass(_drawSkyboxPass);
                EnqueuePass(_renderTransparentForwardPass);
                return;
            }

            // Configure all settings require to start a new camera stack (base camera only)
            if (cameraData.RenderType == CameraRenderType.Base)
            {
                RenderTargetHandle cameraTargetHandle = RenderTargetHandle.GetCameraTarget();

                _activeCameraColorAttachment = cameraTargetHandle;
                _activeCameraDepthAttachment = cameraTargetHandle;

            }
            else
            {
                _activeCameraColorAttachment = _cameraColorAttachment;
                _activeCameraDepthAttachment = _cameraDepthAttachment;
            }

            // Assign camera targets (color and depth)

            RenderTargetIdentifier activeColorRenderTargetId = _activeCameraColorAttachment.Identifier();
            RenderTargetIdentifier activeDepthRenderTargetId = _activeCameraDepthAttachment.Identifier();

            ConfigureCameraTarget(activeColorRenderTargetId, activeDepthRenderTargetId);


            EnqueuePass(_renderOpaqueForwardPass);

            Skybox cameraSkybox;
            cameraData.Camera.TryGetComponent<Skybox>(out cameraSkybox);
            bool isOverlayCamera = cameraData.RenderType == CameraRenderType.Overlay;
            if (camera.clearFlags == CameraClearFlags.Skybox && (RenderSettings.skybox != null || cameraSkybox?.material != null) && !isOverlayCamera)
                EnqueuePass(_drawSkyboxPass);
            EnqueuePass(_renderTransparentForwardPass);
        }

        protected override void Dispose(bool disposing)
        {

        }
    }
}