using Blockiverse.Core;
using Blockiverse.Gameplay;
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
        float lastJumpPressTime = -10.0f;

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
            if (jump == null || !jump.WasPressedThisFrame())
                return;

            float now = Time.unscaledTime;
            if (now - lastJumpPressTime <= DoubleClickWindowSeconds)
            {
                ToggleFlightMode();
                lastJumpPressTime = -10.0f;
                return;
            }

            lastJumpPressTime = now;
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

        Transform ResolveControllerAimSource(string controllerName)
        {
            Transform root = inputRig != null ? inputRig.transform : transform;
            Transform cameraOffset = root.Find("Camera Offset");
            Transform aimSource = cameraOffset != null ? cameraOffset.Find(controllerName) : null;
            if (aimSource == null)
                aimSource = root.Find(controllerName);
            if (aimSource == null)
                aimSource = root;

            return aimSource;
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
