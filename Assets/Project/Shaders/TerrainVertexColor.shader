// Terreno colorido por cor de vértice (calculada em ChunkMeshBuilder), sem texturas.
// Iluminação simples e barata para celular: luz principal + luz ambiente + neblina.
//
// Sombras dinâmicas desligadas de propósito (ficaram "quadradas" no celular e custam caro):
// este shader não recebe nem projeta sombras. Para voltar, é preciso: pragmas _MAIN_LIGHT_SHADOWS*,
// TransformWorldToShadowCoord + GetMainLight(shadowCoord) no fragment, e um passe ShadowCaster
// (que precisa incluir CommonMaterial.hlsl antes de Shadows.hlsl).
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
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color.rgb;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);

                Light mainLight = GetMainLight();

                half3 direct = mainLight.color * (saturate(dot(normalWS, mainLight.direction)) * mainLight.distanceAttenuation);
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
