Shader "Custom/VaporwaveGradient"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.0, 0.9, 1.0, 1)
        _BottomColor ("Bottom Color", Color) = (1.0, 0.2, 0.6, 1)
        _EmissionIntensity ("Emission Intensity", Range(0.5, 5)) = 1.5
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

            half4 _TopColor;
            half4 _BottomColor;
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
                float t = input.uv.y;
                // S-curve for smoother gradient
                t = t * t * (3.0 - 2.0 * t);

                half3 color = lerp(_BottomColor.rgb, _TopColor.rgb, t);
                color *= _EmissionIntensity;

                return half4(color, 1.0);
            }
            ENDCG
        }
    }
}
