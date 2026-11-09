// Terreno colorido por cor de vértice (calculada em ChunkMeshBuilder) e, opcionalmente, com texturas de chão que se repetem.
// Iluminação simples e barata para celular: luz principal + luz ambiente + neblina.
//
// Texturas do chão (opcionais): até 3 (_GroundTex, _GroundTex2, _GroundTex3), uma por zona do mundo (estação do ano). Amostradas
// pela posição do mundo (x, z), repetindo a cada 1 / _GroundTiling metros. O peso de cada textura em cada ponto vem do UV0 da
// malha (x = peso da 1ª, y = peso da 2ª, o que sobra até 1 = peso da 3ª): no meio de uma zona só uma textura pesa e só ela é
// lida; perto da fronteira entre zonas, duas se misturam. A cor de vértice continua mandando no tom (altura, variação) e a
// rocha das encostas fica sem textura: o alfa da cor de vértice (1 - rocha) diz quanto da textura vale em cada ponto.
//
// Qualidade do solo (_GroundNormalOn = 1, opção do menu Desempenho): também lê o mapa de normal de cada textura e usa o
// relevo na iluminação. Desligada, nenhum mapa de normal é lido (o ramo é uniforme, então não custa nada).
//
// Areia dos lagos: duas texturas a mais (_SandTex = areia da margem, _BedTex = areia lisa do fundo), com pesos no UV1 da malha
// (x = areia, y = quanto dela é a do fundo; calculado em LakeMap). Onde a areia pesa 1, as texturas das zonas nem são lidas; na
// transição de volta para a floresta, as duas se misturam. Sem lagos o UV1 é zero e nada disso é lido.
//
// Sem texturas, o WorldGenerator põe uma textura branca e _GroundStrength = 0: o resultado é só a cor de vértice.
//
// Variação da textura (para não formar o padrão de quadradinhos): o chão é dividido em manchas de formato irregular e cada mancha lê a
// textura com um deslocamento próprio. A divisão vem de uma soma de senos da posição, calculada POR VÉRTICE (1 m entre vértices; custa
// nada por pixel), e fica no w de positionWS, sem interpolador novo. Dentro de uma mancha é uma leitura só, como antes; só na faixa
// estreita de transição (cerca de 1/4 dos pixels) são duas. Sem rotação: as normais continuam valendo sem conta extra. As derivadas são as da
// coordenada SEM deslocamento: o mipmap não salta na fronteira. O global _TileVariationOff (ferramenta DEV) desliga para comparar o FPS.
//
// Sombras: este shader só RECEBE as sombras da luz principal (árvores e pedras projetam no terreno); o terreno em si
// não projeta, então não tem passe ShadowCaster. Com as sombras desligadas no jogo, a variante sem sombras é usada e
// nada é calculado. O nível de sombras é controlado por World/ShadowController.
// Luzes adicionais também ainda não são usadas (lanterna do jogador virá numa fase futura).
Shader "PROCEDURAL_GAME/TerrainVertexColor"
{
    Properties
    {
        [NoScaleOffset] _GroundTex ("Textura do chão (zona 1)", 2D) = "white" {}
        [NoScaleOffset] _GroundTex2 ("Textura do chão (zona 2)", 2D) = "white" {}
        [NoScaleOffset] _GroundTex3 ("Textura do chão (zona 3)", 2D) = "white" {}
        [NoScaleOffset] _GroundNrm ("Normal do chão (zona 1)", 2D) = "bump" {}
        [NoScaleOffset] _GroundNrm2 ("Normal do chão (zona 2)", 2D) = "bump" {}
        [NoScaleOffset] _GroundNrm3 ("Normal do chão (zona 3)", 2D) = "bump" {}
        [NoScaleOffset] _SandTex ("Areia da margem dos lagos", 2D) = "white" {}
        [NoScaleOffset] _BedTex ("Areia do fundo dos lagos", 2D) = "white" {}
        [NoScaleOffset] _SandNrm ("Normal da areia da margem", 2D) = "bump" {}
        [NoScaleOffset] _BedNrm ("Normal da areia do fundo", 2D) = "bump" {}
        _GroundTiling ("Repetições por metro", Float) = 0.25
        _GroundStrength ("Força da textura", Range(0, 1)) = 0
        _GroundNormalOn ("Qualidade do solo (0 ou 1)", Float) = 0
        _VertexInfluence ("Influência da cor de vértice no tom", Range(0, 1)) = 0.5
        _LumaRef ("Luminância de referência da cor de vértice", Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            // 3.0: derivadas (ddx/ddy) e SAMPLE_TEXTURE2D_GRAD, para ler cada textura só onde ela pesa.
            #pragma target 3.0

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GroundTex);  SAMPLER(sampler_GroundTex);
            TEXTURE2D(_GroundTex2);
            TEXTURE2D(_GroundTex3);
            TEXTURE2D(_GroundNrm);
            TEXTURE2D(_GroundNrm2);
            TEXTURE2D(_GroundNrm3);
            TEXTURE2D(_SandTex);
            TEXTURE2D(_BedTex);
            TEXTURE2D(_SandNrm);
            TEXTURE2D(_BedNrm);

            // Mesmo bloco nos dois passes: o SRP Batcher exige.
            CBUFFER_START(UnityPerMaterial)
                float _GroundTiling;
                half _GroundStrength;
                half _GroundNormalOn;
                half _VertexInfluence;
                half _LumaRef;
            CBUFFER_END

            // Global, fora do bloco do material (ferramenta DEV). Sem ninguém definir, vale 0: variação ligada.
            float _TileVariationOff;

            // Peso mínimo para valer a leitura de uma textura.
            static const half MinWeight = 0.004h;

            // Quantas manchas por unidade do campo de senos: com a textura repetindo a cada 4 m, dá manchas de uns 4 a 8 m.
            static const float TilePatchScale = 1.6;

            // Faixa (dentro de cada mancha, 0 a 1) em que uma mancha se mistura com a seguinte. Mais larga = transição mais suave e mais leituras duplas.
            static const float TileBlendStart = 0.38;
            static const float TileBlendEnd = 0.62;

            // Campo suave e sem repetição visível: senos de frequências sem múltiplo comum, um deformando o outro. Função só da posição: continua
            // igual na divisa entre chunks.
            float TilePatchField(float2 xz)
            {
                return (sin(xz.x * 0.061 + 1.7 * sin(xz.y * 0.043))
                      + sin(xz.y * 0.053 + 1.3 * sin(xz.x * 0.037))
                      + 0.5 * sin((xz.x + xz.y) * 0.127) + 3.0) * TilePatchScale;
            }

            // Lê a textura com o deslocamento da mancha; só na faixa de transição lê duas vezes. O ramo é seguro: o mipmap vem das derivadas passadas.
            half4 SampleVaried(TEXTURE2D_PARAM(tex, samp), float2 uvA, float2 uvB, half blend, float2 dx, float2 dy)
            {
                if (blend <= 0.0h) return SAMPLE_TEXTURE2D_GRAD(tex, samp, uvA, dx, dy);
                if (blend >= 1.0h) return SAMPLE_TEXTURE2D_GRAD(tex, samp, uvB, dx, dy);
                return lerp(SAMPLE_TEXTURE2D_GRAD(tex, samp, uvA, dx, dy), SAMPLE_TEXTURE2D_GRAD(tex, samp, uvB, dx, dy), blend);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 weights    : TEXCOORD0; // pesos das texturas 1 e 2 (a 3 recebe o resto)
                float2 sand       : TEXCOORD1; // x = peso da areia do lago, y = quanto dela é a do fundo
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : TEXCOORD0; // rgb = cor de vértice; a = quanto da textura vale (1 - rocha)
                half3  normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                float4 positionWS : TEXCOORD3; // xyz = posição; w = campo das manchas de variação da textura
                half2  weights    : TEXCOORD4;
                half2  sand       : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = float4(positions.positionWS, TilePatchField(positions.positionWS.xz));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.weights = input.weights;
                output.sand = input.sand;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);

                // Texturas do chão. As derivadas são calculadas uma vez, fora dos 'if': dentro de um ramo a escolha do mipmap
                // não pode depender de pixels vizinhos, e assim cada textura só é lida onde o peso dela passa do mínimo.
                float2 groundUv = input.positionWS.xz * _GroundTiling;
                float2 ddxUv = ddx(groundUv);
                float2 ddyUv = ddy(groundUv);

                // Mancha atual e seguinte: cada uma com o seu deslocamento (razão áurea e raiz de 2: não se repetem tão cedo).
                float patchField = _TileVariationOff > 0.5 ? 0.0 : input.positionWS.w;
                float patchIndex = floor(patchField);
                float2 uvA = groundUv + frac(patchIndex * float2(0.618034, 0.414214));
                float2 uvB = groundUv + frac((patchIndex + 1.0) * float2(0.618034, 0.414214));
                half patchBlend = smoothstep(TileBlendStart, TileBlendEnd, frac(patchField));

                // A areia do lago toma o lugar das texturas das zonas: onde ela pesa 1, as zonas nem são lidas.
                half sandW = input.sand.x;
                half bedW = input.sand.y;
                half zoneW = saturate(1.0h - sandW);

                half w0 = input.weights.x * zoneW;
                half w1 = input.weights.y * zoneW;
                half w2 = saturate(1.0h - input.weights.x - input.weights.y) * zoneW;

                half3 ground = 0;
                if (w0 > MinWeight) ground += w0 * SampleVaried(TEXTURE2D_ARGS(_GroundTex, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv).rgb;
                if (w1 > MinWeight) ground += w1 * SampleVaried(TEXTURE2D_ARGS(_GroundTex2, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv).rgb;
                if (w2 > MinWeight) ground += w2 * SampleVaried(TEXTURE2D_ARGS(_GroundTex3, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv).rgb;

                half beachW = 1.0h - bedW;
                if (sandW > MinWeight)
                {
                    half3 sand = 0;
                    if (beachW > MinWeight) sand += beachW * SampleVaried(TEXTURE2D_ARGS(_SandTex, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv).rgb;
                    if (bedW > MinWeight) sand += bedW * SampleVaried(TEXTURE2D_ARGS(_BedTex, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv).rgb;
                    ground += sandW * sand;
                }

                // O brilho da cor de vértice (altura, variação) modula a textura, para o relevo não ficar chapado.
                half luma = dot(input.color.rgb, half3(0.299h, 0.587h, 0.114h));
                half variation = lerp(1.0h, clamp(luma / max(_LumaRef, 0.0001h), 0.5h, 1.6h), _VertexInfluence);
                half weight = input.color.a * _GroundStrength;
                half3 albedo = lerp(input.color.rgb, ground * variation, weight);

                // Qualidade do solo: relevo pelo mapa de normal (planar em x, z: a tangente é o eixo x do mundo projetado na superfície).
                if (_GroundNormalOn > 0.5h)
                {
                    half3 tangentNormal = 0;
                    if (w0 > MinWeight) tangentNormal += w0 * UnpackNormal(SampleVaried(TEXTURE2D_ARGS(_GroundNrm, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv));
                    if (w1 > MinWeight) tangentNormal += w1 * UnpackNormal(SampleVaried(TEXTURE2D_ARGS(_GroundNrm2, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv));
                    if (w2 > MinWeight) tangentNormal += w2 * UnpackNormal(SampleVaried(TEXTURE2D_ARGS(_GroundNrm3, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv));
                    if (sandW > MinWeight)
                    {
                        // Areia: o mesmo relevo da "Qualidade do solo". Sem mapa de normal, o padrão ("bump") dá relevo plano.
                        half3 sandNormal = 0;
                        if (beachW > MinWeight) sandNormal += beachW * UnpackNormal(SampleVaried(TEXTURE2D_ARGS(_SandNrm, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv));
                        if (bedW > MinWeight) sandNormal += bedW * UnpackNormal(SampleVaried(TEXTURE2D_ARGS(_BedNrm, sampler_GroundTex), uvA, uvB, patchBlend, ddxUv, ddyUv));
                        tangentNormal += sandW * sandNormal;
                    }

                    tangentNormal = normalize(tangentNormal);

                    half3 tangent = normalize(half3(1.0h, 0.0h, 0.0h) - normalWS * normalWS.x);
                    half3 bitangent = cross(tangent, normalWS);
                    half3 detailed = normalize(tangent * tangentNormal.x + bitangent * tangentNormal.y + normalWS * tangentNormal.z);

                    // A rocha das encostas não recebe relevo, como não recebe a textura.
                    normalWS = normalize(lerp(normalWS, detailed, weight));
                }

                // Sem as palavras-chave de sombra, shadowAttenuation vale 1. Esta versão também suaviza a sombra na borda
                // da distância máxima (a sombra some aos poucos em vez de cortar).
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS.xyz);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS.xyz, half4(1, 1, 1, 1));

                half3 direct = mainLight.color * (saturate(dot(normalWS, mainLight.direction)) * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half3 ambient = SampleSH(normalWS);

                half3 color = albedo * (direct + ambient);
                color = MixFog(color, input.fogFactor);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _GroundTiling;
                half _GroundStrength;
                half _GroundNormalOn;
                half _VertexInfluence;
                half _LumaRef;
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
