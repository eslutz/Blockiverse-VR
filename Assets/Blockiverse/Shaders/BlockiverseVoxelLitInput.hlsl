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

#endif
