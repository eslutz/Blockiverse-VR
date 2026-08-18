Shader "Custom/NeonBorder"
{
    Properties
    {
        _BorderColor ("Border Color", Color) = (0, 1, 1, 1)
        _BorderWidth ("Border Width", Range(0.01, 0.15)) = 0.06
        _EmissionIntensity ("Emission Intensity", Range(1, 10)) = 4
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

            half4 _BorderColor;
            half _BorderWidth;
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
                float2 uv = input.uv;
                float bw = _BorderWidth;
                float aa = fwidth(uv.x) + fwidth(uv.y);

                float distToEdge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float border = 1.0 - smoothstep(bw - aa, bw + aa, distToEdge);

                half3 baseColor = half3(0.02, 0.02, 0.03);
                half3 emissive = _BorderColor.rgb * _EmissionIntensity * border;

                return half4(baseColor + emissive, 1.0);
            }
            ENDCG
        }
    }
}
