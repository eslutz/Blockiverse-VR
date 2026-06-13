using System;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // Creative Tools screen (voxel_creative_ruleset §12): region selection (corner A/B from the
    // targeted block) with fill/replace/delete/copy/paste through WorldEditService, region
    // undo/redo, vegetation/structure spawners, a pick-block action, and environment controls
    // (time-of-day, day-cycle speed, weather). Region edits bypass the per-block authority
    // channel, so everything that mutates the world is gated to offline creative worlds.
    [DisallowMultipleComponent]
    public sealed class BlockiverseCreativeToolsPanel : MonoBehaviour
    {
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] CreativeHotbar hotbar;
        [SerializeField] TMP_Text cornersLabel;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] TMP_Text weatherLabel;
        [SerializeField] Slider timeOfDaySlider;
        [SerializeField] Slider timeScaleSlider;

        readonly WorldEditService editService = new();

        BlockPosition? cornerA;
        BlockPosition? cornerB;
        // The last block the interaction ray pointed at: pressing a panel button moves the ray
        // over UI (clearing the live target), so actions use this cached aim instead.
        BlockPosition? lastTarget;
        VoxelWorld trackedWorld;
        Func<bool> networkSessionActiveProvider;
        bool wired;

        public int WorldEditUndoCount => editService.UndoCount;
        public bool HasWorldEditClipboard => editService.HasClipboard;

        public void ConfigureNetworkSessionActiveProvider(Func<bool> provider)
        {
            networkSessionActiveProvider = provider;
        }

        public void Configure(
            CreativeInteractionController controller,
            CreativeWorldManager manager,
            CreativeHotbar creativeHotbar,
            TMP_Text corners,
            TMP_Text status,
            TMP_Text weather,
            Slider timeOfDay,
            Slider timeScale)
        {
            UnwireSliders();
            interactionController = controller;
            worldManager = manager;
            hotbar = creativeHotbar;
            cornersLabel = corners;
            statusLabel = status;
            weatherLabel = weather;
            timeOfDaySlider = timeOfDay;
            timeScaleSlider = timeScale;
            WireSliders();
            ResetWorldEditStateIfWorldChanged();
        }

        void OnEnable()
        {
            ResolveReferences();
            ResetWorldEditStateIfWorldChanged();
            WireSliders();
            RefreshEnvironmentControls();
            RefreshCornersLabel();
        }

        void OnDisable()
        {
            UnwireSliders();
        }

        void Update()
        {
            ResetWorldEditStateIfWorldChanged();

            BlockPosition? target = interactionController != null ? interactionController.CurrentTarget : null;
            if (target.HasValue)
                lastTarget = target;
        }

        void ResolveReferences()
        {
            if (interactionController == null)
                interactionController = FindFirstObjectByType<CreativeInteractionController>(FindObjectsInactive.Include);

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (hotbar == null)
                hotbar = FindFirstObjectByType<CreativeHotbar>(FindObjectsInactive.Include);
        }

        void WireSliders()
        {
            if (wired)
                return;

            timeOfDaySlider?.onValueChanged.AddListener(OnTimeOfDayChanged);
            timeScaleSlider?.onValueChanged.AddListener(OnTimeScaleChanged);
            wired = true;
        }

        void UnwireSliders()
        {
            if (!wired)
                return;

            timeOfDaySlider?.onValueChanged.RemoveListener(OnTimeOfDayChanged);
            timeScaleSlider?.onValueChanged.RemoveListener(OnTimeScaleChanged);
            wired = false;
        }

        // Pushes the live clock/weather values into the controls without re-firing listeners.
        public void RefreshEnvironmentControls()
        {
            WorldTimeClock clock = worldManager != null ? worldManager.WorldTimeClock : null;
            if (clock != null)
            {
                timeOfDaySlider?.SetValueWithoutNotify(clock.NormalizedTime);
                timeScaleSlider?.SetValueWithoutNotify(clock.TimeScale);
            }

            if (weatherLabel != null && worldManager != null)
                weatherLabel.text = BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.CreativeWeather,
                    BlockiverseLocalization.DisplayName(worldManager.GetWeatherSyncState().State));
        }

        // ── Region selection ──────────────────────────────────────────────────

        public void SetCornerA()
        {
            cornerA = CaptureAim(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeCornerA));
            RefreshCornersLabel();
        }

        public void SetCornerB()
        {
            cornerB = CaptureAim(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeCornerB));
            RefreshCornersLabel();
        }

        BlockPosition? CaptureAim(string what)
        {
            if (!lastTarget.HasValue)
            {
                SetStatus(BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativeSetCornerAim, what));
                return null;
            }

            SetStatus(BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativeSetCorner, what, lastTarget.Value));
            return lastTarget;
        }

        void RefreshCornersLabel()
        {
            if (cornersLabel == null)
                return;

            string a = cornerA.HasValue ? cornerA.Value.ToString() : "—";
            string b = cornerB.HasValue ? cornerB.Value.ToString() : "—";
            cornersLabel.text = BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativeCorners, a, b);
        }

        // ── Region operations (§12.1) ─────────────────────────────────────────

        public void FillRegion()
        {
            if (!TryGetRegion(out BlockPosition min, out BlockPosition max) || !CanEdit(out VoxelWorld world))
                return;

            ReportEdit(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeToolsFill),
                editService.Fill(world, min, max, hotbar.SelectedBlockId));
        }

        public void DeleteRegion()
        {
            if (!TryGetRegion(out BlockPosition min, out BlockPosition max) || !CanEdit(out VoxelWorld world))
                return;

            ReportEdit(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonDelete),
                editService.Delete(world, min, max));
        }

        // Replaces every block of the aimed-at type inside the region with the hotbar selection.
        public void ReplaceRegion()
        {
            if (!TryGetRegion(out BlockPosition min, out BlockPosition max) || !CanEdit(out VoxelWorld world))
                return;

            if (!lastTarget.HasValue || !world.Bounds.Contains(lastTarget.Value))
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeAimReplace));
                return;
            }

            BlockId targetType = world.GetBlock(lastTarget.Value);
            ReportEdit(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeToolsReplace),
                editService.Replace(world, min, max, targetType, hotbar.SelectedBlockId));
        }

        public void CopyRegion()
        {
            if (!TryGetRegion(out BlockPosition min, out BlockPosition max) || !CanEdit(out VoxelWorld world))
                return;

            ReportEdit(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeToolsCopy),
                editService.Copy(world, min, max));
        }

        // Pastes the clipboard with its min corner at corner A (or the current aim).
        public void PasteRegion()
        {
            if (!CanEdit(out VoxelWorld world))
                return;

            BlockPosition? origin = cornerA ?? lastTarget;
            if (!origin.HasValue)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeChoosePasteOrigin));
                return;
            }

            ReportEdit(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeToolsPaste),
                editService.Paste(world, origin.Value));
        }

        public void UndoEdit()
        {
            if (!CanEdit(out VoxelWorld world))
                return;

            ReportEdit(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeToolsUndo),
                editService.Undo(world));
        }

        public void RedoEdit()
        {
            if (!CanEdit(out VoxelWorld world))
                return;

            ReportEdit(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeToolsRedo),
                editService.Redo(world));
        }

        // ── Spawners / pick block ─────────────────────────────────────────────

        public void SpawnTree()
        {
            if (!CanEdit(out VoxelWorld world) || !TryGetAimAbove(world, out BlockPosition basePos))
                return;

            new VegetationService().PlaceStandardTree(world, basePos, trackChange: true);
            worldManager.Renderer?.RebuildDirty();
            SetStatus(BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativeSpawnedTree, basePos));
        }

        public void SpawnRuin()
        {
            if (!CanEdit(out VoxelWorld world) || !TryGetAimAbove(world, out BlockPosition basePos))
                return;

            StructureService.PlaceStructureAt(world, basePos.X, basePos.Y, basePos.Z, world.Seed, trackChange: true);
            worldManager.Renderer?.RebuildDirty();
            SetStatus(BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativeSpawnedRuin, basePos));
        }

        // Puts the aimed-at block into the hotbar selection (block picker).
        public void PickBlock()
        {
            ResetWorldEditStateIfWorldChanged();

            VoxelWorld world = worldManager != null ? worldManager.World : null;
            if (world == null || hotbar == null || !lastTarget.HasValue || !world.Bounds.Contains(lastTarget.Value))
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeAimPick));
                return;
            }

            BlockId picked = world.GetBlock(lastTarget.Value);
            if (hotbar.SelectBlock(picked))
                SetStatus(BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativePicked, picked));
            else
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeMissingCatalogBlock));
        }

        // ── Environment controls ──────────────────────────────────────────────

        void OnTimeOfDayChanged(float value)
        {
            if (NetworkSessionActive())
            {
                RefreshEnvironmentControls();
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeTimeHostOnly));
                return;
            }

            worldManager?.WorldTimeClock?.SetNormalizedTime(value);
        }

        void OnTimeScaleChanged(float value)
        {
            if (NetworkSessionActive())
            {
                RefreshEnvironmentControls();
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeTimeHostOnly));
                return;
            }

            worldManager?.WorldTimeClock?.SetTimeScale(value);
        }

        // Steps the weather to the next state (wrapping through every WeatherState preset).
        public void CycleWeather()
        {
            if (worldManager == null)
                return;

            if (NetworkSessionActive())
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeWeatherHostOnly));
                return;
            }

            var states = (WeatherState[])Enum.GetValues(typeof(WeatherState));
            WeatherState current = worldManager.GetWeatherSyncState().State;
            int next = (Array.IndexOf(states, current) + 1) % states.Length;
            worldManager.SetWeather(states[next]);
            RefreshEnvironmentControls();
        }

        // ── Shared gating/reporting ───────────────────────────────────────────

        bool TryGetRegion(out BlockPosition min, out BlockPosition max)
        {
            min = default;
            max = default;

            if (!cornerA.HasValue || !cornerB.HasValue)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeSetCornersFirst));
                return false;
            }

            BlockPosition a = cornerA.Value;
            BlockPosition b = cornerB.Value;
            min = new BlockPosition(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
            max = new BlockPosition(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
            return true;
        }

        // Region edits write directly to the world (no per-block authority round-trip), so they
        // are only legal in offline creative worlds (§12 permission gating).
        bool CanEdit(out VoxelWorld world)
        {
            ResetWorldEditStateIfWorldChanged();
            world = worldManager != null ? worldManager.World : null;

            if (world == null)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeNoWorld));
                return false;
            }

            if (worldManager.GameMode != WorldGameMode.Creative)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeOnly));
                return false;
            }

            if (NetworkSessionActive())
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeLanUnavailable));
                return false;
            }

            return true;
        }

        void ResetWorldEditStateIfWorldChanged()
        {
            VoxelWorld currentWorld = worldManager != null ? worldManager.World : null;
            if (ReferenceEquals(currentWorld, trackedWorld))
                return;

            trackedWorld = currentWorld;
            editService.Reset();
            cornerA = null;
            cornerB = null;
            lastTarget = null;
            RefreshCornersLabel();
        }

        bool NetworkSessionActive()
        {
            if (networkSessionActiveProvider != null)
                return networkSessionActiveProvider();

            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }

        bool TryGetAimAbove(VoxelWorld world, out BlockPosition above)
        {
            above = default;

            if (!lastTarget.HasValue || !world.Bounds.Contains(lastTarget.Value))
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeAimGround));
                return false;
            }

            above = new BlockPosition(lastTarget.Value.X, lastTarget.Value.Y + 1, lastTarget.Value.Z);
            if (!world.Bounds.Contains(above))
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeNoRoomAbove));
                return false;
            }

            return true;
        }

        void ReportEdit(string operation, WorldEditResult result)
        {
            if (result == WorldEditResult.Success)
            {
                worldManager.Renderer?.RebuildDirty();
                SetStatus(BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativeOperationDone, operation));
                return;
            }

            SetStatus(result switch
            {
                WorldEditResult.VolumeLimitExceeded => BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativeVolumeLimit, operation),
                WorldEditResult.OutOfBounds => BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativeOutOfBounds, operation),
                WorldEditResult.NoClipboard => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeNoClipboard),
                WorldEditResult.NothingToUndo => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeNothingToUndo),
                WorldEditResult.NothingToRedo => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeNothingToRedo),
                WorldEditResult.NothingToReplace => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeNothingToReplace),
                _ => BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CreativeOperationFailed, operation)
            });
        }

        void SetStatus(string message)
        {
            if (statusLabel != null)
                statusLabel.text = message;
        }
    }
}
