#pragma warning disable 0618
using UnityEngine;

namespace Blockiverse.Core
{
    // Centralized fallback lookup for generated-scene references. Prefer explicit Configure(...)
    // wiring; use this only at runtime boundaries where generated objects may initialize in either
    // order and a serialized reference has not been supplied yet.
    public static class BlockiverseSceneLookup
    {
        public static T Find<T>() where T : Object =>
            Object.FindAnyObjectByType<T>();

        public static T Find<T>(FindObjectsInactive inactive) where T : Object =>
            Object.FindAnyObjectByType<T>(inactive);

        public static T[] FindAll<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsSortMode.None);

        public static T[] FindAll<T>(FindObjectsInactive inactive) where T : Object =>
            Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);

        public static GameObject FindGameObject(string name) => GameObject.Find(name);
    }
}
