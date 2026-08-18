using UnityEngine;
using Unity.Profiling;
using Blockiverse.WorldGen;

namespace Blockiverse.Gameplay
{
    internal static class BlockiverseWorldGenProfilingBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            Blockiverse.WorldGen.ProfilerMarker.BeginMarkerCallback = (name) =>
            {
                var marker = new Unity.Profiling.ProfilerMarker(name);
                return marker.Auto();
            };
        }
    }
}
