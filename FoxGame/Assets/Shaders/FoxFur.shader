// 여우 털 셰이더: 정점 색(Blender에서 칠한 털 색) + 부드러운 하프 램버트 조명 + 림 라이트.
Shader "FoxGame/FoxFur"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _RimColor ("Rim Color", Color) = (0.85, 0.92, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.3
        _ShadowTint ("Shadow Tint", Color) = (0.72, 0.78, 0.95, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                half4 _ShadowTint;
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
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
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
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 albedo = i.color.rgb * _BaseColor.rgb;

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half wrap = saturate(dot(n, light.direction) * 0.5 + 0.5);   // 털처럼 부드럽게 감싸는 빛
                half lit = wrap * wrap * light.shadowAttenuation * light.distanceAttenuation;
                half3 diffuse = light.color * lerp(_ShadowTint.rgb * 0.35, 1, lit) * lit;

                half3 ambient = SampleSH(n);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half rim = pow(1 - saturate(dot(n, viewDir)), _RimPower) * _RimStrength;

                half3 color = albedo * (diffuse + ambient) + rim * _RimColor.rgb;
                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
