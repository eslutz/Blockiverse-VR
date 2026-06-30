using UnityEngine;
using Blockiverse.Networking;

namespace Blockiverse.Gameplay
{
    public static class BlockiverseLightingRuntime
    {
        public const string SunObjectName = "Blockiverse Sun";

        public static BlockiverseLightingCycleController EnsureSceneLighting()
        {
            GameObject sunObject = GameObject.Find(SunObjectName);

            if (sunObject == null)
                sunObject = new GameObject(SunObjectName);

            sunObject.name = SunObjectName;

            Light sun = sunObject.GetComponent<Light>();
            if (sun == null)
                sun = sunObject.AddComponent<Light>();

            WorldTimeClock clock = sunObject.GetComponent<WorldTimeClock>();
            if (clock == null)
                clock = sunObject.AddComponent<WorldTimeClock>();

            BlockiverseLightingCycleController controller = sunObject.GetComponent<BlockiverseLightingCycleController>();
            if (controller == null)
                controller = sunObject.AddComponent<BlockiverseLightingCycleController>();

            controller.Configure(clock, sun);
            return controller;
        }
    }
}
