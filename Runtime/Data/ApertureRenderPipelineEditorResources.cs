using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Portal.Rendering.Aperture
{
    public class ApertureRenderPipelineEditorResources : ScriptableObject
    {
        [Serializable, ReloadGroup]
        public sealed class ShaderResources
        {
        }

        [Serializable, ReloadGroup]
        public sealed class MaterialResources
        {
        }

        public ShaderResources Shaders;
        public MaterialResources Materials;
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(ApertureRenderPipelineEditorResources), true)]
    class ApertureRenderPipelineEditorResourcesEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // Add a "Reload All" button in inspector when we are in developer's mode
            if (UnityEditor.EditorPrefs.GetBool("DeveloperMode") && GUILayout.Button("Reload All"))
            {
                var resources = target as ApertureRenderPipelineEditorResources;
                resources.Materials = null;
                resources.Shaders = null;
                //ResourceReloader.ReloadAllNullIn(target, ApertureRenderPipelineAsset.packagePath);
            }
        }
    }
#endif
}
