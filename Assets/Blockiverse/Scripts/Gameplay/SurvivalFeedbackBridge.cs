using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace Blockiverse.Gameplay
{
    // Presentation bridge for survival commands and multiplayer presence: plays the audio/VFX
    // cues for the local player's harvest/place/strip/till/plant/consume results (the creative
    // path has its own bridge via CreativeInteractionController.BlockMutationApplied) and the
    // join/leave stingers for remote peers. Pure feedback — no game state.
    [DisallowMultipleComponent]
    public sealed class SurvivalFeedbackBridge : MonoBehaviour
    {
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] SurvivalVitalsRuntime vitalsRuntime;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseVfxCuePlayer vfxCuePlayer;
        [SerializeField] BlockiverseSubtitleToastPanel toastPanel;

        BlockiverseNetworkSession session;

        // A harvest's cue has to sound like the block that was removed, but command feedback is
        // raised AFTER the authoritative mutation has already replaced the cell with air, so
        // reading the world at that point yields air and the material-aware banks never fire.
        // These remember what each removal took away, keyed by where.
        //
        // Bounded and consumed on read: an entry that is never claimed (a mutation with no command
        // feedback behind it — a remote player's edit, world simulation) must not accumulate, and
        // must not still be sitting there to answer for a later break at the same position.
        // A ring rather than a dictionary plus an eviction queue: those are one bounded
        // structure split in two, and keeping only one half maintained is how a consumed entry
        // leaves a stale id queued that can later evict a live record for the same position.
        // Thirty-two linear comparisons cost nothing at the rate blocks break.
        const int MaxRememberedRemovals = 32;
        readonly (BlockPosition position, BlockId block)[] removedBlocks =
            new (BlockPosition, BlockId)[MaxRememberedRemovals];
        readonly bool[] removedBlockUsed = new bool[MaxRememberedRemovals];
        int nextRemovalSlot;
        VoxelWorld subscribedWorld;

        bool subscribedToNetworking;
        bool subscribedToSync;
        bool subscribedToLoot;
        bool subscribedToVitals;

        public void ConfigureVitalsFeedback(
            SurvivalVitalsRuntime runtime,
            BlockiverseAudioCuePlayer cuePlayer)
        {
            UnsubscribeVitals();
            vitalsRuntime = runtime;
            audioCuePlayer = cuePlayer;
            SubscribeVitals();
        }

        public void ConfigureToastPanel(BlockiverseSubtitleToastPanel panel)
        {
            toastPanel = panel;
        }

        void OnEnable()
        {
            DiscoverDependencies();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void DiscoverDependencies()
        {
            if (!Application.isPlaying)
                return;

            if (survivalSync == null)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (vitalsRuntime == null)
                vitalsRuntime = FindFirstObjectByType<SurvivalVitalsRuntime>(FindObjectsInactive.Include);

            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            if (vfxCuePlayer == null)
                vfxCuePlayer = FindFirstObjectByType<BlockiverseVfxCuePlayer>();

            if (toastPanel == null)
                toastPanel = FindFirstObjectByType<BlockiverseSubtitleToastPanel>(FindObjectsInactive.Include);

            if (session == null)
                session = FindFirstObjectByType<BlockiverseNetworkSession>(FindObjectsInactive.Include);
        }

        void Subscribe()
        {
            if (survivalSync != null && !subscribedToSync)
            {
                survivalSync.CommandFeedback += OnCommandFeedback;
                subscribedToSync = true;
            }

            if (worldManager != null && !subscribedToLoot)
            {
                worldManager.ContainerLooted += OnContainerLooted;
                subscribedToLoot = true;
            }

            SubscribeWorld();

            SubscribeVitals();

            if (session != null && !subscribedToNetworking)
            {
                session.ClientConnected += OnClientConnected;
                session.ClientDisconnected += OnClientDisconnected;
                subscribedToNetworking = true;
            }
        }

        void Unsubscribe()
        {
            if (survivalSync != null && subscribedToSync)
                survivalSync.CommandFeedback -= OnCommandFeedback;
            subscribedToSync = false;

            if (worldManager != null && subscribedToLoot)
                worldManager.ContainerLooted -= OnContainerLooted;
            subscribedToLoot = false;

            UnsubscribeWorld();

            UnsubscribeVitals();

            if (session != null && subscribedToNetworking)
            {
                session.ClientConnected -= OnClientConnected;
                session.ClientDisconnected -= OnClientDisconnected;
                subscribedToNetworking = false;
            }
        }

        void SubscribeVitals()
        {
            if (subscribedToVitals || vitalsRuntime == null)
                return;

            vitalsRuntime.LocalPlayerDamaged += OnLocalPlayerDamaged;
            vitalsRuntime.LocalPlayerLowHealth += OnLocalPlayerLowHealth;
            vitalsRuntime.LocalPlayerDied += OnLocalPlayerDied;
            vitalsRuntime.WorldDrinkTaken += OnWorldDrinkTaken;
            subscribedToVitals = true;
        }

        void UnsubscribeVitals()
        {
            if (!subscribedToVitals || vitalsRuntime == null)
            {
                subscribedToVitals = false;
                return;
            }

            vitalsRuntime.LocalPlayerDamaged -= OnLocalPlayerDamaged;
            vitalsRuntime.LocalPlayerLowHealth -= OnLocalPlayerLowHealth;
            vitalsRuntime.LocalPlayerDied -= OnLocalPlayerDied;
            vitalsRuntime.WorldDrinkTaken -= OnWorldDrinkTaken;
            subscribedToVitals = false;
        }

        // CreativeWorldManager replaces the VoxelWorld instance on a new world, a load, and a
        // multiplayer join. Re-checking here rather than only when feedback arrives is what makes
        // the listener live before the first mutation instead of one command behind it — the first
        // harvest in a session is exactly the one that would otherwise have gone unrecorded.
        // A reference comparison per frame; SubscribeWorld returns immediately when nothing moved.
        void Update()
        {
            SubscribeWorld();
        }

        void OnCommandFeedback(SurvivalCommandResult result, BlockPosition position)
        {
            Vector3 worldCenter = new(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);

            switch (result.CommandKind)
            {
                case SurvivalCommandKind.HarvestResource:
                    if (result.Accepted)
                    {
                        PlayBlockCue(BlockiverseAudioCue.BlockBreak, position, worldCenter);
                        audioCuePlayer?.PlayCue(BlockiverseAudioCue.PickupItem);
                        vfxCuePlayer?.PlayCue(BlockiverseVfxCue.BlockBreakDust, worldCenter);
                        vfxCuePlayer?.PlayCue(BlockiverseVfxCue.ResourceSpark, worldCenter);
                    }
                    else if (result.HarvestFailureReason == BlockHarvestFailureReason.InsufficientTool ||
                             result.HarvestFailureReason == BlockHarvestFailureReason.InventoryFull ||
                             result.FailureReason == SurvivalCommandFailureReason.InventoryFull)
                    {
                        audioCuePlayer?.PlayCue(BlockiverseAudioCue.ToolWrong);
                        ShowToast(DescribeHarvestRejection(result));
                    }
                    break;

                case SurvivalCommandKind.PlaceBlock:
                    if (result.Accepted)
                    {
                        PlayBlockCue(BlockiverseAudioCue.BlockPlace, position, worldCenter);
                        vfxCuePlayer?.PlayCue(BlockiverseVfxCue.BlockPlacePuff, worldCenter);
                    }
                    break;

                case SurvivalCommandKind.StripLog:
                case SurvivalCommandKind.TillSoil:
                case SurvivalCommandKind.PlantSeed:
                    if (result.Accepted)
                    {
                        audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.ToolHitSoft, worldCenter);
                        vfxCuePlayer?.PlayCue(BlockiverseVfxCue.BlockChipBurst, worldCenter);
                    }
                    break;

                case SurvivalCommandKind.UseConsumable:
                    if (result.Accepted)
                        audioCuePlayer?.PlayCue(ConsumeCueFor(result.Item.ItemId));
                    break;
            }
        }

        // Plays a block cue using the family of the block actually at that position.
        // The harvest case reads the world AFTER the mutation, so an emptied cell
        // reads as Air; the generic cue is the right answer there rather than
        // guessing at what used to be present.
        void SubscribeWorld()
        {
            VoxelWorld world = worldManager != null ? worldManager.World : null;
            if (ReferenceEquals(world, subscribedWorld))
                return;

            UnsubscribeWorld();

            if (world == null)
                return;

            world.BlockChanged += OnWorldBlockChanged;
            subscribedWorld = world;
        }

        void UnsubscribeWorld()
        {
            if (subscribedWorld != null)
                subscribedWorld.BlockChanged -= OnWorldBlockChanged;

            subscribedWorld = null;
            ForgetRemovedBlocks();
        }

        void OnWorldBlockChanged(BlockChange change)
        {
            if (change.NewBlock != BlockRegistry.Air || change.PreviousBlock == BlockRegistry.Air)
                return;

            // Overwrite in place when this position is already remembered, so repeated breaks at
            // one spot cannot crowd out the rest of the ring.
            for (int index = 0; index < removedBlocks.Length; index++)
            {
                if (removedBlockUsed[index] && removedBlocks[index].position.Equals(change.Position))
                {
                    removedBlocks[index] = (change.Position, change.PreviousBlock);
                    return;
                }
            }

            removedBlocks[nextRemovalSlot] = (change.Position, change.PreviousBlock);
            removedBlockUsed[nextRemovalSlot] = true;
            nextRemovalSlot = (nextRemovalSlot + 1) % MaxRememberedRemovals;
        }

        bool TryTakeRemovedBlock(BlockPosition position, out BlockId block)
        {
            for (int index = 0; index < removedBlocks.Length; index++)
            {
                if (!removedBlockUsed[index] || !removedBlocks[index].position.Equals(position))
                    continue;

                block = removedBlocks[index].block;
                removedBlockUsed[index] = false;
                return true;
            }

            block = default;
            return false;
        }

        void ForgetRemovedBlocks()
        {
            for (int index = 0; index < removedBlockUsed.Length; index++)
                removedBlockUsed[index] = false;

            nextRemovalSlot = 0;
        }

        // Resolves the material a cue should sound like. For a break the cell is already air by the
        // time feedback arrives, so the removal record answers it; for a placement the cell holds
        // the new block and reading the world is right. On a client the two orderings both work:
        // if the delta has landed the record is there, and if it has not the world still holds the
        // old block.
        bool TryResolveCueBlock(BlockiverseAudioCue cue, BlockPosition position, out BlockId block)
        {
            if (cue == BlockiverseAudioCue.BlockBreak && TryTakeRemovedBlock(position, out block))
                return true;

            VoxelWorld world = worldManager != null ? worldManager.World : null;
            if (world != null && world.Bounds.Contains(position))
            {
                block = world.GetBlock(position);
                return block != BlockRegistry.Air;
            }

            block = default;
            return false;
        }

        void PlayBlockCue(BlockiverseAudioCue cue, BlockPosition position, Vector3 worldCenter)
        {
            if (audioCuePlayer == null)
                return;

            if (TryResolveCueBlock(cue, position, out BlockId block))
            {
                audioCuePlayer.PlayMaterialCueAt(
                    cue,
                    BlockiverseBlockFeedbackCues.FamilyForBlock(BlockRegistry.Default, block),
                    worldCenter);
                return;
            }

            audioCuePlayer.PlayCueAt(cue, worldCenter);
        }

        // Scooping straight from a river or lake — the ruleset's deferred
        // `water_scoop` cue, which had no implementation at all before this.
        void OnWorldDrinkTaken()
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.WaterScoop);
        }

        // Drinks get the drink cue, everything else edible gets the eat cue.
        static BlockiverseAudioCue ConsumeCueFor(ItemId itemId)
        {
            return itemId == ItemId.CleanWaterFlask
                ? BlockiverseAudioCue.Drink
                : BlockiverseAudioCue.Eat;
        }

        // Structure-loot grant: a broken crate dumped its contents into the player (§3 loot loop).
        void OnContainerLooted(BlockPosition position)
        {
            Vector3 worldCenter = new(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);
            audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.ContainerOpen, worldCenter);
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.PickupItem);
        }

        // The session raises these only for remote peers whose presence actually changed, so a
        // refused join, a duplicate host-side notification, and the peers already in the world
        // when we arrived all stay silent. Both cues reach client seats as well as the host.
        void OnClientConnected(ulong clientId)
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.MultiplayerJoin);
            ShowToast(SubtitleKeys.PlayerJoined);
        }

        void OnClientDisconnected(ulong clientId)
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.MultiplayerLeave);
            ShowToast(SubtitleKeys.PlayerLeft);
        }

        // Takes a table KEY, not a string. Every subtitle this bridge produced was hard-coded
        // English until 2026-08-25 — four of them — because Blockiverse.Gameplay had no
        // localization access at all, so there was nowhere to put a key even if someone had wanted
        // to. The package reference was added alongside this change; it breaks no project layering,
        // since this assembly already references TextMeshPro and uGUI.
        void ShowToast(string key)
        {
            toastPanel?.ShowToast(Localize(key));
        }

        // Deliberately mirrors UiText rather than calling it: UiText lives in
        // Blockiverse.UI.Toolkit, which sits ABOVE this assembly, and referencing it from gameplay
        // code would invert the layering to save fifteen lines.
        //
        // The proper fix is to move subtitle DISPLAY out of Blockiverse.Gameplay entirely — the
        // bridge would raise an event and a UI-layer screen would render it, the way every other
        // player-facing string already works. That is a refactor of the subtitle plumbing rather
        // than a localization fix, so it is recorded here instead of attempted in passing.
        static UnityEngine.Localization.Tables.StringTable cachedSubtitleTable;
        static bool subtitleLocaleHookInstalled;

        static string Localize(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            UnityEngine.Localization.Tables.StringTable table = ResolveSubtitleTable();
            string localized = table?.GetEntry(key)?.GetLocalizedString();

            // Falls back to the key rather than throwing. A missing entry then shows as the raw
            // key, which is loud enough to notice and harmless enough to ship past.
            return string.IsNullOrEmpty(localized) ? key : localized;
        }

        // The bare GetLocalizedString(table, key) this used to call resolves only once a locale
        // SELECTION PASS has run, and that pass runs only in Play mode — so every context outside
        // it (EditMode tests, editor tooling) got the raw key back and the fallback above quietly
        // made that look intentional. UiText documents both requirements; this now actually
        // mirrors them instead of only claiming to.
        static UnityEngine.Localization.Tables.StringTable ResolveSubtitleTable()
        {
            if (cachedSubtitleTable != null)
                return cachedSubtitleTable;

            LocalizationSettings.InitializationOperation.WaitForCompletion();

            UnityEngine.Localization.Locale locale =
                LocalizationSettings.SelectedLocale
                ?? LocalizationSettings.ProjectLocale
                ?? LocalizationSettings.AvailableLocales?.GetLocale("en");

            if (locale == null)
                return null;

            cachedSubtitleTable = LocalizationSettings.StringDatabase
                .GetTableAsync(SubtitleKeys.TableName, locale)
                .WaitForCompletion();

            // Without this the cache outlives the locale and subtitles keep speaking the old
            // language after a switch, which is worse than the un-cached resolve it replaced.
            if (!subtitleLocaleHookInstalled)
            {
                LocalizationSettings.SelectedLocaleChanged += _ => cachedSubtitleTable = null;
                subtitleLocaleHookInstalled = true;
            }

            return cachedSubtitleTable;
        }

        static string DescribeHarvestRejection(SurvivalCommandResult result)
        {
            if (result.HarvestFailureReason == BlockHarvestFailureReason.InventoryFull ||
                result.FailureReason == SurvivalCommandFailureReason.InventoryFull)
            {
                return SubtitleKeys.InventoryFull;
            }

            return SubtitleKeys.ToolTooWeak;
        }

        // The harvest keys are the SAME entries the gameplay HUD's status toast uses, so the
        // subtitle and the on-screen message cannot drift apart in wording.
        static class SubtitleKeys
        {
            public const string TableName = "UI";
            public const string InventoryFull = "ui.status.survival.inventory_full";
            public const string ToolTooWeak = "ui.status.survival.tool_too_weak";
            public const string PlayerJoined = "ui.status.multiplayer.player_joined";
            public const string PlayerLeft = "ui.status.multiplayer.player_left";
        }

        void OnLocalPlayerDamaged(HealthChangeResult result)
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.PlayerHurt);
        }

        void OnLocalPlayerLowHealth(HealthChangeResult result)
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.LowHealth);
        }

        void OnLocalPlayerDied()
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.PlayerDeath);
        }
    }
}
