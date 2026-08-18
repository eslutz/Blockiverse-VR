Shader "Custom/NeonGrid"
{
    Properties
    {
        _GridColor ("Grid Color", Color) = (1, 0, 1, 1)
        _GridSize ("Grid Size", Range(2, 10)) = 4
        _LineWidth ("Line Width", Range(0.02, 0.15)) = 0.06
        _EmissionIntensity ("Emission Intensity", Range(1, 10)) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            half4 _GridColor;
            half _GridSize;
            half _LineWidth;
            half _EmissionIntensity;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float gridSize = floor(_GridSize);
                float2 uv = input.uv * gridSize;
                float2 f = frac(uv);
                float2 grid = min(f, 1.0 - f);
                float2 aa = fwidth(uv);

                // Fade to average color at distance to suppress moire
                float cellSize = min(aa.x, aa.y);
                float distanceFade = saturate(cellSize * 2.0);

                float lineX = 1.0 - smoothstep(_LineWidth - aa.x, _LineWidth + aa.x, grid.x);
                float lineY = 1.0 - smoothstep(_LineWidth - aa.y, _LineWidth + aa.y, grid.y);
                float gridLine = saturate(lineX + lineY);

                // Blend toward average at distance
                float averageGrid = _LineWidth * 2.0;
                gridLine = lerp(gridLine, averageGrid, distanceFade);

                half3 baseColor = half3(0.02, 0.01, 0.04);
                half3 emissive = _GridColor.rgb * _EmissionIntensity * gridLine;

                return half4(baseColor + emissive, 1.0);
            }
            ENDCG
        }
    }
}
