using UnityEngine;
using UnityEngine.Events;

namespace Blockiverse.Core
{
    public interface IBlockiverseInputRig
    {
        bool LocomotionSuppressed { get; set; }
        UnityEvent MenuPressed { get; }

        // Support grip. Opens the gameplay-screens hub (Inventory/Crafting/Shared Crate/Blocks) —
        // the same reliable, tracking-independent fallback the wrist menu's gesture needs. Formerly
        // toggled the Creative quick block menu, retired once the catalog screen covered the same
        // job from the wrist menu.
        UnityEvent ScreensPressed { get; }
        UnityEvent BreakPressed { get; }

        // Hotbar slot cycling, bound to the support hand's two face buttons — the only gameplay
        // inputs the shipped controller mapping leaves unclaimed. Added 2026-08-25 with the
        // persistent hotbar strip: the report's point is that changing the held item is the most
        // frequent action in a voxel game and must not cost a screen open, and a strip nobody can
        // drive from the controller would not fix that.
        UnityEvent HotbarNextPressed { get; }
        UnityEvent HotbarPreviousPressed { get; }
        bool TryGetActiveInteractionRayPose(out Vector3 rayOrigin, out Vector3 rayDirection);
        bool TryGetInteractionRayPose(BlockiverseControllerRole hand, out Vector3 rayOrigin, out Vector3 rayDirection);
    }
}
