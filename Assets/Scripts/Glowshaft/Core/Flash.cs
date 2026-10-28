using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Glowshaft
{
    /// <summary>短暫的閃光（子彈命中、史萊姆爆開、撿到補血），亮度線性淡出後自毀。</summary>
    public sealed class Flash : MonoBehaviour
    {
        private Light2D _light;
        private float _intensity;
        private float _duration;
        private float _time;

        public static void Spawn(Transform parent, Vector2 position, Color color, float radius, float intensity, float duration)
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var flash = go.AddComponent<Flash>();
            flash._light = GlowLights.CreatePointLight(go.transform, color, radius, intensity);
            flash._light.transform.localPosition = new Vector3(0f, GameConfig.MuzzleHeight, 0f);
            flash._intensity = intensity;
            flash._duration = Mathf.Max(0.01f, duration);
        }

        private void Update()
        {
            _time += Time.deltaTime;
            if (_time >= _duration)
            {
                Destroy(gameObject);
                return;
            }
            _light.intensity = _intensity * (1f - _time / _duration);
        }
    }
}
