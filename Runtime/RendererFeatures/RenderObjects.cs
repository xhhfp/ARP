using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Portal.Rendering.Aperture
{
    public enum RenderQueueType
    {
        Opaque,
        Transparent,
    }

    public class RenderObjects : ScriptableRendererFeature
    {
        [System.Serializable]
        public class RenderObjectsSettings
        {
            public string passTag = "RenderObjectsFeature";
        }

        public RenderObjectsSettings settings = new RenderObjectsSettings();

        public override void Create()
        {

        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {

        }

    }

}