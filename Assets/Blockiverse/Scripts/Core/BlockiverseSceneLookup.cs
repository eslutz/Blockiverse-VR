using UnityEngine;

namespace Blockiverse.Core
{
    // Centralized fallback lookup for generated-scene references. Prefer explicit Configure(...)
    // wiring; use this only at runtime boundaries where generated objects may initialize in either
    // order and a serialized reference has not been supplied yet.
    public static class BlockiverseSceneLookup
    {
        public static T Find<T>() where T : Object =>
            Object.FindFirstObjectByType<T>();

        public static T Find<T>(FindObjectsInactive inactive) where T : Object =>
            Object.FindFirstObjectByType<T>(inactive);

        public static T[] FindAll<T>(FindObjectsSortMode sortMode) where T : Object =>
            Object.FindObjectsByType<T>(sortMode);

        public static T[] FindAll<T>(FindObjectsInactive inactive, FindObjectsSortMode sortMode) where T : Object =>
            Object.FindObjectsByType<T>(inactive, sortMode);

        public static GameObject FindGameObject(string name) => GameObject.Find(name);
    }
}
