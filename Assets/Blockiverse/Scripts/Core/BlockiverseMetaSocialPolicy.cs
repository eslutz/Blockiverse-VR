using System;

namespace Blockiverse.Core
{
    // Core seam that lets UI consult the Meta social-feature age policy without referencing the
    // Blockiverse.MetaPlatform assembly. The MetaPlatform layer registers
    // CanUseMetaSocialFeatureCallback when its age-category service type is first touched (scene
    // load in play mode, or test setup in edit mode). When no callback is registered the policy
    // defaults to the permissive value (true), which matches the Unknown-age-category default.
    public static class BlockiverseMetaSocialPolicy
    {
        public static Func<bool> CanUseMetaSocialFeatureCallback;

        public static bool CanUseMetaSocialFeature =>
            CanUseMetaSocialFeatureCallback == null || CanUseMetaSocialFeatureCallback();
    }
}
