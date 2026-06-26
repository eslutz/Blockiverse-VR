using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Blockiverse.Gameplay
{
    public sealed class CreativeHotbar : MonoBehaviour
    {
        readonly List<BlockId> blockIds = new();

        BlockRegistry registry;
        [SerializeField] BlockiverseHudToolkitSurface hudSurface;
        [SerializeField] Label selectedBlockLabel;
        [SerializeField] GameObject visibilityRoot;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        int selectedIndex;
        bool visible;

        public BlockId SelectedBlockId => blockIds.Count == 0 ? BlockRegistry.Air : blockIds[selectedIndex];
        public IReadOnlyList<BlockId> BlockIds => blockIds;
        public bool IsVisible => hudSurface != null ? visible : visibilityRoot != null && visibilityRoot.activeSelf;
        public UnityEvent SelectionChanged { get; } = new();

        public void EnsureConfigured()
        {
            if (blockIds.Count == 0)
                ConfigureFromDefaultCatalog(selectedBlockLabel);
        }

        public void Configure(BlockRegistry blockRegistry, IEnumerable<BlockId> selectableBlocks, Label selectedLabel = null)
        {
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            selectedBlockLabel = selectedLabel;
            blockIds.Clear();

            foreach (BlockId blockId in selectableBlocks ?? Enumerable.Empty<BlockId>())
            {
                BlockDefinition definition = registry.Get(blockId);

                if (definition.Id != BlockRegistry.Air && definition.IsRenderable)
                    blockIds.Add(definition.Id);
            }

            selectedIndex = 0;
            RefreshLabel();
        }

        public void ConfigureHudSurface(BlockiverseHudToolkitSurface surface)
        {
            hudSurface = surface;
            visibilityRoot = null;
            Hide(playFeedback: false);
        }

        public void ConfigureVisibilityRoot(GameObject root)
        {
            hudSurface = null;
            visibilityRoot = root;
            Hide(playFeedback: false);
        }

        public void ConfigureFeedback(BlockiverseAudioCuePlayer targetAudioCuePlayer)
        {
            audioCuePlayer = targetAudioCuePlayer;
        }

        public void ConfigureDefault(Label selectedLabel = null)
        {
            BlockRegistry defaultRegistry = BlockRegistry.Default;
            Configure(
                defaultRegistry,
                defaultRegistry.All.Where(block => block.Id != BlockRegistry.Air).Select(block => block.Id),
                selectedLabel);
        }

        public void ConfigureFromCatalog(CreativeCatalog catalog, BlockRegistry blockRegistry, Label selectedLabel = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            Configure(
                blockRegistry ?? BlockRegistry.Default,
                catalog.All.Select(entry => entry.BlockId),
                selectedLabel);
        }

        // Configures the hotbar from the default creative catalog. Kept registry-free so callers
        // in assemblies that don't reference Voxel (the editor bootstrapper) can use it.
        public void ConfigureFromDefaultCatalog(Label selectedLabel = null)
        {
            ConfigureFromCatalog(CreativeCatalog.CreateDefault(), null, selectedLabel);
        }

        public void SelectIndex(int index)
        {
            if (blockIds.Count == 0)
                return;

            selectedIndex = Mathf.Clamp(index, 0, blockIds.Count - 1);
            RefreshLabel();
            SelectionChanged.Invoke();
            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        public void SelectNext()
        {
            if (blockIds.Count == 0)
                return;

            SelectIndex((selectedIndex + 1) % blockIds.Count);
        }

        // Selects a specific block (catalog browser / pick-block). False when the block is not
        // in the selectable list.
        public bool SelectBlock(BlockId blockId)
        {
            for (int i = 0; i < blockIds.Count; i++)
            {
                if (blockIds[i] == blockId)
                {
                    SelectIndex(i);
                    return true;
                }
            }

            return false;
        }

        public void ToggleVisible()
        {
            if (IsVisible)
                Hide();
            else
                Show();
        }

        public void Show()
        {
            visible = true;
            if (hudSurface != null)
            {
                hudSurface.SetHotbarVisible(true);
                PlayFeedback(BlockiverseAudioCue.InventoryOpen);
            }
            else if (visibilityRoot != null)
            {
                visibilityRoot.SetActive(true);
                PlayFeedback(BlockiverseAudioCue.InventoryOpen);
            }
        }

        public void Hide()
        {
            Hide(playFeedback: true);
        }

        void Hide(bool playFeedback)
        {
            visible = false;
            if (hudSurface != null)
            {
                hudSurface.SetHotbarVisible(false);
                if (playFeedback)
                    PlayFeedback(BlockiverseAudioCue.InventoryClose);
            }
            else if (visibilityRoot != null)
            {
                visibilityRoot.SetActive(false);
                if (playFeedback)
                    PlayFeedback(BlockiverseAudioCue.InventoryClose);
            }
        }

        void RefreshLabel()
        {
            string label = blockIds.Count == 0
                ? "No block"
                : registry.Get(SelectedBlockId).Name;
            if (selectedBlockLabel != null)
                selectedBlockLabel.text = label;
            hudSurface?.SetSelectedBlock(label);
        }

        void Awake()
        {
            if (hudSurface == null)
                hudSurface = GetComponent<BlockiverseHudToolkitSurface>()
                    ?? GetComponentInChildren<BlockiverseHudToolkitSurface>(true);

            EnsureConfigured();

            Hide(playFeedback: false);
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            DiscoverFeedback();
            audioCuePlayer?.PlayCue(cue);
        }

        void DiscoverFeedback()
        {
            if (!Application.isPlaying)
                return;

            if (audioCuePlayer == null)
                audioCuePlayer = FindAnyObjectByType<BlockiverseAudioCuePlayer>();
        }
    }
}
