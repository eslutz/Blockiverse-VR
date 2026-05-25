using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Blockiverse.Gameplay
{
    public sealed class CreativeHotbar : MonoBehaviour
    {
        readonly List<BlockId> blockIds = new();

        BlockRegistry registry;
        [SerializeField] Text selectedBlockLabel;
        [SerializeField] Canvas targetCanvas;
        int selectedIndex;

        public BlockId SelectedBlockId => blockIds.Count == 0 ? BlockRegistry.Air : blockIds[selectedIndex];
        public IReadOnlyList<BlockId> BlockIds => blockIds;
        public bool IsVisible => targetCanvas != null && targetCanvas.enabled;
        public UnityEvent SelectionChanged { get; } = new();

        public void Configure(BlockRegistry blockRegistry, IEnumerable<BlockId> selectableBlocks, Text selectedLabel)
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

        public void ConfigureCanvas(Canvas canvas)
        {
            targetCanvas = canvas;
            Hide();
        }

        public void ConfigureDefault(Text selectedLabel)
        {
            BlockRegistry defaultRegistry = BlockRegistry.CreateDefault();
            Configure(
                defaultRegistry,
                defaultRegistry.All.Where(block => block.Id != BlockRegistry.Air).Select(block => block.Id),
                selectedLabel);
        }

        public void SelectIndex(int index)
        {
            if (blockIds.Count == 0)
                return;

            selectedIndex = Mathf.Clamp(index, 0, blockIds.Count - 1);
            RefreshLabel();
            SelectionChanged.Invoke();
        }

        public void SelectNext()
        {
            if (blockIds.Count == 0)
                return;

            SelectIndex((selectedIndex + 1) % blockIds.Count);
        }

        public void SelectPrevious()
        {
            if (blockIds.Count == 0)
                return;

            SelectIndex((selectedIndex - 1 + blockIds.Count) % blockIds.Count);
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
            if (targetCanvas != null)
                targetCanvas.enabled = true;
        }

        public void Hide()
        {
            if (targetCanvas != null)
                targetCanvas.enabled = false;
        }

        void RefreshLabel()
        {
            if (selectedBlockLabel == null)
                return;

            selectedBlockLabel.text = blockIds.Count == 0
                ? "No block"
                : registry.Get(SelectedBlockId).Name;
        }

        void Awake()
        {
            if (targetCanvas == null)
                targetCanvas = GetComponent<Canvas>();

            if (registry == null)
                ConfigureDefault(selectedBlockLabel);

            Hide();
        }
    }
}
