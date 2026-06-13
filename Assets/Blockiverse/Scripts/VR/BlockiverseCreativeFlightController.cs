using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseCreativeFlightController : MonoBehaviour
    {
        const float FlightSpeedBlocksPerTick = 0.10f;

        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] bool flightEnabledDefault = true;

        public BlockiverseInputRig InputRig => inputRig;
        public bool IsFlightActive { get; private set; }
        public bool FlightEnabledDefault
        {
            get => flightEnabledDefault;
            set => flightEnabledDefault = value;
        }

        public static float FlightSpeedBlocksPerSecond => FlightSpeedBlocksPerTick * SimulationTime.TicksPerSecond;

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
            ApplyFlightState();
        }

        public void ApplyFlightState()
        {
            DiscoverDependencies();

            bool active = flightEnabledDefault && IsCreativePlayer();
            ApplyProviderState(active);
            IsFlightActive = active;
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

        void ApplyProviderState(bool active)
        {
            if (inputRig == null)
                return;

            var move = inputRig.ContinuousMoveProvider;
            if (move != null)
            {
                move.enableFly = active;
                if (active)
                    move.moveSpeed = Mathf.Max(move.moveSpeed, FlightSpeedBlocksPerSecond);
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
        }
    }
}
