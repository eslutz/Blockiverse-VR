// The sky the day/night cycle actually owns.
//
// The project shipped Unity's stock Skybox/Procedural, whose only scene input is the direction of
// RenderSettings.sun. Blockiverse uses ONE directional light for both sun and moon and rotates it
// to come from overhead at night so the ground stays lit -- so that skybox saw a light above the
// horizon at midnight and dutifully drew a noon sky behind a night-lit world. It also had nowhere
// to put clouds, which is why every weather state changed the light and left the sky untouched.
//
// Everything here is driven from BlockiverseLightingCycleController, in the same LateUpdate pass
// that already owns the sun, the ambient and the fog.
Shader "Blockiverse/Sky"
{
    Properties
    {
        _ZenithColor("Zenith Color", Color) = (0.16, 0.33, 0.62, 1)
        _HorizonColor("Horizon Color", Color) = (0.62, 0.74, 0.88, 1)
        _GroundColor("Ground Color", Color) = (0.14, 0.15, 0.17, 1)
        _HorizonSharpness("Horizon Sharpness", Range(0.5, 8)) = 2.2

        _SunDirection("Sun Direction", Vector) = (0, 1, 0, 0)
        _SunColor("Sun Color", Color) = (1, 0.96, 0.86, 1)
        _SunSize("Sun Size", Range(0.9, 0.9999)) = 0.9975
        _SunGlow("Sun Glow", Range(0, 200)) = 40

        _CloudColor("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudCoverage("Cloud Coverage", Range(0, 1)) = 0
        _CloudScale("Cloud Scale", Range(0.2, 8)) = 2.2
        _CloudScroll("Cloud Scroll", Vector) = (0, 0, 0, 0)
        _CloudSoftness("Cloud Softness", Range(0.01, 0.6)) = 0.22
    }

    SubShader
    {
        // Background queue, depth-test off: standard skybox state. It shades only the pixels
        // opaque geometry did not already cover, so the cloud noise below is paid for sky only.
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _GroundColor;
                half _HorizonSharpness;
                float4 _SunDirection;
                half4 _SunColor;
                half _SunSize;
                half _SunGlow;
                half4 _CloudColor;
                half _CloudCoverage;
                half _CloudScale;
                float4 _CloudScroll;
                half _CloudSoftness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS = input.positionOS.xyz;
                return output;
            }

            // Cheap hash-based value noise. Generated rather than sampled from a texture on
            // purpose: a cloud texture would mean a new authored art asset plus its whole
            // validation tail, and this project generates everything else it draws.
            // 127.1 / 311.7 are the constants from the SINE hash idiom
            // frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453), where they live inside a dot
            // product before a sin. Dropped into a frac-multiply hash instead, only the FRACTIONAL
            // part of each multiplier survives -- and ValueNoise only ever calls this on integer
            // lattice points (i = floor(p)), so frac(n * 127.1) == frac(n * 0.1) and
            // frac(n * 311.7) == frac(n * 0.7). Both are near-exact tenths, so the field very
            // nearly repeated every 10 units on both axes, and `frac(p.x * p.y)` is symmetric in x
            // and y, which mirrored the tile across its diagonal on top of that.
            //
            // Measured in float32 over a 60x60 lattice: 715 distinct values out of 3600, with
            // 180 of 400 cells matching under a +10 shift. That is the tiled sky with repeating
            // motifs and diagonal seams. A correct hash gives 3600 of 3600.
            //
            // This is Dave Hoskins' hash12: three decorrelated irrational-ish multipliers, mixed
            // through a dot product so no single axis can dominate the result.
            float Hash2(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash2(i);
                float b = Hash2(i + float2(1, 0));
                float c = Hash2(i + float2(0, 1));
                float d = Hash2(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Two octaves. Enough to read as cloud rather than as a blob, cheap enough that the
            // sky stays a trivial fragment on a tile GPU.
            float CloudNoise(float2 p)
            {
                return ValueNoise(p) * 0.65 + ValueNoise(p * 2.17 + 41.3) * 0.35;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.directionWS);

                // Gradient. The sharpness term keeps the horizon a band rather than a linear
                // wash, which is what makes it read as atmosphere.
                half up = saturate(dir.y);
                half sky = pow(up, 1.0h / max(_HorizonSharpness, 0.001h));
                half3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, sky);

                // Below the horizon. Blended rather than hard-cut so the seam is not a visible
                // line when the player looks down off a cliff.
                half below = saturate(-dir.y * 6.0h);
                color = lerp(color, _GroundColor.rgb, below);

                // Sun or moon disk, plus a glow that keeps a low sun from looking pasted on.
                half alignment = saturate(dot(dir, normalize(_SunDirection.xyz)));
                half disk = smoothstep(_SunSize, 1.0h, alignment);
                half glow = pow(alignment, max(_SunGlow, 1.0h)) * 0.35h;
                color += _SunColor.rgb * (disk + glow);

                // Clouds, on the visible hemisphere only. Direction is projected onto a plane
                // overhead, so they converge toward the horizon the way a real cloud deck does
                // rather than sliding across the sky at a constant rate.
                UNITY_BRANCH
                if (_CloudCoverage > 0.001h)
                {
                    float horizon = max(dir.y, 0.08);
                    float2 planar = (dir.xz / horizon) * _CloudScale + _CloudScroll.xy;

                    float density = CloudNoise(planar);

                    // Coverage drives a THRESHOLD, not an opacity: at low coverage a few small
                    // clouds appear, and as it rises they grow and join up. Fading a full-sky
                    // sheet in and out instead would read as haze, not weather.
                    half threshold = 1.0h - _CloudCoverage;
                    half amount = smoothstep(threshold, threshold + _CloudSoftness, (half)density);

                    // Faded out near the horizon, where the projection stretches the noise into
                    // streaks and where terrain and fog would occlude a real deck anyway.
                    // Fade must reach ZERO by the projection clamp above (dir.y = 0.08), or the
                    // frozen smear at the clamp stays visible as a hard band of streaks along the
                    // horizon. saturate(dir.y * 4) is still 0.32 there, which is what drew the
                    // picket fence. Remap so the fade starts at the clamp and eases in.
                    half horizonFade = saturate((dir.y - 0.08h) * 5.0h);
                    amount *= horizonFade * horizonFade * (3.0h - 2.0h * horizonFade);

                    color = lerp(color, _CloudColor.rgb, amount * _CloudColor.a);
                }

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
