using System;
using UnityEngine;
namespace Portal.Rendering.Aperture
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public class ApertureAdditionalLightData : MonoBehaviour
    {
        [Tooltip("Controls if light Shadow Bias parameters use pipeline settings.")]
        [SerializeField] bool _usePipelineSettings = true;

        public bool UsePipelineSettings
        {
            get { return _usePipelineSettings; }
            set { _usePipelineSettings = value; }
        }

        public static readonly int AdditionalLightsShadowResolutionTierCustom = -1;
        public static readonly int AdditionalLightsShadowResolutionTierLow = 0;
        public static readonly int AdditionalLightsShadowResolutionTierMedium = 1;
        public static readonly int AdditionalLightsShadowResolutionTierHigh = 2;
        public static readonly int AdditionalLightsShadowDefaultResolutionTier = AdditionalLightsShadowResolutionTierHigh;
        public static readonly int AdditionalLightsShadowDefaultCustomResolution = 128;
        public static readonly int AdditionalLightsShadowMinimumResolution = 128;

        [Tooltip("Controls if light shadow resolution uses pipeline settings.")]
        [SerializeField]private int _additionalLightsShadowResolutionTier = AdditionalLightsShadowDefaultResolutionTier;

        public int AdditionalLightsShadowResolutionTier
        {
            get { return _additionalLightsShadowResolutionTier; }
        }
    }
}
