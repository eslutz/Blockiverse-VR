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
CBUFFER_END

#endif
