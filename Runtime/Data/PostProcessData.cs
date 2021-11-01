#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
#endif
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Portal.Rendering.Aperture
{
    public class PostProcessData : ScriptableObject
    {
#if UNITY_EDITOR
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812")]
        internal class CreatePostProcessDataAsset : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                var instance = CreateInstance<PostProcessData>();
                AssetDatabase.CreateAsset(instance, pathName);
                //ResourceReloader.ReloadAllNullIn(instance, ApertureRenderPipelineAsset.packagePath);
                Selection.activeObject = instance;
            }
        }

        [MenuItem("Assets/Create/Rendering/Aperture Render Pipeline/Post-process Data", priority = CoreUtils.assetCreateMenuPriority3)]
        static void CreatePostProcessData()
        {
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<CreatePostProcessDataAsset>(), "CustomPostProcessData.asset", null, null);
        }

        internal static PostProcessData GetDefaultPostProcessData()
        {
            //var path = System.IO.Path.Combine(ApertureRenderPipelineAsset.packagePath, "Runtime/Data/PostProcessData.asset");
            //return AssetDatabase.LoadAssetAtPath<PostProcessData>(path);
            return null;
        }

#endif

        [Serializable, ReloadGroup]
        public sealed class ShaderResources
        {

        }

        [Serializable, ReloadGroup]
        public sealed class TextureResources
        {
        }

        public ShaderResources Shaders;
        public TextureResources Textures;
    }
}