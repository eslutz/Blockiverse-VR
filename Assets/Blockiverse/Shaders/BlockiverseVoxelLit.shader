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
        //
        // WATER FAMILIES SHARE ONE WAVE. Freshwater and brine are both water, and Eric's ruling
        // (2026-08-24) is that they may look different but must MOVE the same: two bodies meeting
        // mid-surface with different frequency and speed read as a seam, because the eye tracks
        // motion far more readily than tint. Brine previously ran at its own amplitude, frequency
        // and speed (0.020 / 1.05 / 0.90), which also left its mean surface 5 mm proud of
        // freshwater — the levelling bias in BlockiverseVoxelLitInput.hlsl was added to hide that,
        // and with identical amplitudes it is now a no-op between these two. It stays, because it
        // is what keeps ANY future family from stepping against the others.
        //
        // Emberflow deliberately does NOT join them. It is molten rock, not water; barely moving
        // is the point, and it never shares a shoreline with a lake in a way that would show a
        // seam. Per-family TINT (above) is untouched — the look was never the complaint.
        _WaveFreshwater("Freshwater Wave", Vector) = (0.025, 0.90, 1.10, 8.0)
        _WaveBrine("Brine Wave", Vector) = (0.025, 0.90, 1.10, 8.0)
        _WaveEmberflow("Emberflow Wave", Vector) = (0.012, 0.45, 0.22, 3.0)
        _WaterSpecularStrength("Water Specular Strength", Range(0, 2)) = 0.35

        // Material-driven surface state (the URP Lit pattern). Defaults are OPAQUE, so terrain
        // is unaffected; BlockVisualAtlas.CreateFluidMaterial overrides them for water.
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _Cull("__cull", Float) = 2.0

        // Alpha-cutout threshold, read only when _BLOCKIVERSE_CUTOUT is enabled by
        // BlockVisualAtlas.CreateCutoutMaterial. Terrain never enables the keyword.
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
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
            // ON for water so a FARTHER fluid fragment can never paint over a nearer one -- the
            // sort flip that reads as a far bank sliding on top of the near surface, both between
            // chunks as the head turns and inside one fluid mesh, where a top face and a far wall
            // are submitted in voxel-traversal order rather than depth order.
            //
            // It does NOT make the blending order-independent. When the farther fragment happens
            // to be submitted first it blends and writes depth, and the nearer one then blends
            // over it, so that patch carries two layers of tint instead of one and reads slightly
            // denser; submitted the other way round the far fragment is depth-rejected and only
            // one layer lands. Triangle order is fixed by the voxel scan, so which of the two you
            // get depends on where you are standing. Removing that residue needs a depth-primed
            // second pass over fluid geometry (ColorMask 0 + ZWrite, then ZTest Equal with ZWrite
            // off) or per-frame sorting; both cost an extra pass, so they wait on the device
            // capture that can actually price them.
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
            //
            // Water and cutout are three states on ONE line, not two independent keywords: they
            // are mutually exclusive (no material is both), so this compiles 3 variants where two
            // separate multi_compile_local lines would compile 4.
            #pragma multi_compile_local _ _BLOCKIVERSE_WATER _BLOCKIVERSE_CUTOUT

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
                    float mask = input.fluidData.x;
                    float4 wave = BlockiverseSelectFluidWave(input.fluidData.y);

                    // Displaced through the shared helper, byte for byte the same call the depth
                    // prime pass makes, so the depth this pass is tested against is the depth that
                    // pass wrote.
                    float3 positionWS = positionInputs.positionWS;
                    float3 waveNormal;
                    BlockiverseApplyFluidWave(mask, wave, positionWS, waveNormal);

                    positionInputs.positionWS = positionWS;
                    positionInputs.positionCS = TransformWorldToHClip(positionWS);

                    // Gated on the baked normal as well as the mask: a masked vertex is not always
                    // part of a surface. The foot of a side wall standing on a lower surface is
                    // masked so it MOVES with that surface, but it is still part of a vertical
                    // face, and handing it an upward wave normal would shade the bottom of the
                    // wall as though it were lying flat.
                    float upFacing = step(0.5, normalWS.y);
                    float surfaceMask = mask * upFacing;
                    normalWS = normalize(lerp(normalWS, waveNormal, surfaceMask));

                    output.waterTint = BlockiverseSelectFluidTint(input.fluidData.y);
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

            // One occlusion term per light, never two. A light that owns a shadow slice resolves
            // occlusion from its own cube map -- 1024 atlas, six slices on a 4x4 grid, so 256 px
            // per cube face, about 4 cm at five metres. The baked emitterReach gate resolves it at
            // one metre, because VoxelLightSampler.SampleEmitterReach returns 0 or 1 and
            // ChunkMeshBuilder samples it once per face. Multiplying the two lets the 25x coarser
            // term zero the finer one wherever it is 0, which is what put a block-aligned step
            // through a real shadow.
            half PunctualOcclusion(uint loopIndex, half emitterReach, float3 positionWS, half3 lightDirection)
            {
                // The same mapping GetAdditionalLight performs internally (RealtimeLights.hlsl):
                // the cluster iterator yields real light indices, the UBO path a loop counter.
                #if USE_CLUSTER_LIGHT_LOOP
                    int lightIndex = loopIndex;
                #else
                    int lightIndex = GetPerObjectLightIndex(loopIndex);
                #endif

                // .w is the light's first shadow slice index, -1 when it has no shadow map. It is
                // also -1 for EVERY light when ADDITIONAL_LIGHT_CALCULATE_SHADOWS is undefined, so
                // a player whose shadow keyword got stripped by the build preprocessor falls back
                // to the baked gate everywhere -- exactly today's behaviour, never light through
                // walls. That is the safe direction for the m_PrefilteringMode trap to fail in.
                int shadowSliceIndex = GetAdditionalLightShadowParams(lightIndex).w;

                UNITY_BRANCH
                if (shadowSliceIndex < 0)
                    return emitterReach;

                // The RAW shadow sample, deliberately not Light.shadowAttenuation. URP has already
                // mixed the fade into that one (MixRealtimeAndBakedShadows -> lerp(raw, 1, fade)
                // with no lightmaps here), so a fully shadowed texel reads back as `fade` rather
                // than 0. Crossfading two envelopes that have both been lifted by the same fade
                // reopens pixels that BOTH terms call occluded -- min(fade, 1 - fade) peaks at 0.5
                // mid-band, letting half the punctual light through a wall. Fetching the light
                // without the shadowMask overload leaves shadowAttenuation at 1, so this is the
                // only shadow sample taken, not a second one.
                half raw = AdditionalLightRealtimeShadow(lightIndex, positionWS, lightDirection);

                // Hand occlusion from the cube map to the bake exactly as URP retires the shadow:
                // 0 fade is the map alone, 1 is the bake alone, and a pixel both agree is occluded
                // stays occluded the whole way across.
                half fade = GetAdditionalLightShadowFade(positionWS);
                return lerp(raw, emitterReach, fade);
            }

            half3 AccumulatePunctual(Light light, half3 normalWS, half occlusion)
            {
                half nDotL = saturate(dot(normalWS, light.direction));
                return light.color * (nDotL * light.distanceAttenuation * occlusion);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Without this the fragment stage's unity_StereoEyeIndex stays 0, so the right eye
                // would sample the LEFT eye's light clusters.
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                // Foliage cutout. Clip before any lighting work so discarded fragments cost as
                // little as possible; on a tile GPU the alpha test has already disabled early-Z
                // for this draw, so shading a fragment we are about to throw away is pure waste.
                BlockiverseClipFoliage(sampled.a);

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);

                // Vertex colour carries the baked per-face light data from VoxelLightSampler:
                //   R = sky exposure (1 open sky .. 0 fully enclosed) — gates sun/moon/ambient so
                //       a cave stays dark at noon and a sealed room is dark;
                //   G = emitter reach — 1 only if some emitter in range has clear line of sight
                //       to this face. It is the occluder for punctual lights that own no shadow
                //       map (URP punctual attenuation is otherwise pure inverse-square with no
                //       occlusion at all); see PunctualOcclusion for which lights it applies to;
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
                            // No shadowMask overload: PunctualOcclusion takes the raw shadow sample
                            // itself, so letting URP also compute a fade-mixed one would be both a
                            // second cube-map fetch and the wrong value.
                            Light light = GetAdditionalLight(lightIndex, input.positionWS);
                            additional += AccumulatePunctual(light, normalWS,
                                PunctualOcclusion(lightIndex, emitterReach, input.positionWS, light.direction));
                        }
                    #endif

                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS);
                        additional += AccumulatePunctual(light, normalWS,
                            PunctualOcclusion(lightIndex, emitterReach, input.positionWS, light.direction));
                    LIGHT_LOOP_END
                #elif defined(_ADDITIONAL_LIGHTS_VERTEX)
                    // Interpolated from the vertex stage. Per-vertex lighting gives a coarse 1 m
                    // grid gradient on voxel faces and barely lights a torch alcove, which is why
                    // ConfigureQuestUrpShadowPolicy ships Per Pixel instead.
                    // Gated here rather than after the branch: vertex lighting carries no per-light
                    // shadow information, so the baked gate is the only occluder available to it,
                    // and the per-pixel path above no longer uses a shared post-loop multiply.
                    additional += input.vertexLighting * emitterReach;
                #endif

                // URP's punctual attenuation is rcp(distanceSqr) with no near clamp, and an emitter
                // sits only ~0.5-0.9 m from the faces of its own block, so raw attenuation reaches
                // 1.2-4.0. The URP asset has HDR off, so without this every surface within about a
                // metre of a glowwick would saturate to flat white. Reinhard keeps the canonical
                // brightness ladder intact while asymptotically approaching 1.
                // Occlusion is resolved per light inside the loop above -- each light gets its own
                // shadow map or the baked gate, never both -- so this is scale and range
                // compression only.
                additional *= half(_AdditionalLightScale);
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

        // Depth prime for transparent water. Water is drawn as two materials on the one fluid
        // renderer: this pass first, from a material at a lower render queue, then the shading
        // pass. It writes ONLY depth, so the nearest water fragment claims each pixel and every
        // farther one -- a far wall seen through a near surface, another chunk's surface behind
        // this one -- is depth-rejected before it can blend.
        //
        // Why it exists: ZWrite alone does not make alpha blending order-independent. It decides
        // which fragments survive, not how many blend, so submission order (fixed by the voxel
        // scan, not by the camera) decided whether a patch of water carried one layer of tint or
        // two, and that changed as the player walked around a lake. Priming depth first pins it
        // at exactly one layer from every angle.
        //
        // The ordering is guaranteed by the two materials' render queues, not by URP's shader-tag
        // order, so it does not depend on an internal detail of the pipeline. Terrain and the
        // water shading material both disable this pass outright, so nothing but the prime
        // material ever runs it.
        Pass
        {
            Name "WaterDepthPrime"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull [_Cull]
            // Pushes the primed depth a hair away from the camera so the shading pass's own
            // fragment reliably passes ZTest LEqual. The two passes compute the same displaced
            // position through the same helper, but they are separately compiled programs and an
            // exact-equality depth test would be at the mercy of the compiler's float scheduling.
            // One depth unit is far below the metre-scale separation between water layers.
            Offset 1, 1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterPrimeVert
            #pragma fragment WaterPrimeFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BlockiverseVoxelLitInput.hlsl"

            struct WaterPrimeAttributes
            {
                float4 positionOS : POSITION;
                float2 fluidData : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct WaterPrimeVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            WaterPrimeVaryings WaterPrimeVert(WaterPrimeAttributes input)
            {
                WaterPrimeVaryings output = (WaterPrimeVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 waveNormal;
                BlockiverseApplyFluidWave(
                    input.fluidData.x, BlockiverseSelectFluidWave(input.fluidData.y), positionWS, waveNormal);

                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 WaterPrimeFrag(WaterPrimeVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
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
            // Material-driven, not hardcoded Back: cutout foliage renders two-sided, and a
            // single-sided shadow pass would drop the shadow of every back-facing cross quad.
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #pragma multi_compile_instancing
            // Directional and punctual shadow casters use different normal-bias formulas.
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            // Only the cutout state matters here — water casts no shadow — so this is 2 variants,
            // not the 3 the ForwardLit pass needs.
            #pragma multi_compile_local _ _BLOCKIVERSE_CUTOUT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "BlockiverseVoxelLitInput.hlsl"

            // Engine globals set by ShadowUtils.SetupShadowCasterConstantBuffer — deliberately
            // outside UnityPerMaterial, exactly as URP's own ShadowCasterPass declares them.
            float3 _LightDirection;
            float3 _LightPosition;

        #if defined(_BLOCKIVERSE_CUTOUT)
            // Only the cutout variant samples anything; the opaque shadow variant stays exactly as
            // it was, with no texture fetch and no extra interpolator.
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
        #endif

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            #if defined(_BLOCKIVERSE_CUTOUT)
                float2 uv : TEXCOORD0;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // No UNITY_VERTEX_OUTPUT_STEREO here: shadow maps are rendered once, mono, and reused
            // for both eyes. URP's own ShadowCasterPass omits it too.
            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            #if defined(_BLOCKIVERSE_CUTOUT)
                float2 uv : TEXCOORD0;
            #endif
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
            #if defined(_BLOCKIVERSE_CUTOUT)
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            #endif
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
            #if defined(_BLOCKIVERSE_CUTOUT)
                // Clip with the same threshold ForwardLit uses, or a lacy canopy casts the shadow
                // of the solid cube it replaced — worse than the opaque leaves it is fixing.
                BlockiverseClipFoliage(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a);
            #endif
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
            // Material-driven for the same reason as ShadowCaster: two-sided cutout geometry.
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _BLOCKIVERSE_CUTOUT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BlockiverseVoxelLitInput.hlsl"

        #if defined(_BLOCKIVERSE_CUTOUT)
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
        #endif

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            #if defined(_BLOCKIVERSE_CUTOUT)
                float2 uv : TEXCOORD0;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            #if defined(_BLOCKIVERSE_CUTOUT)
                float2 uv : TEXCOORD0;
            #endif
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
            #if defined(_BLOCKIVERSE_CUTOUT)
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            #endif
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            #if defined(_BLOCKIVERSE_CUTOUT)
                BlockiverseClipFoliage(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a);
            #endif
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
