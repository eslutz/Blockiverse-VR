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
        [SerializeField] bool flightEnabledDefault = true;
        [SerializeField] Transform rightHandAimSource;

        InputActionAsset cachedInputActions;
        InputAction cachedJumpAction;
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
            inputRig = rig;
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
                inputRig = GetComponent<BlockiverseInputRig>() ?? GetComponentInParent<BlockiverseInputRig>();

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
            InputActionAsset actions = inputRig != null ? inputRig.InputActions : null;
            if (actions == null)
                return null;

            if (cachedInputActions == actions && cachedJumpAction != null)
                return cachedJumpAction;

            cachedInputActions = actions;
            InputActionMap gameplayMap = actions.FindActionMap(BlockiverseInputActionNames.GameplayMap, throwIfNotFound: false);
            cachedJumpAction = gameplayMap?.FindAction(BlockiverseInputActionNames.Jump, throwIfNotFound: false);
            return cachedJumpAction;
        }

        Vector3 ResolveFlightForward()
        {
            Transform aim = ResolveRightHandAimSource();
            Vector3 forward = aim != null ? aim.forward : transform.forward;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        Transform ResolveRightHandAimSource()
        {
            if (rightHandAimSource != null)
                return rightHandAimSource;

            Transform root = inputRig != null ? inputRig.transform : transform;
            Transform cameraOffset = root.Find("Camera Offset");
            rightHandAimSource =
                cameraOffset != null ? cameraOffset.Find("Right Aim Pose") : null;
            if (rightHandAimSource == null)
                rightHandAimSource = root.Find("Right Aim Pose");
            if (rightHandAimSource == null && cameraOffset != null)
                rightHandAimSource = cameraOffset.Find("Right Controller");
            if (rightHandAimSource == null)
                rightHandAimSource = root;

            return rightHandAimSource;
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
