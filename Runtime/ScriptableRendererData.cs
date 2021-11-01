using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Portal.Rendering.Aperture
{
    /// <summary>
    /// Class <c>ScriptableRendererData</c> contains resources for a <c>ScriptableRenderer</c>.
    /// <seealso cref="ScriptableRenderer"/>
    /// </summary>
    public abstract class ScriptableRendererData : ScriptableObject
    {
        internal bool isInvalidated { get; set; }

        protected abstract ScriptableRenderer Create();

        [SerializeField] internal List<ScriptableRendererFeature> _rendererFeatures = new List<ScriptableRendererFeature>(10);
        [SerializeField] internal List<long> _rendererFeatureMap = new List<long>(10);

        public List<ScriptableRendererFeature> RendererFeatures
        {
            get => _rendererFeatures;
        }

        public new void SetDirty()
        {
            isInvalidated = true;
        }

        internal ScriptableRenderer InternalCreateRenderer()
        {
            isInvalidated = false;
            return Create();
        }

        protected virtual void OnValidate()
        {
            SetDirty();
#if UNITY_EDITOR
            if (_rendererFeatures.Contains(null))
                ValidateRendererFeatures();
#endif
        }

        protected virtual void OnEnable()
        {
            SetDirty();
        }

#if UNITY_EDITOR
        internal virtual Material GetDefaultMaterial(DefaultMaterialType materialType)
        {
            return null;
        }

        internal virtual Shader GetDefaultShader()
        {
            return null;
        }

        internal bool ValidateRendererFeatures()
        {
            // Get all Subassets
            var subassets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(this));
            List<long> linkedIds = new List<long>();
            Dictionary<long, object> loadedAssets = new Dictionary<long, object>();
            bool mapValid = _rendererFeatureMap != null && _rendererFeatureMap?.Count == _rendererFeatures?.Count;
            string debugOutput = $"{name}\nValid Sub-assets:\n";

            // Collect valid, compiled sub-assets
            foreach (var asset in subassets)
            {
                if (asset == null || asset.GetType().BaseType != typeof(ScriptableRendererFeature)) continue;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long localId);
                loadedAssets.Add(localId, asset);
                debugOutput += $"-{asset.name}\n--localId={localId}\n";
            }

            // Collect assets that are connected to the list
            for (var i = 0; i < _rendererFeatures?.Count; i++)
            {
                if (!_rendererFeatures[i]) continue;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_rendererFeatures[i], out var guid, out long localId))
                {
                    linkedIds.Add(localId);
                }
            }

            var mapDebug = mapValid ? "Linking" : "Map missing, will attempt to re-map";
            debugOutput += $"Feature List Status({mapDebug}):\n";

            // Try fix missing references
            for (var i = 0; i < _rendererFeatures?.Count; i++)
            {
                if (_rendererFeatures[i] == null)
                {
                    if (mapValid && _rendererFeatureMap[i] != 0)
                    {
                        var localId = _rendererFeatureMap[i];
                        loadedAssets.TryGetValue(localId, out var asset);
                        _rendererFeatures[i] = (ScriptableRendererFeature)asset;
                    }
                    else
                    {
                        _rendererFeatures[i] = (ScriptableRendererFeature)GetUnusedAsset(ref linkedIds, ref loadedAssets);
                    }
                }

                debugOutput += _rendererFeatures[i] != null ? $"-{i}:Linked\n" : $"-{i}:Missing\n";
            }

            UpdateMap();

            if (!_rendererFeatures.Contains(null))
                return true;

            Debug.LogError($"{name} is missing RendererFeatures\nThis could be due to missing scripts or compile error.", this);
            return false;
        }

        private static object GetUnusedAsset(ref List<long> usedIds, ref Dictionary<long, object> assets)
        {
            foreach (var asset in assets)
            {
                var alreadyLinked = usedIds.Any(used => asset.Key == used);

                if (alreadyLinked)
                    continue;

                usedIds.Add(asset.Key);
                return asset.Value;
            }

            return null;
        }

        private void UpdateMap()
        {
            if (_rendererFeatureMap.Count != _rendererFeatures.Count)
            {
                _rendererFeatureMap.Clear();
                _rendererFeatureMap.AddRange(new long[_rendererFeatures.Count]);
            }

            for (int i = 0; i < RendererFeatures.Count; i++)
            {
                if (_rendererFeatures[i] == null) continue;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_rendererFeatures[i], out var guid, out long localId)) continue;
                _rendererFeatureMap[i] = localId;
            }
        }

#endif
    }

}

