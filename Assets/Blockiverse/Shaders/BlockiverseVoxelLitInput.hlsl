#ifndef BLOCKIVERSE_VOXEL_LIT_INPUT_INCLUDED
#define BLOCKIVERSE_VOXEL_LIT_INPUT_INCLUDED

// Shared material constant buffer. The SRP Batcher requires an IDENTICAL UnityPerMaterial
// layout in EVERY pass of the shader — if the blocks drift apart the whole shader falls out
// of the batch and each chunk costs a SetPass call on Quest. Keeping it in one include is what
// makes that impossible to get wrong.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseColor;
    float _BakedLightFloor;
    float _AdditionalLightScale;
    float _SelfEmissionStrength;

    // Water. Present in every variant because the SRP Batcher demands one layout, but read only
    // by the _BLOCKIVERSE_WATER pass -- the opaque terrain material leaves them at their defaults.
    float4 _TintFreshwater;
    float4 _TintBrine;
    float4 _TintEmberflow;
    // x = dip amplitude (blocks), y = spatial frequency (rad/block),
    // z = time speed (rad/s), w = normal amplification (visual slope multiplier)
    float4 _WaveFreshwater;
    float4 _WaveBrine;
    float4 _WaveEmberflow;
    float _WaterSpecularStrength;

    // Material-driven surface state, the URP Lit pattern. Defaults are opaque, so the terrain
    // material renders byte-identically to before water existed.
    float _SrcBlend;
    float _DstBlend;
    float _ZWrite;
    float _Cull;
CBUFFER_END

// Water look and wave selection live here, in the shared include, because the ForwardLit water
// variant and the depth-prime pass MUST compute the same displaced position. The prime exists to
// make water blending order-independent, and that only holds if the depth it writes matches the
// depth the shading pass tests against.
float4 BlockiverseSelectFluidWave(float familyIndex)
{
    float isEmberflow = step(1.5, familyIndex);
    float isBrine = step(0.5, familyIndex) - isEmberflow;
    float isFresh = 1.0 - isBrine - isEmberflow;

    return _WaveFreshwater * isFresh + _WaveBrine * isBrine + _WaveEmberflow * isEmberflow;
}

float4 BlockiverseSelectFluidTint(float familyIndex)
{
    float isEmberflow = step(1.5, familyIndex);
    float isBrine = step(0.5, familyIndex) - isEmberflow;
    float isFresh = 1.0 - isBrine - isEmberflow;

    return _TintFreshwater * isFresh + _TintBrine * isBrine + _TintEmberflow * isEmberflow;
}

// Displaces positionWS in place and returns the analytic surface normal for the displaced point.
// Chunk vertices are absolute voxel coordinates and the world root is pinned to identity, so a
// world-space wave is continuous across every chunk border with no per-chunk uniform.
// How far from the camera the wave keeps its full amplitude, and where it has faded out entirely.
// Ripples on a lake two hundred metres away carry no information -- from an elevated vantage they
// read as noise across the whole surface, and every one of them is a sin/cos pair plus a normal
// rebuild. Fading them out is mostly a LOOK fix; the honest performance note is that the vertex is
// transformed either way, so this saves shader work but no vertices. Cutting the vertices needs a
// fluid LOD, which is a different job.
//
// It also buys headroom for the near field: with the far field quiet, close-up amplitude can rise
// without the horizon turning into static.
#define BLOCKIVERSE_WAVE_FULL_DISTANCE 28.0
#define BLOCKIVERSE_WAVE_FADE_DISTANCE 90.0

void BlockiverseApplyFluidWave(float mask, float4 wave, inout float3 positionWS, out float3 waveNormal)
{
    float cameraDistance = distance(positionWS, GetCameraPositionWS());
    float distanceFade = 1.0 - smoothstep(
        BLOCKIVERSE_WAVE_FULL_DISTANCE, BLOCKIVERSE_WAVE_FADE_DISTANCE, cameraDistance);

    float amp = wave.x * mask * distanceFade;
    float k = wave.y;
    float t = _Time.y * wave.z;

    float sinX, cosX, sinZ, cosZ;
    sincos(positionWS.x * k + t, sinX, cosX);
    sincos(positionWS.z * k * 0.83 + t * 1.17, sinZ, cosZ);

    // Strictly non-positive: dy lands in [-2*amp, 0]. The surface only ever dips below the voxel
    // face plane, so a crest can never poke above the cell, and a wall either stays put or rides
    // the surface it stands on down by exactly the same amount -- the displacement is a pure
    // function of x and z.
    float s = 0.5 * (sinX + sinZ);
    positionWS.y += amp * (s - 1.0);

    // The CPU-baked flat normal cannot follow a GPU displacement, which would light the whole
    // animated surface as one rigid sliding sheet. Rebuild it from the wave derivative and amplify
    // by wave.w so the shading reads even though the geometry moves only a couple of centimetres.
    // Faded by the same term. Leaving the normal at full strength would keep a distant, now-flat
    // surface glinting as though it were still moving -- the shading is doing most of the visual
    // work here, so a mismatch would be more obvious than the geometry ever was.
    float slopeScale = amp * 0.5 * wave.w;
    waveNormal = normalize(float3(
        -slopeScale * k * cosX,
        1.0,
        -slopeScale * k * 0.83 * cosZ));
}

#endif
