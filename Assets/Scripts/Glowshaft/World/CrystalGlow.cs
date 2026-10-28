using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Glowshaft
{
    /// <summary>水晶的光緩慢呼吸（強度 ±15%）。由 WorldBuilder 以 AddComponent 建立，不序列化進任何場景。</summary>
    public sealed class CrystalGlow : MonoBehaviour
    {
        private const float Amplitude = 0.15f;
        private const float Speed = 1.6f;

        private Light2D _light;
        private float _baseIntensity;
        private float _phase;

        public void Init(Light2D light, float phase)
        {
            _light = light;
            _baseIntensity = light.intensity;
            _phase = phase * Mathf.PI * 2f;
        }

        private void Update()
        {
            if (_light == null)
                return;
            _light.intensity = _baseIntensity * (1f + Amplitude * Mathf.Sin(Time.time * Speed + _phase));
        }
    }
}
