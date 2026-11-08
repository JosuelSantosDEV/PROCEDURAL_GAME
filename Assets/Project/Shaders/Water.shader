// Água dos lagos: OPACA, sem textura e sem textura de profundidade (barata no celular). A cor de cada vértice (gravada em sRGB, por
// causa da precisão de 8 bits nos tons escuros) vem da profundidade (raso claro, fundo escuro, ver WaterMeshBuilder). Por cima da cor:
// luz própria (a água não fica preta com o ambiente escuro), reflexo da cor do céu/neblina com fresnel (Schlick: de cima reflete pouco,
// rasante vira espelho), brilho do sol sobre pequenas ondas (senos no próprio pixel) e a neblina do jogo.
// Desenhada ANTES do terreno (fila Geometry-10): o terreno que fica embaixo da água é descartado pelo teste de profundidade e
// não é sombreado à toa. Os dois lados são desenhados: visto de baixo (nadando) o plano é o "teto" da água, claro e com ondulação,
// na cor da neblina submersa (sem faixa preta quando a câmera passa pela superfície).
Shader "PROCEDURAL_GAME/Water"
{
    Properties
    {
        _Glint ("Brilho do sol", Range(0, 2)) = 0.8
        _GlintSharpness ("Nitidez do brilho", Range(8, 256)) = 90
        _WaveStrength ("Força das ondas", Range(0, 0.15)) = 0.05
        _Reflectivity ("Reflexo olhando de cima (F0)", Range(0, 1)) = 0.2
        _SkyBrightness ("Brilho do reflexo do céu", Range(0, 2)) = 1.0
        _SelfLight ("Luz própria da água", Range(0, 1)) = 0.3
        _LidBrightness ("Brilho do teto visto de baixo", Range(0.5, 6)) = 2.5
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
                half _Reflectivity;
                half _SkyBrightness;
                half _SelfLight;
                half _LidBrightness;
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

                float2 p = input.positionWS.xz;
                float t = _Time.y;

                // Visto de baixo (câmera dentro d'água): o teto da água, na cor da neblina submersa (mais clara que ela, com
                // ondulação de luz). Perto da câmera é um pouco mais claro; longe a neblina o dissolve. Nada de preto.
                if (!IS_FRONT_VFACE(isFront, true, false))
                {
                    half ripple = 0.5h + 0.5h * (half)(sin(p.x * 0.9 + t * 0.8) * sin(p.y * 1.1 - t * 0.6));
                    half3 lid = unity_FogColor.rgb * _LidBrightness * (0.75h + 0.5h * ripple) + mainLight.color * 0.05h * ripple;
                    return half4(MixFog(lid, input.fogFactor), 1.0h);
                }

                // Cor do vértice: sRGB -> linear (projeto em Linear).
                half3 baseColor = input.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                baseColor = FastSRGBToLinear(baseColor);
                #endif

                // Ondas: a inclinação da superfície varia no espaço e no tempo com senos baratos (sem textura).
                float2 slope = float2(sin(p.x * 0.7 + t * 0.9) + sin(p.y * 1.1 - t * 0.7),
                                      cos(p.x * 1.3 - t * 0.6) + sin(p.y * 0.9 + t * 1.1)) * _WaveStrength;
                half3 normal = normalize(half3(slope.x, 1.0h, slope.y));

                half3 halfDir = normalize(mainLight.direction + viewDir);
                half glint = pow(saturate(dot(normal, halfDir)), _GlintSharpness) * _Glint;

                // Cor da água em si (luz própria evita o preto com ambiente escuro).
                half3 diffuse = baseColor * (ambient + mainLight.color * 0.35h + _SelfLight);

                // Fresnel de Schlick: de cima reflete pouco (_Reflectivity); em ângulo raso reflete o céu/neblina quase por inteiro.
                half ndv = saturate(dot(normal, viewDir));
                half fresnel = _Reflectivity + (1.0h - _Reflectivity) * pow(1.0h - ndv, 5.0h);
                half3 sky = unity_FogColor.rgb * _SkyBrightness;

                half3 color = lerp(diffuse, sky, fresnel) + mainLight.color * glint;

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
                half _Reflectivity;
                half _SkyBrightness;
                half _SelfLight;
                half _LidBrightness;
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
