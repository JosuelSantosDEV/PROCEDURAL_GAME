// Água dos lagos: OPACA, sem textura e sem textura de profundidade (barata no celular). A cor vem dos vértices da malha (raso claro,
// fundo escuro, calculada em WaterMeshBuilder). Por cima da cor: um brilho do sol sobre pequenas ondas (cálculo no próprio pixel,
// sem textura), um leve reflexo da cor do céu/neblina nas bordas (fresnel) e a neblina do jogo.
// Desenhada ANTES do terreno (fila Geometry-10): o terreno que fica embaixo da água é descartado pelo teste de profundidade e
// não é sombreado à toa. Os dois lados são desenhados: visto de baixo (nadando) o plano é uma "tampa" escura.
Shader "PROCEDURAL_GAME/Water"
{
    Properties
    {
        _Glint ("Brilho do sol", Range(0, 2)) = 0.6
        _GlintSharpness ("Nitidez do brilho", Range(8, 256)) = 90
        _WaveStrength ("Força das ondas", Range(0, 0.15)) = 0.035
        _SkyReflection ("Reflexo do céu nas bordas", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Glint;
                half _GlintSharpness;
                half _WaveStrength;
                half _SkyReflection;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  color      : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.color = input.color.rgb;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);

                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                Light mainLight = GetMainLight();
                half3 ambient = SampleSH(half3(0, 1, 0));

                // Visto de baixo (câmera dentro d'água): tampa escura, sem brilho.
                if (!IS_FRONT_VFACE(isFront, true, false))
                {
                    half3 under = input.color * (ambient + mainLight.color * 0.1h) * 0.5h;
                    return half4(MixFog(under, input.fogFactor), 1.0h);
                }

                // Ondas: a inclinação da superfície varia no espaço e no tempo com senos baratos (sem textura).
                float2 p = input.positionWS.xz;
                float t = _Time.y;
                float2 slope = float2(sin(p.x * 0.7 + t * 0.9) + sin(p.y * 1.1 - t * 0.7),
                                      cos(p.x * 1.3 - t * 0.6) + sin(p.y * 0.9 + t * 1.1)) * _WaveStrength;
                half3 normal = normalize(half3(slope.x, 1.0h, slope.y));

                half3 halfDir = normalize(mainLight.direction + viewDir);
                half glint = pow(saturate(dot(normal, halfDir)), _GlintSharpness) * _Glint;

                half3 color = input.color * (ambient + mainLight.color * 0.35h) + mainLight.color * glint;

                // Fresnel: de longe (ângulo raso) a água reflete a cor do céu/neblina.
                half fresnel = pow(1.0h - saturate(dot(normal, viewDir)), 4.0h) * _SkyReflection;
                color = lerp(color, unity_FogColor.rgb, fresnel);

                return half4(MixFog(color, input.fogFactor), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Glint;
                half _GlintSharpness;
                half _WaveStrength;
                half _SkyReflection;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
