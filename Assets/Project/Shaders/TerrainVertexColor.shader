// Terreno colorido por cor de vértice (calculada em ChunkMeshBuilder), sem texturas.
// Iluminação simples e barata para celular: luz principal + luz ambiente + neblina.
//
// Sombras: este shader só RECEBE as sombras da luz principal (árvores e pedras projetam no terreno); o terreno em si
// não projeta, então não tem passe ShadowCaster. Com as sombras desligadas no jogo, a variante sem sombras é usada e
// nada é calculado. O nível de sombras é controlado por World/ShadowController.
// Luzes adicionais também ainda não são usadas (lanterna do jogador virá numa fase futura).
Shader "PROCEDURAL_GAME/TerrainVertexColor"
{
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
            #pragma target 2.0

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  color      : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color.rgb;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);

                // Sem as palavras-chave de sombra, shadowAttenuation vale 1. Esta versão também suaviza a sombra na borda
                // da distância máxima (a sombra some aos poucos em vez de cortar).
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));

                half3 direct = mainLight.color * (saturate(dot(normalWS, mainLight.direction)) * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half3 ambient = SampleSH(normalWS);

                half3 color = input.color * (direct + ambient);
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
            #pragma target 2.0

            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
