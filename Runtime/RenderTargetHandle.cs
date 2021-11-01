using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Portal.Rendering.Aperture
{
    public struct RenderTargetHandle
    {
        public int Id { set; get; }
        private RenderTargetIdentifier _rtid { set; get; }

        public static readonly RenderTargetHandle CameraTarget = new RenderTargetHandle { Id = -1 };

        public RenderTargetHandle(RenderTargetIdentifier renderTargetIdentifier)
        {
            Id = -2;
            _rtid = renderTargetIdentifier;
        }

        internal static RenderTargetHandle GetCameraTarget()
        {
            return CameraTarget;
        }

        public void Init(string shaderProperty)
        {
            // Shader.PropertyToID returns what is internally referred to as a "ShaderLab::FastPropertyName".
            // It is a value coming from an internal global std::map<char*,int> that converts shader property strings into unique integer handles (that are faster to work with).
            Id = Shader.PropertyToID(shaderProperty);
        }

        public void Init(RenderTargetIdentifier renderTargetIdentifier)
        {
            Id = -2;
            _rtid = renderTargetIdentifier;
        }

        public RenderTargetIdentifier Identifier()
        {
            if (Id == -1)
            {
                return BuiltinRenderTextureType.CameraTarget;
            }
            if (Id == -2)
            {
                return _rtid;
            }
            return new RenderTargetIdentifier(Id);
        }

        public bool HasInternalRenderTargetId()
        {
            return Id == -2;
        }

        public bool Equals(RenderTargetHandle other)
        {
            if (Id == -2 || other.Id == -2)
                return Identifier() == other.Identifier();
            return Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is RenderTargetHandle && Equals((RenderTargetHandle)obj);
        }

        public override int GetHashCode()
        {
            return Id;
        }

        public static bool operator ==(RenderTargetHandle c1, RenderTargetHandle c2)
        {
            return c1.Equals(c2);
        }

        public static bool operator !=(RenderTargetHandle c1, RenderTargetHandle c2)
        {
            return !c1.Equals(c2);
        }
    }
}