// Cubo comprimido (cubo de fallo de presión): metal oscuro con cada cara "hundida" hacia el centro
// y anillos de presión que viajan hacia adentro. Se calcula en el espacio del objeto (sin texturas).
// URP, con las macros de instancing estéreo para Quest.
Shader "Molecula/CompressedCube"
{
    Properties
    {
        _BaseColor ("Color del metal (bordes)", Color) = (0.29, 0.33, 0.38, 1)
        _DarkColor ("Color del centro hundido", Color) = (0.08, 0.09, 0.11, 1)
        _ShadeStrength ("Fuerza del sombreado", Range(0, 1)) = 0.5

        [Header(Anillos de presion)]
        _LineColor ("Color de los anillos y aristas", Color) = (0.62, 0.7, 0.78, 1)
        _Rings ("Cantidad de anillos", Range(1, 12)) = 5
        _Speed ("Velocidad (negativo = hacia afuera)", Range(-3, 3)) = 0.6
        _LineIntensity ("Intensidad de los anillos", Range(0, 3)) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Compressed"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DarkColor;
                half _ShadeStrength;
                half4 _LineColor;
                float _Rings;
                float _Speed;
                half _LineIntensity;
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
                float3 positionOS : TEXCOORD0;
                float3 normalOS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
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
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS = IN.normalOS;
                OUT.normalWS = n.normalWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Coordenadas dentro de la cara actual del cubo (la cara se elige por su normal)
                float3 a = abs(IN.normalOS);
                float2 uv = (a.x > a.y && a.x > a.z) ? IN.positionOS.yz
                          : (a.y > a.z ? IN.positionOS.xz : IN.positionOS.xy);

                // d = 0 en el centro de la cara, 1 en el borde (distancia en cuadrado)
                float d = saturate(max(abs(uv.x), abs(uv.y)) * 2.0);

                // Centro oscuro y bordes más claros: la cara parece hundida hacia adentro
                half3 col = lerp(_DarkColor.rgb, _BaseColor.rgb, smoothstep(0.0, 1.0, d));

                half3 n = normalize(IN.normalWS);
                half3 lightDir = normalize(half3(0.4, 0.8, 0.3));
                half ndl = dot(n, lightDir) * 0.5 + 0.5;
                col *= lerp(1.0 - _ShadeStrength, 1.0, ndl);

                // Anillos de presión que se desplazan hacia el centro (como si el cubo se apretara)
                float rp = frac(d * _Rings + _Time.y * _Speed);
                float ring = smoothstep(0.0, 0.1, rp) * (1.0 - smoothstep(0.1, 0.2, rp));
                ring *= smoothstep(0.1, 0.5, d);
                col += _LineColor.rgb * ring * _LineIntensity * 0.5;

                // Aristas marcadas
                col = lerp(col, _LineColor.rgb, smoothstep(0.88, 0.98, d) * 0.6);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
