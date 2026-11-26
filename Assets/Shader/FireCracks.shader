// Cubo de piedra oscura con grietas de fuego incandescentes (cubo de fallo de temperatura).
// Las grietas salen de un ruido Voronoi 3D calculado en el espacio del objeto (sin texturas),
// así se adaptan a cualquier tamaño de cubo. URP, con las macros de instancing estéreo para Quest.
Shader "Molecula/FireCracks"
{
    Properties
    {
        _BaseColor ("Color de la piedra", Color) = (0.1, 0.08, 0.07, 1)
        _ShadeStrength ("Fuerza del sombreado", Range(0, 1)) = 0.5

        [Header(Grietas)]
        _CrackColorOuter ("Fuego (borde)", Color) = (1, 0.27, 0, 1)
        _CrackColorInner ("Fuego (centro)", Color) = (1, 0.83, 0.3, 1)
        _CrackScale ("Cantidad de grietas", Range(1, 10)) = 3.5
        _CrackWidth ("Grosor de las grietas", Range(0.02, 0.4)) = 0.12
        _GlowIntensity ("Intensidad del brillo", Range(0, 5)) = 2

        [Header(Pulso)]
        _PulseSpeed ("Velocidad del pulso", Range(0, 10)) = 2.5
        _PulseAmount ("Cuánto pulsa", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "FireCracks"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _ShadeStrength;
                half4 _CrackColorOuter;
                half4 _CrackColorInner;
                float _CrackScale;
                float _CrackWidth;
                half _GlowIntensity;
                float _PulseSpeed;
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
                float3 positionOS : TEXCOORD0;
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
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS = n.normalWS;
                return OUT;
            }

            float3 hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453123);
            }

            // Voronoi 3D: devuelve la distancia al punto más cercano (x) y al segundo más cercano (y)
            float2 voronoi(float3 x)
            {
                float3 p = floor(x);
                float3 f = frac(x);
                float f1 = 8.0;
                float f2 = 8.0;

                [unroll] for (int k = -1; k <= 1; k++)
                [unroll] for (int j = -1; j <= 1; j++)
                [unroll] for (int i = -1; i <= 1; i++)
                {
                    float3 g = float3(i, j, k);
                    float3 r = g + hash3(p + g) - f;
                    float d = dot(r, r);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) { f2 = d; }
                }
                return float2(sqrt(f1), sqrt(f2));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Grietas = donde las dos celdas más cercanas casi empatan
                float2 v = voronoi(IN.positionOS * _CrackScale);
                float edge = v.y - v.x;
                float crack = 1.0 - smoothstep(0.0, _CrackWidth, edge);
                float halo = 1.0 - smoothstep(0.0, _CrackWidth * 3.0, edge);

                // Piedra: cada placa un poco más clara u oscura, con sombreado suave
                half3 n = normalize(IN.normalWS);
                half3 lightDir = normalize(half3(0.4, 0.8, 0.3));
                half ndl = dot(n, lightDir) * 0.5 + 0.5;
                half3 stone = _BaseColor.rgb * lerp(0.7, 1.3, saturate(v.x));
                stone *= lerp(1.0 - _ShadeStrength, 1.0, ndl);

                // El fuego late de forma distinta según la zona del cubo
                float flicker = sin(_Time.y * _PulseSpeed + dot(IN.positionOS, float3(11.0, 7.0, 13.0)));
                half pulse = lerp(1.0 - _PulseAmount, 1.0, 0.5 + 0.5 * flicker);

                half3 fire = lerp(_CrackColorOuter.rgb, _CrackColorInner.rgb, crack * crack);

                half3 col = stone;
                col += _CrackColorOuter.rgb * halo * 0.25 * pulse;                 // resplandor alrededor de las grietas
                col = lerp(col, fire * _GlowIntensity * pulse, crack);             // la grieta en sí
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
