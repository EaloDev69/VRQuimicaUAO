// Cubos de residuo (fallo de presión o temperatura): color plano con sombreado suave,
// borde brillante (rim) que pulsa. URP, ligero para Quest. Incluye las macros de instancing estéreo.
Shader "Molecula/JunkCube"
{
    Properties
    {
        _BaseColor ("Color base", Color) = (0.08, 0.06, 0.1, 1)
        _ShadeStrength ("Fuerza del sombreado", Range(0, 1)) = 0.5

        [Header(Borde brillante)]
        _RimColor ("Color del borde", Color) = (0.7, 0.2, 0.4, 1)
        _RimPower ("Grosor del borde (mayor = más fino)", Range(0.5, 8)) = 2.5
        _RimIntensity ("Intensidad del borde", Range(0, 4)) = 1.5

        [Header(Pulso)]
        _PulseSpeed ("Velocidad del pulso", Range(0, 10)) = 3
        _PulseAmount ("Cuánto pulsa (0 = fijo)", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "JunkUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _ShadeStrength;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half _PulseSpeed;
                half _PulseAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = n.normalWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half3 n = normalize(IN.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // Sombreado suave con una luz fija (half-lambert): da volumen sin depender de las luces de la escena
                half3 lightDir = normalize(half3(0.4, 0.8, 0.3));
                half ndl = dot(n, lightDir) * 0.5 + 0.5;
                half3 col = _BaseColor.rgb * lerp(1.0 - _ShadeStrength, 1.0, ndl);

                // Borde brillante que pulsa
                half rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                half pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed);
                half pulseFactor = lerp(1.0 - _PulseAmount, 1.0, pulse);
                col += _RimColor.rgb * rim * _RimIntensity * pulseFactor;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
