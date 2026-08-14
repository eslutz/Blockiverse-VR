Shader "Skybox/VaporwaveGrid"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.02, 0.0, 0.08, 1)
        _GlowColor ("Glow Color", Color) = (0.7, 0.04, 0.4, 1)
        _GlowFalloff ("Glow Falloff", Range(1, 30)) = 6.0
        _BottomColor ("Bottom Color", Color) = (0.02, 0.0, 0.04, 1)
        _GridColor ("Grid Color", Color) = (1.0, 0.1, 0.7, 1)
        _GridScale ("Grid Scale", Float) = 6.0
        _GridSpeed ("Grid Speed", Float) = 0.5
        _GridLineWidth ("Grid Line Width", Range(0.01, 0.15)) = 0.06
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _TopColor;
            half4 _GlowColor;
            float _GlowFalloff;
            half4 _BottomColor;
            half4 _GridColor;
            float _GridScale;
            float _GridSpeed;
            float _GridLineWidth;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.texcoord;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 dir = normalize(i.dir);
                float y = dir.y;

                // Sky: dark top with pink glow rising from the horizon
                half4 skyColor;
                if (y > 0.0)
                {
                    // Glow fades out exponentially going up from horizon
                    float glowT = exp(-y * _GlowFalloff);
                    skyColor = lerp(_TopColor, _GlowColor, glowT);
                }
                else
                {
                    // Below horizon: quickly go dark so grid pops
                    float t = saturate(-y * 4.0);
                    skyColor = lerp(_GlowColor, _BottomColor, t);
                }

                // Grid on the lower hemisphere (ground plane projection)
                half gridIntensity = 0.0;

                if (y < -0.001)
                {
                    // Ray-plane intersection at y = -1
                    float hitT = -1.0 / y;
                    float2 planeUV = dir.xz * hitT;

                    // Animate: scroll grid toward viewer
                    planeUV.y += _Time.y * _GridSpeed;

                    // Scale
                    planeUV *= _GridScale;

                    // Screen-space derivatives for anti-aliasing
                    float2 fw = fwidth(planeUV);

                    // Anti-aliased grid lines:
                    // Use fwidth to set the smoothstep transition width per-pixel
                    // This ensures edges are always smooth regardless of distance
                    float2 halfLineW = float2(_GridLineWidth, _GridLineWidth) * 0.5;
                    float2 wrapped = abs(frac(planeUV) - 0.5);
                    float lineX = 1.0 - smoothstep(halfLineW.x - fw.x, halfLineW.x + fw.x, wrapped.x);
                    float lineY = 1.0 - smoothstep(halfLineW.y - fw.y, halfLineW.y + fw.y, wrapped.y);
                    half gridVal = saturate(lineX + lineY);

                    // Moire suppression: when grid cells approach sub-pixel size,
                    // crossfade to average grid coverage to prevent shimmer.
                    float maxFreq = max(fw.x, fw.y);
                    half moireFade = 1.0 - smoothstep(0.5, 1.2, maxFreq);
                    half avgCoverage = saturate(_GridLineWidth * 2.0);
                    gridVal = lerp(avgCoverage, gridVal, moireFade);

                    // Fade when looking straight down, keep bright near horizon
                    half angleFade = 1.0 - smoothstep(0.3, 1.0, -y);

                    gridIntensity = gridVal * angleFade;
                }

                // Composite
                half4 finalColor = lerp(skyColor, _GridColor, gridIntensity);
                return finalColor;
            }
            ENDCG
        }
    }
}
