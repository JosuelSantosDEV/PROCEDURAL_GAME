// Água dos lagos: TRANSPARENTE por profundidade, sem textura, sem textura de profundidade e sem cópia da tela (barata no celular; o
// pacote Procedural Water Shader usa essas três coisas e por isso não serve para o jogo todo). A malha (WaterMeshBuilder) leva, por
// vértice, a PROFUNDIDADE (vermelho, 0 na margem ... 1 na profundidade máxima). Nas bordas a água é quase transparente (aparece
// o chão de areia, sem choque de cor) e ganha cor e opacidade conforme fica mais funda. Por cima: luz própria (que some à noite:
// escala com a claridade da cena), reflexo da cor do céu/neblina com fresnel de Schlick, brilho do sol sobre ondas (soma de 4
// senos em direções diferentes) e a neblina do jogo.
// ESPUMA/ONDAS DO PLAYER: o WaterWakeController envia até 20 pontos (posição, idade, força) por onde o Player andou na água; aqui
// cada ponto vira um anel que se expande e uma espuma que acompanha o rastro e some com o tempo (o splash de entrada é uma rajada
// de pontos). Sem pontos ativos, o laço nem roda. Não há espuma fixa na margem.
// Desenhada DEPOIS do terreno (fila Transparent, sem escrever profundidade); o terreno sob a água é sombreado normalmente.
// Os dois lados são desenhados: visto de baixo (nadando) o plano é o "teto" da água, opaco, na cor da neblina submersa.
Shader "PROCEDURAL_GAME/Water"
{
    Properties
    {
        _ShallowColor ("Cor da água rasa", Color) = (0.30, 0.50, 0.47, 1)
        _DeepColor ("Cor da água funda", Color) = (0.09, 0.27, 0.30, 1)
        _EdgeFade ("Transparência: fração da profundidade até a cor cheia", Range(0.02, 1)) = 0.30
        _MaxAlpha ("Opacidade máxima (no fundo)", Range(0.3, 1)) = 0.92
        _FoamColor ("Cor da espuma do Player", Color) = (0.90, 0.96, 0.94, 1)
        _FoamAmount ("Espuma do Player", Range(0, 1)) = 0.85
        _DayLightRef ("Claridade de dia (luz própria some abaixo disto)", Range(0.05, 2)) = 0.35
        _Glint ("Brilho do sol", Range(0, 2)) = 0.8
        _GlintSharpness ("Nitidez do brilho", Range(8, 256)) = 90
        _WaveStrength ("Força das ondas", Range(0, 0.15)) = 0.08
        _Reflectivity ("Reflexo olhando de cima (F0)", Range(0, 1)) = 0.2
        _SkyBrightness ("Brilho do reflexo do céu", Range(0, 2)) = 1.0
        _SelfLight ("Luz própria da água (de dia)", Range(0, 1)) = 0.5
        _LidBrightness ("Brilho do teto visto de baixo", Range(0.5, 6)) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fog
            // Luzes extras (lanterna) por pixel, com máscara de luz (cookie).
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Rastro do Player (WaterWakeController). Tem que bater com WaterWakeController.MaxWakes e .Life.
            #define WAKE_MAX 20
            #define WAKE_LIFE 2.0

            float4 _WakeData[WAKE_MAX];   // x, y = posição (x, z) no mundo; z = idade (s; negativa = ainda não nasceu); w = força
            float _WakeCount;             // 0 = nenhum ponto ativo (o laço nem roda)

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half _EdgeFade;
                half _MaxAlpha;
                half4 _FoamColor;
                half _FoamAmount;
                half _DayLightRef;
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
                half   depth01    : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.depth01 = input.color.r;
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

                // Visto de baixo (câmera dentro d'água): o teto da água, OPACO, na cor da neblina submersa (mais clara que ela,
                // com ondulação de luz). Perto da câmera é um pouco mais claro; longe a neblina o dissolve. Nada de preto.
                if (!IS_FRONT_VFACE(isFront, true, false))
                {
                    half ripple = 0.5h + 0.5h * (half)(sin(p.x * 0.9 + t * 0.8) * sin(p.y * 1.1 - t * 0.6));
                    half3 lid = unity_FogColor.rgb * _LidBrightness * (0.75h + 0.5h * ripple) + mainLight.color * 0.05h * ripple;
                    return half4(MixFog(lid, input.fogFactor), 1.0h);
                }

                // Profundidade: cor (raso -> fundo) e opacidade (quase transparente na margem, cheia a partir de _EdgeFade).
                half depth01 = saturate(input.depth01);
                half3 baseColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, smoothstep(0.0h, 1.0h, depth01));
                half alpha = smoothstep(0.0h, _EdgeFade, depth01) * _MaxAlpha;

                // Rastro do Player: espuma que acompanha o movimento e some, mais um anel que se expande (balança a normal).
                half foam = 0.0h;
                float2 wakeBump = float2(0.0, 0.0);
                if (_WakeCount > 0.5)
                {
                    float foamNoise = 0.5 + 0.25 * (sin(p.x * 3.1 + t * 1.3) + sin(p.y * 2.7 - t * 1.1) + sin((p.x - p.y) * 1.9 + t * 0.7));
                    float foamSum = 0.0;

                    for (int i = 0; i < WAKE_MAX; i++)
                    {
                        float4 w = _WakeData[i];
                        float age = w.z;
                        if (age < 0.0 || age > WAKE_LIFE || w.w <= 0.0) continue;

                        float k = age / WAKE_LIFE;
                        float fade = (1.0 - k) * (1.0 - k);
                        float2 delta = p - w.xy;
                        float dist = length(delta);

                        float radius = (0.35 + age * 1.1) * w.w;
                        float ring = 1.0 - smoothstep(0.0, 0.30 * w.w + 0.12, abs(dist - radius));
                        float disk = 1.0 - smoothstep(0.0, radius + 0.2, dist);

                        foamSum += (ring * 0.9 + disk * 0.6) * fade;
                        wakeBump += (delta / max(dist, 0.05)) * cos(dist * 7.0 - age * 9.0) * ring * fade;
                    }

                    foam = (half)saturate(foamSum * (0.65 + 0.7 * foamNoise)) * _FoamAmount;
                }

                // Ondas: soma de 4 senos em direções e escalas diferentes (sem faixas alinhadas), barata e sem textura.
                float2 slope = float2(0.8, 0.6) * cos(dot(p, float2(0.8, 0.6)) * 0.55 + t * 0.8)
                             + float2(-0.4, 0.9) * cos(dot(p, float2(-0.4, 0.9)) * 0.93 - t * 0.7) * 0.8
                             + float2(0.95, -0.3) * cos(dot(p, float2(0.95, -0.3)) * 1.71 + t * 1.3) * 0.5
                             + float2(-0.7, -0.7) * cos(dot(p, float2(-0.7, -0.7)) * 2.9 - t * 1.9) * 0.3;
                half3 normal = normalize(half3(slope.x * _WaveStrength + wakeBump.x * 0.2, 1.0h, slope.y * _WaveStrength + wakeBump.y * 0.2));

                half3 halfDir = normalize(mainLight.direction + viewDir);
                half glint = pow(saturate(dot(normal, halfDir)), _GlintSharpness) * _Glint;

                // Claridade da cena (0 de noite, 1 de dia): a luz própria da água segue ela, então o lago não brilha à noite.
                half lightLevel = saturate(dot(ambient + mainLight.color, half3(0.3333h, 0.3333h, 0.3333h)) / _DayLightRef);

                // Cor da água em si (a luz própria evita o preto com o ambiente escuro).
                half3 diffuse = baseColor * (ambient + mainLight.color * 0.35h + _SelfLight * lightLevel);

                // Lanterna (luzes extras): ilumina a água e faz o brilho dela nas ondas.
                half3 extraGlint = half3(0.0h, 0.0h, 0.0h);
            #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < lightCount; ++lightIndex)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    half3 lightColor = light.color * light.distanceAttenuation;
                    diffuse += baseColor * lightColor * saturate(dot(normal, light.direction));
                    half3 lightHalf = normalize(light.direction + viewDir);
                    extraGlint += lightColor * pow(saturate(dot(normal, lightHalf)), _GlintSharpness) * _Glint;
                }
            #endif

                // Fresnel de Schlick: de cima reflete pouco (_Reflectivity); em ângulo raso reflete o céu/neblina quase por inteiro.
                half ndv = saturate(dot(normal, viewDir));
                half fresnel = _Reflectivity + (1.0h - _Reflectivity) * pow(1.0h - ndv, 5.0h);
                half3 sky = unity_FogColor.rgb * _SkyBrightness;

                half3 color = lerp(diffuse, sky, fresnel) + mainLight.color * glint + extraGlint;
                color = lerp(color, _FoamColor.rgb * (ambient * 2.0h + mainLight.color * 0.5h + 0.15h * lightLevel), foam);

                // A espuma aparece mesmo onde a água é rasa e transparente.
                alpha = max(alpha, foam * 0.9h);

                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
