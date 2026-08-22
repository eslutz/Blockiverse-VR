using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.VR
{
    // Audio for being in water. Swimming shipped silent: the swim provider owns the only
    // authoritative submersion state on the rig (feet/body/head sampled per frame), so this
    // subscribes to its transitions rather than re-deriving "am I wet" from the world, which is
    // how you end up with two systems disagreeing about it.
    //
    // Splash on entry, a submerged loop while the head is under, and a stroke cadence while
    // actually moving. Emberflow is deliberately excluded from all three — lava is not water and
    // must not splash like it; contact with it already has its own hurt cue. It gets its own
    // loop instead, so standing in it is not silent.
    [DisallowMultipleComponent]
    public sealed class BlockiverseSwimFeedback : MonoBehaviour
    {
        // A stroke roughly every second and a half of continuous swimming. Slower than a real
        // swimmer's cadence on purpose: the player is gliding, not racing, and anything quicker
        // turns into the machine-gunning that footsteps were rate-capped to avoid.
        public const float StrokeIntervalSeconds = 1.5f;

        // Horizontal speed only, and deliberately not 0.35: that is exactly
        // BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond, so a total-speed test at that
        // threshold strokes forever for a player sinking with no input at all.
        public const float StrokeMinimumSpeed = 0.30f;

        // A splash cannot repeat inside this window. ExitSwimming forces the state to Dry whenever
        // locomotion is suppressed or creative flight takes over — opening a menu while afloat does
        // it — so without this, closing the menu re-enters and splashes again without the player
        // ever having left the water.
        public const float ResplashLockoutSeconds = 1.5f;

        // Backstop. The provider now applies BlockiverseSwimMotion.ResolveHeadSubmerged, so the
        // Swimming/Surfaced line no longer strobes at source -- but stopping a loop destroys its
        // AudioSource and starting one retriggers from sample zero, so the cost of a spurious
        // toggle is high enough to be worth a second guard. This also covers the state being
        // forced to Dry for reasons unrelated to the water, which ExitSwimming does whenever
        // locomotion is suppressed.
        public const float SubmergedReleaseSeconds = 0.35f;

        // Anything faster than this in one frame is a teleport or a respawn, not motion: it would
        // otherwise register as a maximum-volume entry splash and a stroke on the same frame.
        public const float TeleportSpeedThreshold = 25.0f;

        [SerializeField] BlockiverseSwimProvider swimProvider;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;

        Vector3 lastPosition;
        float lastDescentSpeed;
        float nextStrokeTime;
        float nextSplashAllowedTime;
        float submergedReleaseAt;
        bool subscribed;
        bool submergedLoopActive;
        bool submergedWanted;
        bool emberflowLoopActive;
        bool emberflowWanted;

        // Exposed so the rig prefab test can prove the wiring survived a regeneration, the same
        // way BlockiverseAudioCuePlayer exposes its own references.
        public BlockiverseSwimProvider SwimProvider => swimProvider;
        public BlockiverseAudioCuePlayer AudioCuePlayer => audioCuePlayer;

        public void Configure(BlockiverseSwimProvider provider, BlockiverseAudioCuePlayer cuePlayer)
        {
            Unsubscribe();
            swimProvider = provider;
            audioCuePlayer = cuePlayer;
            Subscribe();
        }

        void OnEnable()
        {
            ResolveReferences();
            lastPosition = transform.position;
            Subscribe();

            // Already underwater when enabled (scene reload, component re-enabled mid-dive): no
            // further transition is coming, so sync to the current state rather than waiting.
            if (swimProvider != null)
            {
                RequestSubmergedLoop(ShouldLoopSubmerged(swimProvider.State, swimProvider.Family));
                RequestEmberflowLoop(swimProvider.Family == FluidFamily.Emberflow &&
                                     swimProvider.State != SwimState.Dry);
            }
        }

        void OnDisable()
        {
            Unsubscribe();
            submergedWanted = false;
            StopSubmergedLoop();
            RequestEmberflowLoop(false);
        }

        void ResolveReferences()
        {
            if (swimProvider == null)
                swimProvider = GetComponent<BlockiverseSwimProvider>() ?? GetComponentInParent<BlockiverseSwimProvider>();

            if (audioCuePlayer == null)
                audioCuePlayer = GetComponent<BlockiverseAudioCuePlayer>() ?? FindFirstObjectByType<BlockiverseAudioCuePlayer>();
        }

        void Subscribe()
        {
            if (subscribed || swimProvider == null)
                return;

            swimProvider.StateChanged += OnSwimStateChanged;
            subscribed = true;
        }

        void Unsubscribe()
        {
            // Cleared even when the provider is already gone (a destroyed component reads null), or
            // a later Configure would see subscribed still true and never resubscribe.
            if (!subscribed)
                return;

            subscribed = false;

            if (swimProvider != null)
                swimProvider.StateChanged -= OnSwimStateChanged;
        }

        void OnSwimStateChanged(SwimState previous, SwimState next, FluidFamily family)
        {
            if (audioCuePlayer == null)
            {
                RequestSubmergedLoop(false);
                return;
            }

            if (family == FluidFamily.Emberflow)
            {
                // Lava gets its own bed rather than water's. Closing the water loop matters here:
                // the player may have waded straight from one fluid into the other.
                RequestSubmergedLoop(false);
                RequestEmberflowLoop(next != SwimState.Dry);
                return;
            }

            RequestEmberflowLoop(false);

            if (ShouldSplash(previous, next, family) && Time.time >= nextSplashAllowedTime)
            {
                nextSplashAllowedTime = Time.time + ResplashLockoutSeconds;
                // Scaled by how fast the player was descending as they went in, through the
                // volumeScale the thunder work added: a slow wade off a ledge is not the same
                // event as a drop from height, and loudness is the cue that says which. Scaling
                // inside ResolveVolume keeps the mix sliders and mute as the single gate.
                audioCuePlayer.PlayCue(BlockiverseAudioCue.WaterSplash, EntrySplashScale(lastDescentSpeed));
                nextStrokeTime = Time.time + StrokeIntervalSeconds;
            }

            RequestSubmergedLoop(ShouldLoopSubmerged(next, family));
        }

        void Update()
        {
            Vector3 position = transform.position;
            Vector3 delta = position - lastPosition;
            lastPosition = position;

            float frameSpeed = Time.deltaTime > 0.0f ? delta.magnitude / Time.deltaTime : 0.0f;
            bool teleported = frameSpeed > TeleportSpeedThreshold;

            // Tracked every frame, wet or dry, because the speed that matters for the entry splash
            // is the one from the frame BEFORE the swim provider took over vertical motion — by the
            // time the state flips, the fall is already over. A teleport is not a fall.
            if (Time.deltaTime > 0.0f)
                lastDescentSpeed = teleported ? 0.0f : Mathf.Max(0.0f, -delta.y / Time.deltaTime);

            TickSubmergedLoop();
            TickEmberflowLoop();

            if (audioCuePlayer == null || swimProvider == null || !swimProvider.IsSwimming)
                return;

            if (swimProvider.Family == FluidFamily.Emberflow || teleported)
                return;

            // Horizontal travel only. Vertical motion is sinking or rising, which is not a stroke,
            // and testing total speed against the passive sink rate strokes forever while the
            // player holds nothing at all.
            float horizontalSpeed = Time.deltaTime > 0.0f
                ? new Vector2(delta.x, delta.z).magnitude / Time.deltaTime
                : 0.0f;

            if (horizontalSpeed < StrokeMinimumSpeed || Time.time < nextStrokeTime)
                return;

            nextStrokeTime = Time.time + StrokeIntervalSeconds;
            audioCuePlayer.PlayCue(BlockiverseAudioCue.SwimStroke);
        }

        // The loop is requested rather than switched, so a head bobbing across the surface does not
        // destroy and recreate an AudioSource every frame.
        void RequestSubmergedLoop(bool wanted)
        {
            submergedWanted = wanted;

            if (wanted)
            {
                submergedReleaseAt = 0.0f;
                StartSubmergedLoop();
                return;
            }

            if (submergedLoopActive && submergedReleaseAt <= 0.0f)
                submergedReleaseAt = Time.time + SubmergedReleaseSeconds;
        }

        void TickSubmergedLoop()
        {
            // Still under and still silent: retry. StartLoop refuses while the resolved volume is
            // zero, so diving with Mute All on, or with the master or weather slider at zero, fails
            // to latch — and the loop is only ever requested on a state CHANGE, so without this the
            // player would have to leave the water and come back before unmuting had any effect.
            if (submergedWanted)
            {
                if (!submergedLoopActive)
                    StartSubmergedLoop();

                return;
            }

            if (!submergedLoopActive || submergedReleaseAt <= 0.0f)
                return;

            if (Time.time >= submergedReleaseAt)
                StopSubmergedLoop();
        }

        // The decisions, as pure functions so they can be pinned in EditMode without a rig, an
        // XR origin, or water — the same shape as BlockiverseSwimMotion's motion maths.

        /// <summary>
        /// True when this transition is the player actually entering the water.
        /// Wading is walking, not entry: footsteps already cover shallow water through the Water
        /// surface bank, so splashing there would double up on every step.
        /// </summary>
        public static bool ShouldSplash(SwimState previous, SwimState next, FluidFamily family)
        {
            if (family == FluidFamily.Emberflow)
                return false;

            bool wasOut = previous == SwimState.Dry || previous == SwimState.Wading;
            bool isIn = next == SwimState.Swimming || next == SwimState.Surfaced;
            return wasOut && isIn;
        }

        /// <summary>
        /// True while the head is under. This is the same line the underwater view switches on,
        /// so the sound changes at the moment the view does rather than a beat away from it.
        /// </summary>
        public static bool ShouldLoopSubmerged(SwimState state, FluidFamily family) =>
            state == SwimState.Swimming && family != FluidFamily.Emberflow;

        // A step off a bank barely registers; a fall from height lands full. Floored so a
        // gentle entry is still audible rather than silently swallowed.
        public static float EntrySplashScale(float descentSpeedMetersPerSecond)
        {
            const float quietEntry = 1.0f;
            const float loudEntry = 6.0f;
            float t = Mathf.InverseLerp(quietEntry, loudEntry, descentSpeedMetersPerSecond);
            return Mathf.Lerp(0.45f, 1.0f, t);
        }

        // Emberflow needs no release dwell — there is no surface line to bob across, you are
        // either standing in lava or you are not — but it does need the same retry as the
        // submerged loop, and for the same reason: StartLoop refuses at zero resolved volume and
        // nothing asks again until the swim state next changes.
        void TickEmberflowLoop()
        {
            if (emberflowWanted && !emberflowLoopActive && audioCuePlayer != null)
            {
                emberflowLoopActive = audioCuePlayer.StartLoop(BlockiverseAudioCue.EmberflowLoop) ||
                                      audioCuePlayer.IsLoopActive(BlockiverseAudioCue.EmberflowLoop);
            }
        }

        void RequestEmberflowLoop(bool wanted)
        {
            emberflowWanted = wanted;

            if (wanted == emberflowLoopActive || audioCuePlayer == null)
                return;

            if (wanted)
            {
                emberflowLoopActive = audioCuePlayer.StartLoop(BlockiverseAudioCue.EmberflowLoop) ||
                                      audioCuePlayer.IsLoopActive(BlockiverseAudioCue.EmberflowLoop);
                return;
            }

            emberflowLoopActive = false;
            audioCuePlayer.StopLoop(BlockiverseAudioCue.EmberflowLoop);
        }

        void StartSubmergedLoop()
        {
            if (submergedLoopActive || audioCuePlayer == null)
                return;

            // Only latch when the player actually took it. StartLoop returns false when the cue
            // has no clip or the volume resolves to zero; latching regardless would leave the dive
            // silent and never retry.
            submergedLoopActive = audioCuePlayer.StartLoop(BlockiverseAudioCue.SubmergedLoop) ||
                                  audioCuePlayer.IsLoopActive(BlockiverseAudioCue.SubmergedLoop);
        }

        void StopSubmergedLoop()
        {
            submergedReleaseAt = 0.0f;

            if (!submergedLoopActive)
                return;

            submergedLoopActive = false;
            audioCuePlayer?.StopLoop(BlockiverseAudioCue.SubmergedLoop);
        }
    }
}
