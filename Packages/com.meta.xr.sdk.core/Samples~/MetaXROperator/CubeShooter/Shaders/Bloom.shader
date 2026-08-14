Shader "Hidden/Bloom"
{
    Properties
    {
        _MainTex ("", 2D) = "white" {}
        _Filter ("Filter", Vector) = (0,0,0,0)
        _Intensity ("Intensity", Float) = 1
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);
            half4 _Filter;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 c = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv);
                half brightness = max(c.r, max(c.g, c.b));
                half soft = brightness - _Filter.y;
                soft = clamp(soft, 0, _Filter.z);
                soft = soft * soft * _Filter.w;
                half contribution = max(soft, brightness - _Filter.x);
                contribution /= max(brightness, 0.001);
                return c * contribution;
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);
            float4 _MainTex_TexelSize;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 ts = _MainTex_TexelSize.xy;
                half4 c = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + float2(-1, -1) * ts);
                c += UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + float2( 1, -1) * ts);
                c += UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + float2(-1,  1) * ts);
                c += UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + float2( 1,  1) * ts);
                return c * 0.25;
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);
            UNITY_DECLARE_SCREENSPACE_TEXTURE(_BloomTex);

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 bloom = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_BloomTex, i.uv);
                half4 current = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv);
                return current + bloom;
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);
            UNITY_DECLARE_SCREENSPACE_TEXTURE(_BloomTex);
            half _Intensity;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 source = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv);
                half4 bloom = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_BloomTex, i.uv);
                return source + bloom * _Intensity;
            }
            ENDCG
        }
    }
}
