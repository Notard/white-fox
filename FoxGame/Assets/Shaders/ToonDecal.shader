// 얼굴에 붙이는 2D 그림 (애니메이션 눈). 투명한 곳은 잘라내고(알파 컷), 셀 명암은 살짝만.
// 표정은 FoxExpression 이 _BaseMap 을 바꿔 끼운다 (뜬 눈 / 웃는 눈 / 감은 눈).
Shader "FoxGame/ToonDecal"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.45
        _ShadeColor ("Shade Color", Color) = (0.82, 0.83, 0.97, 1)
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        Pass
        {
            Name "Decal"
            Tags { "LightMode" = "UniversalForward" }
            Offset -1, -1   // 얼굴 표면 바로 앞에 그린다

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor, _ShadeColor;
                half _Cutoff, _ShadeThreshold;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; half fogFactor : TEXCOORD2; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(tex.a - _Cutoff);
                Light light = GetMainLight();
                half lambert = dot(normalize(i.normalWS), light.direction) * 0.5 + 0.5;
                half band = step(_ShadeThreshold, lambert);
                half3 color = tex.rgb * lerp(_ShadeColor.rgb, 1, band);
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
    }
}
