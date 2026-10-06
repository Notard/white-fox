// 섬 지형 셰이더: 월드 좌표 트라이플래너 (UV 없음, 이음매 없음).
//  윗면  = 잔디, 정점 색 R = 얼음, G = 금 간 땅
//  옆면·바닥 = 절벽
//  정점 색 A = 가려짐(AO): 절벽 아래쪽·섬 밑면을 어둡게
//  칸 구분은 아주 옅은 체커 명암 (_GridStrength)
//  셀 셰이딩: 2단 명암(하늘빛 그늘) + 외곽선 (탄젠트에 넣은 평균 노멀 방향으로 밀어, 모서리에서도 끊기지 않음)
Shader "FoxGame/Terrain"
{
    Properties
    {
        _GrassTex ("Grass", 2D) = "white" {}
        _IceTex ("Ice", 2D) = "white" {}
        _CrackTex ("Cracked", 2D) = "white" {}
        _CliffTex ("Cliff", 2D) = "white" {}
        _TopScale ("Top Tiling (per world unit)", Float) = 0.45
        _CliffScale ("Cliff Tiling (per world unit)", Float) = 0.6
        _GridOrigin ("Grid Origin (xz)", Vector) = (0, 0, 0, 0)
        _CellSize ("Cell Size", Float) = 1.5
        _GridStrength ("Grid Checker Strength", Range(0, 0.2)) = 0.045
        _TopSharpness ("Top/Side Blend", Range(0.1, 1)) = 0.35
        _ShadowTint ("Shadow Tint", Color) = (0.62, 0.68, 0.9, 1)
        _RimColor ("Rim Color", Color) = (1, 0.97, 0.9, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.18
        _IceGloss ("Ice Gloss", Range(0, 2)) = 0.9
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.5
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.2)) = 0.03
        _OutlineColor ("Outline Color", Color) = (0.24, 0.2, 0.2, 1)
        _OutlineWidth ("Outline Width (화면 비율)", Range(0, 0.02)) = 0.0022
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

            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_IceTex);   SAMPLER(sampler_IceTex);
            TEXTURE2D(_CrackTex); SAMPLER(sampler_CrackTex);
            TEXTURE2D(_CliffTex); SAMPLER(sampler_CliffTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassTex_ST, _IceTex_ST, _CrackTex_ST, _CliffTex_ST;
                float _TopScale, _CliffScale;
                float4 _GridOrigin;
                float _CellSize, _GridStrength, _TopSharpness;
                half4 _ShadowTint, _RimColor;
                half _RimStrength, _IceGloss, _ShadeThreshold, _ShadeSoftness;
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

            half3 Cliff(float3 pos, half3 n)
            {
                half3 w = abs(n);
                w = pow(w, 4);
                w /= (w.x + w.y + w.z + 1e-4);
                half3 cx = SAMPLE_TEXTURE2D(_CliffTex, sampler_CliffTex, pos.zy * _CliffScale).rgb;
                half3 cz = SAMPLE_TEXTURE2D(_CliffTex, sampler_CliffTex, pos.xy * _CliffScale).rgb;
                half3 cy = SAMPLE_TEXTURE2D(_CliffTex, sampler_CliffTex, pos.xz * _CliffScale).rgb;
                return cx * w.x + cz * w.z + cy * w.y;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                float3 pos = i.positionWS;

                // 윗면: 잔디 → 얼음 / 금 간 땅
                float2 uvTop = pos.xz * _TopScale;
                half3 grass = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, uvTop).rgb;
                half3 ice = SAMPLE_TEXTURE2D(_IceTex, sampler_IceTex, uvTop).rgb;
                half3 crack = SAMPLE_TEXTURE2D(_CrackTex, sampler_CrackTex, uvTop).rgb;
                half3 top = lerp(grass, ice, i.color.r);
                top = lerp(top, crack, i.color.g);

                // 아주 옅은 칸 체커 (윗면에만)
                float2 cell = (pos.xz - _GridOrigin.xz) / _CellSize;
                half checker = fmod(abs(floor(cell.x) + floor(cell.y)), 2.0);
                top *= 1 - checker * _GridStrength;

                // 옆면: 절벽 (밑면은 더 어둡게)
                half3 side = Cliff(pos, n);
                side *= lerp(0.7, 1.0, saturate(n.y + 1));

                half topW = smoothstep(0.75 - _TopSharpness * 0.5, 0.75 + _TopSharpness * 0.25, n.y);
                half3 albedo = lerp(side, top, topW);

                // 셀 명암: 밝은 면 / 하늘빛 그늘을 한 경계에서 나눈다 (그림자도 같은 경계)
                Light light = GetMainLight(TransformWorldToShadowCoord(pos));
                half lit = (dot(n, light.direction) * 0.5 + 0.5) * lerp(0.35, 1, light.shadowAttenuation);
                half band = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, lit);
                half3 lightTint = light.color / max(max(light.color.r, light.color.g), max(light.color.b, 1e-3));
                half3 diffuse = lerp(_ShadowTint.rgb * 0.78, lightTint, band);
                half3 ambient = SampleSH(n) * 0.12;

                half ao = lerp(0.55, 1, i.color.a);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(pos);
                half rim = pow(1 - saturate(dot(n, viewDir)), 4) * _RimStrength * topW;

                // 얼음은 빛나는 하이라이트
                half3 h = normalize(light.direction + viewDir);
                half spec = step(0.985, saturate(dot(n, h))) * _IceGloss * i.color.r * topW * band;

                half3 color = albedo * (diffuse + ambient) * ao + rim * _RimColor.rgb + spec * light.color;
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassTex_ST, _IceTex_ST, _CrackTex_ST, _CliffTex_ST;
                float _TopScale, _CliffScale;
                float4 _GridOrigin;
                float _CellSize, _GridStrength, _TopSharpness;
                half4 _ShadowTint, _RimColor;
                half _RimStrength, _IceGloss, _ShadeThreshold, _ShadeSoftness;
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 tangentOS : TANGENT; };
            struct Varyings { float4 positionCS : SV_POSITION; half fogFactor : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float4 pos = TransformObjectToHClip(input.positionOS.xyz);
                // 탄젠트 = 같은 위치 정점들의 평균 노멀 (TerrainBuilder 가 넣음)
                float3 nWS = TransformObjectToWorldNormal(input.tangentOS.xyz);
                float2 nCS = mul((float3x3)GetWorldToHClipMatrix(), nWS).xy;
                float2 offset = nCS / max(length(nCS), 1e-4) * _OutlineWidth * pos.w * 2;
                offset.x *= _ScreenParams.y / _ScreenParams.x;
                pos.xy += offset;
                o.positionCS = pos;
                o.fogFactor = ComputeFogFactor(pos.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(MixFog(_OutlineColor.rgb, i.fogFactor), 1); }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
