// 阿甲 3D 的卡通著色：albedo 貼圖（或頂點色後備）＋固定方向的硬色階明暗＋rim light＋硬邊高光＋倒殼描邊。
// 專案用 URP 2D Renderer，它只畫 LightMode＝Universal2D 與 SRPDefaultUnlit 的 pass：主體用前者、描邊用後者。
// 明暗不吃場景燈（和 Blender 預覽一樣只看法線與固定方向），角色左右翻轉時明暗也一致。
// 貼圖與通道定義（hero3d.py 產出）：
//   _BaseMap  albedo（sRGB）；沒有貼圖時是白圖，顏色全由頂點色決定（_VertexColor＝1）。
//   _MaskMap  linear：R＝高光遮罩、G＝陰影偏移（0.5 不偏移，越低越容易進陰影）、
//             B＝材質類別（0 皮膚、0.33 殼、0.66 布、1 金屬／眼睛），選陰影色用。_UseMask＝0 時不讀。
//   頂點色    RGB＝材質固有色（sRGB，後備）、A＝描邊粗細倍率。
//   TEXCOORD1 描邊用的平滑法線（切線空間 xy，z 重建）；全 0 時退回頂點法線。
// 貼圖本身已畫有明暗（Meshy 生成的 albedo 自帶 AO 與筆觸）時把 _RampStrength 調低，色階明暗只疊一部分，
// 臉上不會碎成一塊塊陰影；1＝原本的完整色階。
// 不用任何 keyword（每個 pass 只有一個變體），開關都是 0／1 的數值。
Shader "Sidescroller/AhJiaToon"
{
    Properties
    {
        _BaseMap ("Base Map (Albedo)", 2D) = "white" {}
        _MaskMap ("Mask Map (R Spec, G Shadow Offset, B Material)", 2D) = "linearGrey" {}
        [ToggleUI] _UseMask ("Use Mask Map", Float) = 0
        _VertexColor ("Vertex Color Weight", Range(0, 1)) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)

        [Header(Shading)]
        _LightDir ("Light Direction (World)", Vector) = (-0.35, 0.8, -0.5, 0)
        _Step1 ("Shadow Step", Range(0, 1)) = 0.38
        _Step2 ("Light Step", Range(0, 1)) = 0.62
        _MidMix ("Mid Band (0 Shadow, 1 Lit)", Range(0, 1)) = 0.55
        _RampStrength ("Ramp Strength (Lower for Painted Albedo)", Range(0, 1)) = 1
        _ShadowOffset ("Mask G Shadow Offset", Range(0, 1)) = 0.3
        _ShadowColor ("Shadow (No Mask)", Color) = (0.58, 0.52, 0.6, 1)
        _ShadowSkin ("Shadow Skin", Color) = (0.78, 0.55, 0.6, 1)
        _ShadowShell ("Shadow Shell", Color) = (0.6, 0.47, 0.55, 1)
        _ShadowCloth ("Shadow Cloth", Color) = (0.5, 0.55, 0.72, 1)
        _ShadowMetal ("Shadow Metal / Eye", Color) = (0.55, 0.5, 0.55, 1)

        [Header(Rim and Specular)]
        _RimColor ("Rim Color", Color) = (1, 0.93, 0.8, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.45
        _RimWidth ("Rim Width", Range(0, 1)) = 0.28
        _RimDir ("Rim Direction (World)", Vector) = (0.8, 0.25, 0.45, 0)
        _SpecColor ("Spec Color", Color) = (1, 1, 1, 1)
        _SpecSize ("Spec Size", Range(0, 0.5)) = 0.06

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.231, 0.141, 0.078, 1)
        _OutlineWidth ("Outline Width (px @ 1080p)", Range(0, 10)) = 2.5
        _OutlineDepth ("Outline Depth Push (m)", Range(0, 0.1)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _MaskMap_ST;
            half _UseMask;
            half _VertexColor;
            half4 _Tint;
            float4 _LightDir;
            half _Step1;
            half _Step2;
            half _MidMix;
            half _RampStrength;
            half _ShadowOffset;
            half4 _ShadowColor;
            half4 _ShadowSkin;
            half4 _ShadowShell;
            half4 _ShadowCloth;
            half4 _ShadowMetal;
            half4 _RimColor;
            half _RimStrength;
            half _RimWidth;
            float4 _RimDir;
            half4 _SpecColor;
            half _SpecSize;
            half4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineDepth;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MaskMap);
        SAMPLER(sampler_MaskMap);
        ENDHLSL

        Pass
        {
            Name "Toon"
            Tags { "LightMode" = "Universal2D" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            // 0→1 的硬邊，寬度約 1 像素（fwidth 抗鋸齒，色階不會閃）
            half HardStep(half edge, half x)
            {
                half w = max(fwidth(x), 1e-4h);
                return saturate((x - edge) / w + 0.5h);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 l = normalize(_LightDir.xyz);
                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // 頂點色是 sRGB，專案是 Linear 色彩空間，Unity 不會自動轉換頂點色
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(i.uv, _BaseMap)).rgb;
                albedo *= lerp(half3(1.0h, 1.0h, 1.0h), SRGBToLinear(i.color.rgb), _VertexColor);
                half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, TRANSFORM_TEX(i.uv, _MaskMap));
                mask = lerp(half4(0.0h, 0.5h, 0.0h, 0.0h), mask, _UseMask);

                // 陰影色：依材質類別選一列（B 四捨五入到 0／1／2／3），沒有 mask 時用通用陰影色
                half category = mask.b * 3.0h;
                half3 shadow = category < 0.5h ? _ShadowSkin.rgb
                    : (category < 1.5h ? _ShadowShell.rgb : (category < 2.5h ? _ShadowCloth.rgb : _ShadowMetal.rgb));
                shadow = lerp(_ShadowColor.rgb, shadow, _UseMask);

                // 3 階：陰影／中間／亮；mask G 往下推容易進陰影
                half ndl = dot(n, l) * 0.5h + 0.5h + (mask.g - 0.5h) * _ShadowOffset;
                half3 mid = lerp(shadow, half3(1.0h, 1.0h, 1.0h), _MidMix);
                half3 ramp = lerp(shadow, mid, HardStep(_Step1, ndl));
                ramp = lerp(ramp, half3(1.0h, 1.0h, 1.0h), HardStep(_Step2, ndl));
                ramp = lerp(half3(1.0h, 1.0h, 1.0h), ramp, _RampStrength);
                half3 color = albedo * ramp;

                // rim light：輪廓邊緣（法線與視線垂直）且朝 _RimDir 那側的一道亮邊
                half fresnel = 1.0h - saturate(dot(n, v));
                half rimSide = saturate(dot(n, normalize((half3)_RimDir.xyz)) * 2.0h);
                half rim = HardStep(1.0h - _RimWidth, fresnel) * rimSide * _RimStrength;
                color = lerp(color, _RimColor.rgb * albedo + _RimColor.rgb * 0.35h, rim);

                // 硬邊高光（帽子、扣環、眼睛、鼻子；mask R）
                half nh = dot(n, normalize(l + v));
                color += _SpecColor.rgb * (HardStep(1.0h - _SpecSize, nh) * mask.r);

                return half4(color * _Tint.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 smoothNormalTS : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            // 倒殼沿平滑法線在螢幕上往外推固定像素（以 1080 高為基準，換解析度、鏡頭遠近粗細都一樣），
            // 粗細再乘頂點色 A。平滑法線由切線空間還原，跟著蒙皮後的法線／切線轉動。
            // 另外沿視線往後推 _OutlineDepth，貼在一起的零件（殼條、帽簷）之間不會冒出零碎的內線。
            Varyings Vert(Attributes i)
            {
                Varyings o;
                float3 normalWS = TransformObjectToWorldNormal(i.normalOS);
                float3 tangentWS = TransformObjectToWorldDir(i.tangentOS.xyz);
                float3 bitangentWS = cross(normalWS, tangentWS) * (i.tangentOS.w * GetOddNegativeScale());
                float2 xy = i.smoothNormalTS;
                float z = sqrt(saturate(1.0 - dot(xy, xy)));
                float3 outlineWS = xy.x * tangentWS + xy.y * bitangentWS + z * normalWS;

                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                positionWS -= GetWorldSpaceNormalizeViewDir(positionWS) * _OutlineDepth;
                o.positionCS = TransformWorldToHClip(positionWS);
                // 裁切空間 x 已除過畫面比例，先換回像素比例再正規化，方向才不會偏
                float2 dir = mul((float3x3)GetWorldToHClipMatrix(), outlineWS).xy;
                dir.x *= _ScreenParams.x / _ScreenParams.y;
                dir *= rsqrt(max(dot(dir, dir), 1e-8));
                float2 ndcPerPixel = float2(_ScreenParams.y / _ScreenParams.x, 1.0) / 540.0;
                o.positionCS.xy += dir * ndcPerPixel * (_OutlineWidth * i.color.a * o.positionCS.w);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
