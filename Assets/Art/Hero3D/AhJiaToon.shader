// 阿甲 3D 的卡通著色：頂點色（sRGB）＋固定方向的 3 階明暗＋倒殼描邊。
// 專案用 URP 2D Renderer，它只畫 LightMode＝Universal2D 與 SRPDefaultUnlit 的 pass：主體用前者、描邊用後者。
// 明暗不吃場景燈（和 Blender 預覽一樣只看法線與固定方向），角色左右翻轉時明暗也一致。
Shader "Sidescroller/AhJiaToon"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _LightDir ("Light Direction (World)", Vector) = (-0.35, 0.8, -0.5, 0)
        _Step1 ("Shadow Step", Range(0, 1)) = 0.38
        _Step2 ("Light Step", Range(0, 1)) = 0.62
        _Shade1 ("Shadow Level", Range(0, 1)) = 0.55
        _Shade2 ("Mid Level", Range(0, 1)) = 0.8
        _OutlineColor ("Outline Color", Color) = (0.231, 0.141, 0.078, 1)
        _OutlineWidth ("Outline Width (m)", Float) = 0.011
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float4 _LightDir;
            half _Step1;
            half _Step2;
            half _Shade1;
            half _Shade2;
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            half4 color : COLOR;
        };
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

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float ndl = dot(normalize(i.normalWS), normalize(_LightDir.xyz)) * 0.5 + 0.5;
                half level = ndl < _Step1 ? _Shade1 : (ndl < _Step2 ? _Shade2 : 1.0h);
                // 頂點色是 sRGB，專案是 Linear 色彩空間，Unity 不會自動轉換頂點色
                half3 baseColor = SRGBToLinear(i.color.rgb);
                return half4(baseColor * level * _Tint.rgb, 1.0h);
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

            Varyings Vert(Attributes i)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.positionCS = TransformWorldToHClip(positionWS + normalWS * _OutlineWidth);
                o.normalWS = normalWS;
                o.color = i.color;
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
