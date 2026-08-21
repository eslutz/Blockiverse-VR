Shader "Blockiverse/Voxel Lit"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        // Minimum sky exposure. 0 = a sealed room or deep tunnel with no emitter is truly dark;
        // raise (e.g. 0.01) for a faint "eyes adjusted" floor if true black proves unplayable.
        _BakedLightFloor("Baked Light Floor", Range(0, 1)) = 0.0
        // Scales realtime punctual contribution so a cluster of glowwicks does not clip to white.
        _AdditionalLightScale("Additional Light Scale", Range(0, 2)) = 1.0
        // How brightly an emitter block renders its own faces regardless of surrounding light.
        _SelfEmissionStrength("Self Emission Strength", Range(0, 2)) = 1.0

        // Per-family water look, selected per vertex from the fluid mesh's UV1 channel.
        // Unused by the opaque terrain material.
        _TintFreshwater("Freshwater Tint", Color) = (0.72, 0.92, 1.00, 0.72)
        _TintBrine("Brine Tint", Color) = (0.68, 0.92, 0.90, 0.78)
        _TintEmberflow("Emberflow Tint", Color) = (1.00, 0.86, 0.72, 1.00)
        // x = dip amplitude (blocks), y = spatial frequency (rad/block),
        // z = time speed (rad/s), w = normal amplification (visual slope multiplier).
        // x must never exceed VoxelWorldRenderer.MaxWaveDipMeters * 0.5, or troughs fall outside
        // the padded mesh bounds and pop at the edge of vision.
        _WaveFreshwater("Freshwater Wave", Vector) = (0.025, 0.90, 1.10, 8.0)
        _WaveBrine("Brine Wave", Vector) = (0.020, 1.05, 0.90, 7.0)
        _WaveEmberflow("Emberflow Wave", Vector) = (0.012, 0.45, 0.22, 3.0)
        _WaterSpecularStrength("Water Specular Strength", Range(0, 2)) = 0.35

        // Material-driven surface state (the URP Lit pattern). Defaults are OPAQUE, so terrain
        // is unaffected; BlockVisualAtlas.CreateFluidMaterial overrides them for water.
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Opaque by default; the water material drives these to alpha blending. ZWrite stays
            // ON for water: it resolves water-over-water per pixel, which is the only thing that
            // fixes ordering INSIDE one fluid mesh (a top face and a far wall in one draw, in
            // arbitrary order) as well as per-chunk sort flips as the head turns.
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            // 3.5 guarantees the loop/indexing features the clustered light path relies on.
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            // URP 17.5 lighting keywords, names taken from URP's own Shaders/Lit.shader.
            // _MAIN_LIGHT_SHADOWS_SCREEN is deliberately omitted: it only applies with the
            // ScreenSpaceShadows renderer feature, and this project's renderer feature list is empty.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            // Soft shadows are a four-way keyword set in URP 17.5; declaring only _SHADOWS_SOFT
            // silently falls back to hard shadows whenever the asset picks Low/Medium/High.
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // _FORWARD_PLUS was deprecated in URP 6.1; Core.hlsl derives USE_CLUSTER_LIGHT_LOOP
            // from this keyword and the LIGHT_LOOP_* macros switch on it.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            // Quest-only fast paths that ship in URP 17.5. There are no spot lights in this game,
            // so the angle-attenuation branch is pure waste on device.
            #if defined(UNITY_PLATFORM_META_QUEST)
                #pragma multi_compile _ META_QUEST_LIGHTUNROLL
                #pragma multi_compile _ META_QUEST_NO_SPOTLIGHTS_LIGHT_LOOP
            #endif

            #pragma multi_compile_fog
            // Required for OpenXR single-pass instanced/multiview stereo to resolve the per-eye
            // matrices and, once additional lights land, the per-eye light cluster.
            #pragma multi_compile_instancing

            // multi_compile_local, NOT shader_feature_local: no material ASSET in the build
            // references this shader (BlockVisualAtlas swaps it in at runtime), so a
            // shader_feature variant would be stripped from the Android player and water would
            // render as opaque terrain on device while looking correct in the editor.
            #pragma multi_compile_local _ _BLOCKIVERSE_WATER

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "BlockiverseVoxelLitInput.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                #if defined(_BLOCKIVERSE_WATER)
                    // ChunkMeshBuilder.AddFluidFace: x = surface mask (1 on emitted +Y faces),
                    // y = FluidFamily index. Declared only in the water variant so terrain never
                    // pays the extra attribute fetch.
                    float2 fluidData : TEXCOORD1;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
                float fogCoord : TEXCOORD3;
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    half3 vertexLighting : TEXCOORD4;
                #endif
                #if defined(_BLOCKIVERSE_WATER)
                    float4 waterTint : TEXCOORD5;
                    float surfaceMask : TEXCOORD6;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                float3 normalWS = normalInputs.normalWS;

                #if defined(_BLOCKIVERSE_WATER)
                    float familyIndex = input.fluidData.y;
                    float isEmberflow = step(1.5, familyIndex);
                    float isBrine = step(0.5, familyIndex) - isEmberflow;
                    float isFresh = 1.0 - isBrine - isEmberflow;

                    float4 tint = _TintFreshwater * isFresh + _TintBrine * isBrine + _TintEmberflow * isEmberflow;
                    float4 wave = _WaveFreshwater * isFresh + _WaveBrine * isBrine + _WaveEmberflow * isEmberflow;

                    float mask = input.fluidData.x;
                    float amp = wave.x * mask;
                    float k = wave.y;
                    float t = _Time.y * wave.z;

                    // Chunk vertices are absolute voxel coordinates and the world root is pinned
                    // to identity, so a world-space wave is continuous across every chunk border
                    // with no per-chunk uniform. Writing it against positionWS (rather than
                    // object space) keeps that true even if the world root ever moves.
                    float3 positionWS = positionInputs.positionWS;
                    float sinX, cosX, sinZ, cosZ;
                    sincos(positionWS.x * k + t, sinX, cosX);
                    sincos(positionWS.z * k * 0.83 + t * 1.17, sinZ, cosZ);

                    // Strictly non-positive: dy lands in [-2*amp, 0]. The surface only ever dips
                    // below the voxel face plane, so a crest can never poke above the cell, and a
                    // wall either stays put or rides the surface it stands on down by exactly the
                    // same amount -- the displacement is a pure function of x and z.
                    float s = 0.5 * (sinX + sinZ);
                    positionWS.y += amp * (s - 1.0);

                    positionInputs.positionWS = positionWS;
                    positionInputs.positionCS = TransformWorldToHClip(positionWS);

                    // RecalculateNormals baked a flat (0,1,0) that a GPU displacement leaves
                    // stale, which would light the whole animated surface as one rigid sliding
                    // sheet. Rebuild it from the wave derivative and amplify by wave.w so the
                    // shading reads even though the geometry only moves a couple of centimetres.
                    float slopeScale = amp * 0.5 * wave.w;
                    float3 waveNormal = normalize(float3(
                        -slopeScale * k * cosX,
                        1.0,
                        -slopeScale * k * 0.83 * cosZ));

                    // Gated on the baked normal as well as the mask: a masked vertex is not always
                    // part of a surface. The foot of a side wall standing on a lower surface is
                    // masked so it MOVES with that surface, but it is still part of a vertical
                    // face, and handing it an upward wave normal would shade the bottom of the
                    // wall as though it were lying flat.
                    float upFacing = step(0.5, normalWS.y);
                    float surfaceMask = mask * upFacing;
                    normalWS = normalize(lerp(normalWS, waveNormal, surfaceMask));

                    output.waterTint = tint;
                    // The gated mask, not the raw one: downstream this means "on an animated
                    // horizontal surface", which is what the highlight wants. A wall foot moves
                    // but must not glint.
                    output.surfaceMask = surfaceMask;
                #endif

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogCoord = ComputeFogFactor(positionInputs.positionCS.z);

                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    // VertexLighting must run in the vertex stage — that is the whole point of the
                    // per-vertex path. The project ships Per Pixel, so this variant normally gets
                    // stripped; it exists so the shader still behaves if the pipeline is switched.
                    output.vertexLighting = VertexLighting(positionInputs.positionWS, output.normalWS);
                #endif

                return output;
            }

            half3 AccumulatePunctual(Light light, half3 normalWS)
            {
                half nDotL = saturate(dot(normalWS, light.direction));
                return light.color * (nDotL * light.distanceAttenuation * light.shadowAttenuation);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Without this the fragment stage's unity_StereoEyeIndex stays 0, so the right eye
                // would sample the LEFT eye's light clusters.
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);

                // Vertex colour carries the baked per-face light data from VoxelLightSampler:
                //   R = sky exposure (1 open sky .. 0 fully enclosed) — gates sun/moon/ambient so
                //       a cave stays dark at noon and a sealed room is dark;
                //   G = emitter reach — 1 only if some emitter in range has clear line of sight
                //       to this face, so realtime point lights cannot shine through walls or
                //       the ground (that is otherwise pure inverse-square with no occlusion);
                //   B = the block's own emissive level, so a torch stays visible in the dark.
                half bakedSky = max(input.color.r, half(_BakedLightFloor));
                half emitterReach = input.color.g;
                half selfEmission = input.color.b;

                // The clustered LIGHT_LOOP_BEGIN macro expands to code referencing a local named
                // exactly `inputData`, so it must exist with these two fields populated.
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // No lightmaps or shadowmask in this project: fully unoccluded probe mask.
                half4 shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);

                float4 shadowCoord = float4(0, 0, 0, 0);
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif

                Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);

                // Sky-gated terms: ambient probe plus whichever of sun/moon is above the horizon.
                half3 lighting = SampleSH(normalWS) * bakedSky;
                lighting += mainLight.color *
                    (saturate(dot(normalWS, mainLight.direction)) *
                     mainLight.distanceAttenuation * mainLight.shadowAttenuation * bakedSky);

                // Realtime punctual lights (glowwick, lumen lamp, campfire, spark flare, emberflow).
                // Deliberately NOT sky-gated: an enclosed room lit by a torch must read as lit.
                half3 additional = half3(0.0h, 0.0h, 0.0h);

                #if defined(_ADDITIONAL_LIGHTS)
                    #if USE_CLUSTER_LIGHT_LOOP
                        // Extra DIRECTIONAL lights live before the cluster structure and are never
                        // visited by the cluster iterator, so they need their own pre-loop.
                        // [loop] and the name `lightIndex` both matter: the bound is non-literal,
                        // and CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK expands to code that
                        // hard-references a variable called lightIndex.
                        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                        {
                            additional += AccumulatePunctual(
                                GetAdditionalLight(lightIndex, input.positionWS, shadowMask), normalWS);
                        }
                    #endif

                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        additional += AccumulatePunctual(
                            GetAdditionalLight(lightIndex, input.positionWS, shadowMask), normalWS);
                    LIGHT_LOOP_END
                #elif defined(_ADDITIONAL_LIGHTS_VERTEX)
                    // Interpolated from the vertex stage. Per-vertex lighting gives a coarse 1 m
                    // grid gradient on voxel faces and barely lights a torch alcove, which is why
                    // ConfigureQuestUrpShadowPolicy ships Per Pixel instead.
                    additional += input.vertexLighting;
                #endif

                // URP's punctual attenuation is rcp(distanceSqr) with no near clamp, and an emitter
                // sits only ~0.5-0.9 m from the faces of its own block, so raw attenuation reaches
                // 1.2-4.0. The URP asset has HDR off, so without this every surface within about a
                // metre of a glowwick would saturate to flat white. Reinhard keeps the canonical
                // brightness ladder intact while asymptotically approaching 1.
                // Baked line-of-sight gate first: behind a wall or under the ground this is 0 and
                // no amount of realtime intensity gets through.
                additional *= emitterReach * half(_AdditionalLightScale);
                additional = additional / (half3(1.0h, 1.0h, 1.0h) + additional);

                lighting += additional;

                // An emitter block lights its own faces. Without this a lone glowwick in a sealed
                // cave would render black inside the pool of light it casts.
                lighting += selfEmission * half(_SelfEmissionStrength);

                #if defined(_BLOCKIVERSE_WATER)
                    // Water keeps the full terrain lighting solve above -- sky gating, the sun or
                    // moon, clustered punctual lights and self emission -- because a torch-lit
                    // pool and a glowing emberflow are exactly as load-bearing as a lit wall.
                    // Only the surface treatment differs.
                    half3 color = sampled.rgb * input.waterTint.rgb * lighting;

                    // The highlight comes off the wave normal itself, so it is band-limited by the
                    // one-metre vertex spacing. An independent high-frequency sparkle sine would
                    // alias past ~20 m -- and alias DIFFERENTLY in each eye, which is binocular
                    // rivalry: invisible in the flat game view, a comfort defect in the headset.
                    half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                    half3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                    half spec = pow(saturate(dot(normalWS, halfDir)), 32.0) * half(input.surfaceMask);
                    // Gated by the same shadow and sky terms as the diffuse sun above. Without the
                    // shadow attenuation the glint burns straight through a cliff's or a bridge's
                    // shadow, which is the one place the water is obviously not in sunlight.
                    color += mainLight.color *
                        (spec * half(_WaterSpecularStrength) * bakedSky * mainLight.shadowAttenuation);

                    color = MixFog(color, input.fogCoord);
                    return half4(color, input.waterTint.a * sampled.a);
                #else
                    half3 color = sampled.rgb * lighting;
                    color = MixFog(color, input.fogCoord);
                    return half4(color, sampled.a);
                #endif
            }
            ENDHLSL
        }

        // Lets voxel terrain CAST shadows. Without this pass chunk meshes are invisible to every
        // shadow map, so nothing the player builds ever casts a shadow.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #pragma multi_compile_instancing
            // Directional and punctual shadow casters use different normal-bias formulas.
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "BlockiverseVoxelLitInput.hlsl"

            // Engine globals set by ShadowUtils.SetupShadowCasterConstantBuffer — deliberately
            // outside UnityPerMaterial, exactly as URP's own ShadowCasterPass declares them.
            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // No UNITY_VERTEX_OUTPUT_STEREO here: shadow maps are rendered once, mono, and reused
            // for both eyes. URP's own ShadowCasterPass omits it too.
            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 GetShadowPositionHClip(ShadowAttributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                return ApplyShadowClamping(positionCS);
            }

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        // Keeps voxel geometry present for depth-prepass / copy-depth paths.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BlockiverseVoxelLitInput.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
