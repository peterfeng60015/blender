using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Glowshaft
{
    /// <summary>
    /// 光源工廠（world 負責）。所有 Light2D 都由這裡建立，Core 只決定顏色／大小並呼叫。
    /// 公開簽名不要改（改之前找 gameplay）。
    ///
    /// 共同約定：回傳的 Light2D 掛在新建的子物件上（parent 的子物件，localPosition＝0），
    /// 由 parent 被 Destroy 時一起消失；照亮全部 sorting layer（Light2D.Awake 預設就是全部）。
    ///
    /// 法線打光：<c>Light2D.normalMapQuality</c>／<c>normalMapDistance</c> 在這版 URP 是唯讀屬性，
    /// 這裡用反射設私有序列化欄位 <c>m_NormalMapQuality</c>／<c>m_NormalMapDistance</c>。
    /// 這兩個是序列化欄位，IL2CPP 不會剝掉；URP 升版若改名，會記一次警告並退回不開法線（光照照常）。
    /// </summary>
    public static class GlowLights
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo NormalMapQualityField = typeof(Light2D).GetField("m_NormalMapQuality", PrivateInstance);
        private static readonly FieldInfo NormalMapDistanceField = typeof(Light2D).GetField("m_NormalMapDistance", PrivateInstance);
        private static bool _warnedNormalMap;

        /// <summary>整張地圖唯一的 Global Light 2D（幾乎全黑、偏藍的環境光，只讓輪廓隱約可見）。由 WorldBuilder.Build 呼叫。</summary>
        public static Light2D CreateGlobalLight(Transform parent)
        {
            var light = NewLight(parent, "GlobalLight", Light2D.LightType.Global);
            light.color = new Color(0.45f, 0.52f, 0.8f);
            light.intensity = 0.16f;
            // Global light 是整片平光，不吃法線
            return light;
        }

        /// <summary>
        /// 玩家頭燈（Spot：Point Light 2D 加內外角）。光錐朝向回傳物件的 transform.up；
        /// Core（<c>Player</c>）每幀以 <c>localRotation = Quaternion.Euler(0, 0, 瞄準角 - 90)</c> 繞 Z 轉，讓 up 軸對準瞄準方向
        /// （不直接設 <c>transform.up</c>，避免朝正下方時繞 X 軸翻轉）。
        /// </summary>
        public static Light2D CreateHeadlight(Transform parent)
        {
            var light = NewLight(parent, "Headlight", Light2D.LightType.Point);
            light.color = new Color(1f, 0.93f, 0.75f);
            light.intensity = 1.5f;
            light.pointLightInnerAngle = 28f;
            light.pointLightOuterAngle = 72f;
            light.pointLightInnerRadius = 0.3f;
            light.pointLightOuterRadius = 11f;
            light.falloffIntensity = 0.6f;
            // 光源離地越低，法線的明暗對比越強；頭燈範圍大，用 Accurate
            EnableNormalMap(light, Light2D.NormalMapQuality.Accurate, 2f);
            return light;
        }

        /// <summary>
        /// 一般點光源（子彈、史萊姆、補血道具、爆開的閃光、玩家身邊的微光、水晶都用這個）。
        /// color、outerRadius（世界單位）、intensity 由呼叫端決定；Core 之後可能會直接改 intensity 做閃爍或淡出。
        /// </summary>
        public static Light2D CreatePointLight(Transform parent, Color color, float outerRadius, float intensity)
        {
            var light = NewLight(parent, "PointLight", Light2D.LightType.Point);
            light.color = color;
            light.intensity = intensity;
            outerRadius = Mathf.Max(0.01f, outerRadius);
            light.pointLightInnerRadius = 0f;
            light.pointLightOuterRadius = outerRadius;
            // 小光源用 Fast 就夠；離地高度隨半徑，小光源貼近地面才看得出法線
            EnableNormalMap(light, Light2D.NormalMapQuality.Fast, Mathf.Clamp(outerRadius * 0.5f, 0.5f, 2.5f));
            return light;
        }

        private static Light2D NewLight(Transform parent, string name, Light2D.LightType type)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light2D>();
            light.lightType = type;
            return light;
        }

        private static void EnableNormalMap(Light2D light, Light2D.NormalMapQuality quality, float distance)
        {
            if (NormalMapQualityField == null || NormalMapDistanceField == null)
            {
                if (!_warnedNormalMap)
                {
                    _warnedNormalMap = true;
                    Debug.LogWarning("[GlowLights] 找不到 Light2D 的 m_NormalMapQuality／m_NormalMapDistance 欄位（URP 改版？），法線打光未啟用");
                }
                return;
            }
            NormalMapQualityField.SetValue(light, quality);
            NormalMapDistanceField.SetValue(light, distance);
        }
    }
}
