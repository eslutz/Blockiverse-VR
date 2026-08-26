using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // Settings > Textures. Lists the four built-in sets and any installed packs, and applies a
    // choice to the LIVE world -- no reload, no chunk re-mesh.
    //
    // Why a screen rather than another row on the New World cycler, which would have been cheaper:
    // the New World screen is reachable only while creating a world, and a texture choice has to be
    // changeable afterwards. A `< >` cycler also has room for one label and nothing else, and this
    // list has to show author, version and ATTRIBUTION -- which is the wrong thing to omit when the
    // art belongs to someone else. Settings is reachable from both the title and pause menus, so
    // one screen serves "pick before playing" and "change while playing".
    [UiToolkitScreen(
        MenuActions.TexturesSettingsScreen,
        "Assets/Blockiverse/UI/Documents/TexturesSettingsScreen.uxml",
        800, 1428, UiToolkitPlacementProfile.Menu)]
    public sealed class TexturesSettingsScreenController : UiToolkitScreenController
    {
        Label titleLabel;
        Label builtInHeading;
        Label installedHeading;
        VisualElement builtInList;
        VisualElement packList;
        Label emptyLabel;
        Button refreshButton;
        Button closeButton;

        // Same dynamic namespace the New World selector uses for these four ids.
        const string CanonicalValueKeyPrefix = "ui.value.canonical.";

        readonly List<Button> rowButtons = new();

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        public override string ScreenId => MenuActions.TexturesSettingsScreen;

        /// <summary>The token currently applied. Read from the preference so the screen reflects
        /// what a fresh world or a multiplayer join would use.</summary>
        public string SelectedToken => BlockiverseTexturePackPreferences.Token;

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            titleLabel = Require<Label>(root, "bv-textures-title", ref allFound);
            builtInHeading = Require<Label>(root, "bv-textures-built-in-heading", ref allFound);
            installedHeading = Require<Label>(root, "bv-textures-installed-heading", ref allFound);
            builtInList = Require<VisualElement>(root, "bv-textures-built-in-list", ref allFound);
            packList = Require<VisualElement>(root, "bv-textures-pack-list", ref allFound);
            emptyLabel = Require<Label>(root, "bv-textures-empty", ref allFound);
            refreshButton = Require<Button>(root, "bv-textures-refresh", ref allFound);
            closeButton = Require<Button>(root, "bv-textures-close", ref allFound);

            ApplyStaticLabels();
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            if (refreshButton != null)
                refreshButton.clicked += OnRefreshClicked;

            if (closeButton != null)
                closeButton.clicked += RequestClose;
        }

        protected override void OnUnregisterCallbacks()
        {
            if (refreshButton != null)
                refreshButton.clicked -= OnRefreshClicked;

            if (closeButton != null)
                closeButton.clicked -= RequestClose;

            ClearRows();
        }

        // Labels are written here rather than bound in UXML because these ui.generated.textures.*
        // entries are not in the "UI" table yet. A LocalizedString binding renders EMPTY for a
        // missing entry, while UiText.Get falls back to the key string -- the same reason
        // AudioSettingsScreenController writes its toggle labels in code.
        void ApplyStaticLabels()
        {
            if (titleLabel != null)
                titleLabel.text = UiText.Get(BlockiverseLocalization.Keys.TexturesSettingsTitle);

            if (builtInHeading != null)
                builtInHeading.text = UiText.Get(BlockiverseLocalization.Keys.TexturesBuiltInHeading);

            if (installedHeading != null)
                installedHeading.text = UiText.Get(BlockiverseLocalization.Keys.TexturesInstalledHeading);

            if (emptyLabel != null)
                emptyLabel.text = UiText.Get(BlockiverseLocalization.Keys.TexturesNoPacksInstalled);

            if (refreshButton != null)
                refreshButton.text = UiText.Get(BlockiverseLocalization.Keys.TexturesRefresh);

            if (closeButton != null)
                closeButton.text = UiText.Get(BlockiverseLocalization.Keys.TexturesClose);
        }

        protected override void OnDetach()
        {
            titleLabel = null;
            builtInHeading = null;
            installedHeading = null;
            builtInList = null;
            packList = null;
            emptyLabel = null;
            refreshButton = null;
            closeButton = null;
        }

        // Rebuilt on show as well as on Refresh: a player may have sideloaded a pack while the
        // screen was hidden, and the list is cheap enough that a rescan costs nothing noticeable.
        protected override void OnShown() => Rebuild();

        public void RequestClose()
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            DispatchAction(MenuActions.TexturesSettingsClose);
        }

        void OnRefreshClicked()
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            Rebuild();
        }

        /// <summary>Rescans the pack directory and rebuilds both lists.</summary>
        public void Rebuild()
        {
            if (builtInList == null || packList == null)
                return;

            ClearRows();
            BlockiverseTexturePackLibrary.EnsurePackRootExists();

            string selected = SelectedToken;

            foreach (string id in BlockTextureSetIds.MenuOptions)
            {
                AddRow(
                    builtInList,
                    id,
                    UiText.Get(CanonicalValueKeyPrefix + id),
                    byline: null);
            }

            IReadOnlyList<BlockiverseTexturePackInfo> installed = BlockiverseTexturePackLibrary.Installed();

            foreach (BlockiverseTexturePackInfo pack in installed)
                AddRow(packList, pack.Token, pack.Manifest.displayName, BuildByline(pack.Manifest));

            if (emptyLabel != null)
                emptyLabel.style.display = installed.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            MarkSelected(selected);
        }

        // Author, version, tile size and licence on one line. Composed from sanitised manifest
        // fields and assigned as PLAIN TEXT -- never resolved through the localization table, so a
        // pack whose author is literally "ui.status.crate.shared" renders as that string.
        static string BuildByline(BlockiverseTexturePackManifest manifest)
        {
            var parts = new List<string>(4);

            if (!string.IsNullOrEmpty(manifest.author))
                parts.Add(manifest.author);

            if (!string.IsNullOrEmpty(manifest.packVersion))
                parts.Add("v" + manifest.packVersion);

            parts.Add(manifest.tilePixels + "px");

            // Attribution before licence: for third-party art the credit line is the thing that
            // has to be visible, and it is the reason the format asks for it at all.
            if (!string.IsNullOrEmpty(manifest.attribution))
                parts.Add(manifest.attribution);
            else if (!string.IsNullOrEmpty(manifest.license))
                parts.Add(manifest.license);

            return string.Join("  ·  ", parts);
        }

        void AddRow(VisualElement parent, string token, string title, string byline)
        {
            var row = new VisualElement { name = "bv-textures-row-" + token };
            row.AddToClassList("hs-list__row");

            var button = new Button { name = "bv-textures-select-" + token, text = title };
            button.AddToClassList("hs-button");
            button.userData = token;
            button.clicked += () => OnRowSelected(token);
            rowButtons.Add(button);
            row.Add(button);

            if (!string.IsNullOrEmpty(byline))
            {
                var bylineLabel = new Label(byline) { pickingMode = PickingMode.Ignore };
                bylineLabel.AddToClassList("hs-secondary");
                row.Add(bylineLabel);
            }

            parent.Add(row);
        }

        void OnRowSelected(string token)
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);

            // Both, deliberately. The preference is what a NEW world or a multiplayer join will
            // use; the session controller applies it to the world currently loaded and writes it
            // into that world's save. Setting only one would make the choice either forgetful or
            // world-local, and players expect neither.
            // The token travels through the preference rather than an action payload:
            // DispatchAction carries only an id, and the preference is where a new world and a
            // multiplayer join already look. The session controller reads it when it handles the
            // action, so there is one source of truth rather than two that can disagree.
            BlockiverseTexturePackPreferences.Token = token;
            DispatchAction(MenuActions.TexturesSettingsSelect);

            MarkSelected(token);
        }

        void MarkSelected(string token)
        {
            foreach (Button button in rowButtons)
            {
                bool isSelected = string.Equals(button.userData as string, token, System.StringComparison.Ordinal);
                button.EnableInClassList("hs-button--selected", isSelected);
            }
        }

        void ClearRows()
        {
            rowButtons.Clear();
            builtInList?.Clear();
            packList?.Clear();
        }
    }
}
