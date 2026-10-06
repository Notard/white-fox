// 애니메이션풍 셀 셰이딩 (여우·눈·코).
//  - 2단 명암: 밝은 면 / 그늘(_ShadeColor, 연보라빛)을 또렷하게 나눈다. 그림자도 같은 경계로.
//  - 림 라이트: 가장자리에 밝은 테두리
//  - 하이라이트: 작고 또렷한 반사광
//  - 외곽선: 뒷면을 노멀 방향으로 밀어 그린다 (화면에서 일정한 두께). LightMode SRPDefaultUnlit
// 색 = 정점 색 × _BaseColor × _BaseMap (털은 Blender 에서 칠한 정점 색, 바위 등 소품은 텍스처) + _EmissionColor
// 여우·바위·보석·스위치·문·계단·장식이 같은 셰이더를 써서 화풍이 하나로 맞는다.
Shader "FoxGame/FoxToon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BaseMap ("Texture", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        _ShadeColor ("Shade Color (그늘 곱하기)", Color) = (0.7, 0.72, 0.95, 1)
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.48
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.2)) = 0.025
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimWidth ("Rim Width", Range(0, 1)) = 0.28
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.45
        _SpecColor ("Highlight Color", Color) = (1, 1, 1, 1)
        _SpecSize ("Highlight Size", Range(0, 0.2)) = 0.03
        _SpecStrength ("Highlight Strength", Range(0, 1)) = 0.35
        _OutlineColor ("Outline Color", Color) = (0.28, 0.27, 0.45, 1)
        _OutlineWidth ("Outline Width (화면 비율)", Range(0, 0.02)) = 0.0032
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadeColor, _RimColor, _SpecColor, _OutlineColor, _EmissionColor;
            half _ShadeThreshold, _ShadeSoftness, _RimWidth, _RimStrength, _SpecSize, _SpecStrength;
            float _OutlineWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ToonLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = input.color;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 albedo = i.color.rgb * _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));

                // 2단 명암: 하프 램버트에 그림자를 곱한 뒤 한 경계에서 또렷하게 나눈다
                half lambert = dot(n, light.direction) * 0.5 + 0.5;
                half lit = lambert * lerp(0.35, 1, light.shadowAttenuation);
                half band = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, lit);
                // 빛 세기에 따라 너무 밝거나 어두워지지 않게 색만 가져온다
                half3 lightTint = light.color / max(max(light.color.r, light.color.g), max(light.color.b, 1e-3));
                half3 color = albedo * lerp(_ShadeColor.rgb, lightTint, band);
                color += albedo * SampleSH(n) * 0.18;

                // 림 라이트 (밝은 면 쪽 가장자리)
                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fres = 1 - saturate(dot(n, v));
                half rim = smoothstep(1 - _RimWidth, 1 - _RimWidth + 0.04, fres) * lerp(0.35, 1, band);
                color = lerp(color, _RimColor.rgb, rim * _RimStrength);

                // 또렷한 반사광
                half3 h = normalize(light.direction + v);
                half spec = step(1 - _SpecSize, saturate(dot(n, h))) * band;
                color += _SpecColor.rgb * spec * _SpecStrength;
                color += _EmissionColor.rgb;

                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half fogFactor : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float4 pos = TransformObjectToHClip(input.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(input.normalOS);
                float2 nCS = mul((float3x3)GetWorldToHClipMatrix(), nWS).xy;
                float len = max(length(nCS), 1e-4);
                // 화면에서 일정한 두께: 클립 공간에서 w 를 곱해 밀고, 가로는 화면비로 보정
                float2 offset = nCS / len * _OutlineWidth * pos.w * 2;
                offset.x *= _ScreenParams.y / _ScreenParams.x;
                pos.xy += offset;
                o.positionCS = pos;
                o.fogFactor = ComputeFogFactor(pos.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, i.fogFactor), 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
