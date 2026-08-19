using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blockiverse.VR
{
    public sealed class BlockiverseCreativeFlightController : MonoBehaviour
    {
        const float FlightSpeedBlocksPerTick = 0.10f;
        const float SprintFlightSpeedBlocksPerTick = 0.22f;
        const float DoubleClickWindowSeconds = 0.35f;
        // A press longer than this is a flight hold, not a tap.
        const float TapMaxDurationSeconds = 0.25f;

        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] bool flightEnabledDefault;
        [SerializeField] Transform dominantHandAimSource;

        Transform cachedLeftHandAimSource;
        Transform cachedRightHandAimSource;

        bool hasExplicitFlightState;
        bool requestedFlightActive;
        bool providerStateInitialized;
        bool lastProviderActive;
        bool jumpHeldLastFrame;
        float jumpPressStartedAt = -1.0f;
        float lastTapEndedAt = -10.0f;
        bool lastPressWasTap;

        public BlockiverseInputRig InputRig => inputRig;
        public bool IsFlightActive { get; private set; }
        public bool FlightEnabledDefault
        {
            get => flightEnabledDefault;
            set => flightEnabledDefault = value;
        }

        public static float FlightSpeedBlocksPerSecond => FlightSpeedBlocksPerTick * SimulationTime.TicksPerSecond;
        public static float SprintFlightSpeedBlocksPerSecond => SprintFlightSpeedBlocksPerTick * SimulationTime.TicksPerSecond;

        public void Configure(
            BlockiverseInputRig rig,
            CreativeWorldManager manager = null,
            MultiplayerSurvivalSync sync = null)
        {
            if (inputRig != rig)
            {
                inputRig = rig;
                ClearCachedAimSources();
            }

            worldManager = manager != null ? manager : worldManager;
            survivalSync = sync != null ? sync : survivalSync;
            if (Application.isPlaying)
                ApplyFlightState();
        }

        void OnEnable()
        {
            DiscoverDependencies();
            ApplyFlightState();
        }

        void OnDisable()
        {
            ApplyProviderState(active: false);
            IsFlightActive = false;
        }

        void Update()
        {
            UpdateFlightToggleInput();
            ApplyFlightState();
            TickFlightMotion(Time.deltaTime);
        }

        public void SetFlightActive(bool active)
        {
            hasExplicitFlightState = true;
            requestedFlightActive = active;
            ApplyFlightState();
        }

        public void ToggleFlightMode()
        {
            SetFlightActive(!IsFlightRequestedActive());
        }

        public void ApplyFlightState()
        {
            DiscoverDependencies();

            bool creative = IsCreativePlayer();
            if (!creative)
            {
                hasExplicitFlightState = false;
                requestedFlightActive = false;
            }

            bool active = creative && IsFlightRequestedActive();
            ApplyProviderState(active);
            IsFlightActive = active;
        }

        /// <summary>A press short enough to count as a tap; anything longer is a flight hold.</summary>
        public static bool IsTapPress(float pressDurationSeconds) =>
            pressDurationSeconds >= 0.0f && pressDurationSeconds <= TapMaxDurationSeconds;

        /// <summary>
        /// A new press completes a double-tap only when the previous press was itself a tap, so
        /// holding the button to fly can never toggle flight off mid-air.
        /// </summary>
        public static bool CompletesDoubleTap(bool previousPressWasTap, float secondsSincePreviousPress) =>
            previousPressWasTap &&
            secondsSincePreviousPress >= 0.0f &&
            secondsSincePreviousPress <= DoubleClickWindowSeconds;

        public static Vector3 ComputeFlightDisplacement(Vector3 aimForward, bool moveHeld, float deltaSeconds)
        {
            return ComputeFlightDisplacement(aimForward, moveHeld, sprintActive: false, deltaSeconds);
        }

        public static Vector3 ComputeFlightDisplacement(Vector3 aimForward, bool moveHeld, bool sprintActive, float deltaSeconds)
        {
            if (!moveHeld || deltaSeconds <= 0.0f)
                return Vector3.zero;

            if (aimForward.sqrMagnitude <= 0.0001f)
                return Vector3.zero;

            float speed = sprintActive ? SprintFlightSpeedBlocksPerSecond : FlightSpeedBlocksPerSecond;
            return aimForward.normalized * speed * deltaSeconds;
        }

        void DiscoverDependencies()
        {
            if (inputRig == null)
            {
                BlockiverseInputRig discoveredRig = GetComponent<BlockiverseInputRig>() ?? GetComponentInParent<BlockiverseInputRig>();
                if (discoveredRig != null)
                {
                    inputRig = discoveredRig;
                    ClearCachedAimSources();
                }
            }

            if (!Application.isPlaying)
                return;

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (survivalSync == null)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
        }

        bool IsCreativePlayer()
        {
            if (survivalSync != null)
                return survivalSync.CurrentMode == PlayerModeState.Creative;

            return worldManager != null && worldManager.GameMode == WorldGameMode.Creative;
        }

        bool IsFlightRequestedActive()
        {
            return hasExplicitFlightState ? requestedFlightActive : flightEnabledDefault;
        }

        void UpdateFlightToggleInput()
        {
            if (!Application.isPlaying || !BlockiverseRuntimeState.AllowWorldInput)
                return;

            InputAction jump = ResolveJumpAction();
            if (jump == null)
            {
                jumpHeldLastFrame = false;
                return;
            }

            // Edges are derived from IsPressed() polling rather than WasPressedThisFrame /
            // WasReleasedThisFrame: IsPressed is the reading that reliably drives hold-to-fly on
            // device, and depending on the edge helpers here left flight untoggleable.
            float now = Time.unscaledTime;
            bool held = jump.IsPressed();

            if (held && !jumpHeldLastFrame)
            {
                // Toggle on the second press of a double-tap. The previous press must have been a
                // quick tap, so holding the button to fly can never complete a double-tap and
                // switch flight off mid-air. Taps count in both directions, so a double-tap
                // enters flight and a second double-tap exits it (gravity then takes over).
                if (CompletesDoubleTap(lastPressWasTap, now - lastTapEndedAt))
                {
                    ToggleFlightMode();
                    lastPressWasTap = false;
                    lastTapEndedAt = -10.0f;
                }

                jumpPressStartedAt = now;
            }
            else if (!held && jumpHeldLastFrame)
            {
                float pressDuration = jumpPressStartedAt >= 0.0f ? now - jumpPressStartedAt : float.MaxValue;
                jumpPressStartedAt = -1.0f;
                lastPressWasTap = IsTapPress(pressDuration);
                lastTapEndedAt = now;
            }

            jumpHeldLastFrame = held;
        }

        void TickFlightMotion(float deltaSeconds)
        {
            if (!Application.isPlaying || !IsFlightActive || !BlockiverseRuntimeState.AllowWorldInput)
                return;

            InputAction jump = ResolveJumpAction();
            bool sprintActive = inputRig != null && inputRig.SprintActive;
            Vector3 displacement = ComputeFlightDisplacement(
                ResolveFlightForward(),
                jump != null && jump.IsPressed(),
                sprintActive,
                deltaSeconds);
            if (displacement.sqrMagnitude > 0.0f)
                transform.position += displacement;
        }

        InputAction ResolveJumpAction()
        {
            return inputRig != null ? inputRig.ResolveJumpActionForCurrentControls() : null;
        }

        Vector3 ResolveFlightForward()
        {
            Transform aim = ResolveDominantHandAimSource();
            Vector3 forward = aim != null ? aim.forward : transform.forward;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        Transform ResolveDominantHandAimSource()
        {
            if (dominantHandAimSource != null)
                return dominantHandAimSource;

            BlockiverseControllerRole hand = inputRig != null
                ? inputRig.ActiveToolHand
                : BlockiverseControllerRole.Right;

            return hand == BlockiverseControllerRole.Left
                ? ResolveLeftHandAimSource()
                : ResolveRightHandAimSource();
        }

        Transform ResolveLeftHandAimSource()
        {
            if (cachedLeftHandAimSource == null)
                cachedLeftHandAimSource = ResolveControllerAimSource("Left Controller");

            return cachedLeftHandAimSource;
        }

        Transform ResolveRightHandAimSource()
        {
            if (cachedRightHandAimSource == null)
                cachedRightHandAimSource = ResolveControllerAimSource("Right Controller");

            return cachedRightHandAimSource;
        }

        // Flight travels along where the controller POINTS, which is the ray origin the pointer
        // rays use — NOT the controller transform, whose forward is the OpenXR grip pose running
        // up the handle (holding the controller level would fly the player straight up).
        //
        // Returns null until the rig has built the ray origin; ResolveFlightForward falls back to
        // the rig forward for that frame rather than caching a grip-posed source, which would
        // leave flight 90 degrees off for the rest of the session.
        Transform ResolveControllerAimSource(string controllerName)
        {
            Transform root = inputRig != null ? inputRig.transform : transform;
            Transform cameraOffset = root.Find("Camera Offset");
            Transform controller = cameraOffset != null ? cameraOffset.Find(controllerName) : null;
            return controller != null ? controller.Find("Ray Origin") : null;
        }

        void ClearCachedAimSources()
        {
            cachedLeftHandAimSource = null;
            cachedRightHandAimSource = null;
        }

        void ApplyProviderState(bool active)
        {
            if (inputRig == null)
                return;

            bool providerStateChanged = !providerStateInitialized || lastProviderActive != active;
            providerStateInitialized = true;
            lastProviderActive = active;
            inputRig.TurnWithBothHands = active;
            inputRig.CreativeFlightLocomotionActive = active;

            if (inputRig.LocomotionSuppressed)
            {
                var suppressedMove = inputRig.ContinuousMoveProvider;
                if (suppressedMove != null)
                {
                    suppressedMove.enableFly = false;
                    suppressedMove.enabled = false;
                }

                var suppressedGravity = inputRig.GravityProvider;
                if (suppressedGravity != null)
                {
                    suppressedGravity.enabled = true;
                    suppressedGravity.useGravity = false;
                }

                var suppressedJump = inputRig.JumpProvider;
                if (suppressedJump != null)
                    suppressedJump.enabled = false;

                return;
            }

            var move = inputRig.ContinuousMoveProvider;
            if (move != null)
            {
                move.enableFly = false;
                if (active)
                    move.enabled = false;
            }

            var gravity = inputRig.GravityProvider;
            if (gravity != null)
            {
                gravity.enabled = true;
                gravity.useGravity = !active;
            }

            var jump = inputRig.JumpProvider;
            if (jump != null)
                jump.enabled = !active && move != null && move.enabled;

            if (!active && providerStateChanged)
                inputRig.RefreshLocomotionProviderState();
        }
    }
}
