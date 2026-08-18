Shader "Custom/JazzCup"
{
    Properties
    {
        _TealColor ("Teal Color", Color) = (0.0, 0.75, 0.7, 1)
        _PurpleColor ("Purple Color", Color) = (0.4, 0.1, 0.6, 1)
        _WaveScale ("Wave Scale", Range(0.5, 4)) = 1.5
        _WaveOffset ("Wave Offset", Range(0, 1)) = 0.3
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

            half4 _TealColor;
            half4 _PurpleColor;
            half _WaveScale;
            half _WaveOffset;

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
                float2 uv = input.uv;
                float aa = fwidth(uv.y) * 2.0;

                // White/light gray base
                half3 base = half3(0.95, 0.95, 0.97);

                // Teal wave: main swooping curve
                float wave1Center = 0.45 + sin(uv.x * 3.14159 * _WaveScale) * 0.15;
                float wave1 = smoothstep(wave1Center - 0.08 - aa, wave1Center - 0.08, uv.y)
                            * (1.0 - smoothstep(wave1Center + 0.08, wave1Center + 0.08 + aa, uv.y));

                // Thin teal accent line above
                float wave1b_center = wave1Center + 0.14;
                float wave1b = smoothstep(wave1b_center - 0.02 - aa, wave1b_center - 0.02, uv.y)
                             * (1.0 - smoothstep(wave1b_center + 0.02, wave1b_center + 0.02 + aa, uv.y));

                // Purple wave: offset and slightly different frequency
                float wave2Center = 0.38 + sin(uv.x * 3.14159 * _WaveScale + _WaveOffset * 6.28) * 0.18;
                float wave2 = smoothstep(wave2Center - 0.05 - aa, wave2Center - 0.05, uv.y)
                            * (1.0 - smoothstep(wave2Center + 0.05, wave2Center + 0.05 + aa, uv.y));

                half3 color = base;
                color = lerp(color, _TealColor.rgb, wave1 * 0.9);
                color = lerp(color, _TealColor.rgb * 0.8, wave1b * 0.85);
                color = lerp(color, _PurpleColor.rgb, wave2 * 0.8);

                return half4(color, 1.0);
            }
            ENDCG
        }
    }
}
