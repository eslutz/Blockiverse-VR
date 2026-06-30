using System.Collections.Generic;
using System.Linq;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.WorldGen;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Tests.EditMode
{
    public sealed class ActionMenuEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            BlockiverseLocalization.ClearOverridesForTesting();

            foreach (GameObject target in objectsToDestroy)
                if (target != null)
                    Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
        }

        [Test]
        public void ActionMenuSetsTitleAndLabelsAndEmitsActionOnClick()
        {
            BlockiverseActionMenu menu = CreateComponent<BlockiverseActionMenu>("PauseMenu");
            Button[] buttons = CreateButtons(7);
            TMP_Text[] labels = CreateTexts(7);
            TMP_Text title = CreateText("Title");
            menu.Configure(title, buttons, labels);

            menu.SetMenu(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitlePaused),
                MenuActions.PauseMenu(canToggleMode: true, canOpenCreativeTools: true, canQuit: true));

            Assert.That(title.text, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitlePaused)));
            Assert.That(labels[0].text, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.PauseResume)));
            Assert.That(labels[2].text, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.PauseToggleMode)));
            Assert.That(labels[3].text, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.PauseCreativeTools)));
            Assert.That(labels[5].text, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.PauseReturnToTitle)));

            string invoked = null;
            menu.ActionInvoked += id => invoked = id;

            buttons[0].onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.PauseResume));

            buttons[2].onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.PauseToggleMode));

            buttons[5].onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.PauseReturnToTitle));
        }

        [Test]
        public void ActionMenuHidesSurplusButtonsAndIgnoresTheirClicks()
        {
            BlockiverseActionMenu menu = CreateComponent<BlockiverseActionMenu>("Menu");
            Button[] buttons = CreateButtons(6);
            TMP_Text[] labels = CreateTexts(6);
            menu.Configure(CreateText("Title"), buttons, labels);

            // Confirmation dialog only uses two buttons.
            menu.SetMenu(
                BlockiverseLocalization.Format(BlockiverseLocalization.Keys.WorldDetailsDeletePrompt, "Test World"),
                MenuActions.Confirm(
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonDelete),
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonCancel)));

            Assert.That(buttons[0].gameObject.activeSelf, Is.True);
            Assert.That(buttons[1].gameObject.activeSelf, Is.True);
            for (int i = 2; i < buttons.Length; i++)
                Assert.That(buttons[i].gameObject.activeSelf, Is.False, $"Surplus button {i} should be hidden.");

            int invocations = 0;
            menu.ActionInvoked += _ => invocations++;
            buttons[3].onClick.Invoke(); // hidden / no action
            Assert.That(invocations, Is.EqualTo(0));

            buttons[0].onClick.Invoke();
            Assert.That(invocations, Is.EqualTo(1));
        }

        [Test]
        public void TitleMenuFiltersActionsByAvailability()
        {
            IReadOnlyList<MenuAction> minimal = MenuActions.Title(hasLatestSave: false, hasAnySave: false, canQuit: false);
            string[] minimalIds = minimal.Select(a => a.ActionId).ToArray();
            Assert.That(minimalIds, Does.Not.Contain(MenuActions.TitleContinue));
            Assert.That(minimalIds, Does.Not.Contain(MenuActions.TitleLoadWorld));
            Assert.That(minimalIds, Does.Not.Contain(MenuActions.TitleQuit));
            Assert.That(minimalIds, Does.Contain(MenuActions.TitleNewWorld));

            IReadOnlyList<MenuAction> full = MenuActions.Title(hasLatestSave: true, hasAnySave: true, canQuit: true);
            string[] fullIds = full.Select(a => a.ActionId).ToArray();
            Assert.That(fullIds[0], Is.EqualTo(MenuActions.TitleContinue));
            Assert.That(fullIds, Does.Contain(MenuActions.TitleLoadWorld));
            Assert.That(fullIds, Does.Contain(MenuActions.TitleQuit));
        }

        [Test]
        public void PauseMenuFiltersCreativeActionsByPermission()
        {
            string[] survivalIds = MenuActions.PauseMenu(canToggleMode: false, canOpenCreativeTools: false, canQuit: false)
                .Select(a => a.ActionId)
                .ToArray();

            Assert.That(survivalIds, Does.Contain(MenuActions.PauseResume));
            Assert.That(survivalIds, Does.Contain(MenuActions.PauseSaveGame));
            Assert.That(survivalIds, Does.Not.Contain(MenuActions.PauseToggleMode));
            Assert.That(survivalIds, Does.Not.Contain(MenuActions.PauseCreativeTools));
            Assert.That(survivalIds, Does.Not.Contain(MenuActions.PauseQuit));

            string[] creativeIds = MenuActions.PauseMenu(canToggleMode: true, canOpenCreativeTools: true, canQuit: true)
                .Select(a => a.ActionId)
                .ToArray();

            Assert.That(creativeIds, Does.Contain(MenuActions.PauseToggleMode));
            Assert.That(creativeIds, Does.Contain(MenuActions.PauseCreativeTools));
            Assert.That(creativeIds, Does.Contain(MenuActions.PauseQuit));
        }

        [Test]
        public void DeathMenuOmitsBedrollWhenNoSpawnIsSet()
        {
            string[] withoutBedroll = MenuActions.Death(hasBedrollSpawn: false).Select(a => a.ActionId).ToArray();
            Assert.That(withoutBedroll, Does.Not.Contain(MenuActions.DeathRespawnBedroll));
            Assert.That(withoutBedroll[0], Is.EqualTo(MenuActions.DeathRespawnWorldSpawn));

            string[] withBedroll = MenuActions.Death(hasBedrollSpawn: true).Select(a => a.ActionId).ToArray();
            Assert.That(withBedroll[0], Is.EqualTo(MenuActions.DeathRespawnBedroll));
        }

        [Test]
        public void ConfirmDialogUsesProvidedLabels()
        {
            IReadOnlyList<MenuAction> confirm = MenuActions.Confirm("Accept generated save", "Keep generated save");
            Assert.That(confirm[0].ActionId, Is.EqualTo(MenuActions.ConfirmAccept));
            Assert.That(confirm[0].Label, Is.EqualTo("Accept generated save"));
            Assert.That(confirm[1].ActionId, Is.EqualTo(MenuActions.ConfirmCancel));
            Assert.That(confirm[1].Label, Is.EqualTo("Keep generated save"));
        }

        [Test]
        public void BuiltInMenuLabelsResolveThroughLocalizationKeys()
        {
            BlockiverseLocalization.SetOverrideForTesting(BlockiverseLocalization.Keys.PauseResume, "Continuar");
            BlockiverseLocalization.SetOverrideForTesting(BlockiverseLocalization.Keys.PauseToggleMode, "Cambiar modo");

            BlockiverseActionMenu menu = CreateComponent<BlockiverseActionMenu>("PauseMenu");
            Button[] buttons = CreateButtons(7);
            TMP_Text[] labels = CreateTexts(7);
            menu.Configure(CreateText("Title"), buttons, labels);

            menu.SetMenu(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitlePaused),
                MenuActions.PauseMenu(canToggleMode: true, canOpenCreativeTools: true, canQuit: true));

            Assert.That(labels[0].text, Is.EqualTo("Continuar"));
            Assert.That(labels[2].text, Is.EqualTo("Cambiar modo"));
            Assert.That(labels[3].text, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.PauseCreativeTools)));
        }

        [Test]
        public void LocalizedTextBindingAppliesKeyOverrides()
        {
            BlockiverseLocalization.SetOverrideForTesting(BlockiverseLocalization.Keys.TitleSettings, "Ajustes");

            TMP_Text label = CreateText("Generated Label");
            BlockiverseLocalizedText binding = label.gameObject.AddComponent<BlockiverseLocalizedText>();
            binding.Configure(BlockiverseLocalization.Keys.TitleSettings, "Settings");

            Assert.That(label.text, Is.EqualTo("Ajustes"));

            BlockiverseLocalization.ClearOverridesForTesting();
            binding.RefreshText();

            Assert.That(label.text, Is.EqualTo("Settings"));
        }

        [Test]
        public void RuntimeDisplayNamesAvoidRawCanonicalIdsAndEnumMembers()
        {
            Assert.That(
                BlockiverseLocalization.DisplayNameForCanonicalId("survival_terrain"),
                Is.EqualTo("Survival Terrain"));
            Assert.That(
                BlockiverseLocalization.DisplayName(CreativeCatalogCategory.DeepRock),
                Is.EqualTo("Deep Rock"));
            Assert.That(
                BlockiverseLocalization.DisplayName(CraftingStation.ClayKiln),
                Is.EqualTo("Clay Kiln"));
            Assert.That(
                BlockiverseLocalization.DisplayName(SurvivalCommandFailureReason.NotAStation),
                Is.EqualTo("Not a Station"));
            Assert.That(
                BlockiverseLocalization.DisplayName(WeatherState.HeavyRain),
                Is.EqualTo("Heavy Rain"));
        }

        [Test]
        public void RuntimeDisplayNamesUseGeneratedOverrideKeys()
        {
            BlockiverseLocalization.SetOverrideForTesting("ui.value.canonical.survival_terrain", "Terrain de survie");
            BlockiverseLocalization.SetOverrideForTesting("ui.value.survival_command_failure.not_a_station", "Station indisponible");

            Assert.That(
                BlockiverseLocalization.DisplayNameForCanonicalId("survival_terrain"),
                Is.EqualTo("Terrain de survie"));
            Assert.That(
                BlockiverseLocalization.DisplayName(SurvivalCommandFailureReason.NotAStation),
                Is.EqualTo("Station indisponible"));
        }

        [Test]
        public void ErrorMenuHasStableIdAndLocalizedLabel()
        {
            IReadOnlyList<MenuAction> error = MenuActions.Error();
            Assert.That(error, Has.Count.EqualTo(1));
            Assert.That(error[0].ActionId, Is.EqualTo(MenuActions.ErrorClose));
            Assert.That(error[0].Label, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ErrorClose)));
        }

        [Test]
        public void LanMultiplayerMenuIncludesReconnectOption()
        {
            IReadOnlyList<MenuAction> withoutReconnect = MenuActions.LanMultiplayer(canReconnect: false);
            Assert.That(withoutReconnect.Select(a => a.ActionId), Does.Not.Contain(MenuActions.LanReconnect));

            IReadOnlyList<MenuAction> withReconnect = MenuActions.LanMultiplayer(canReconnect: true);
            Assert.That(withReconnect.Select(a => a.ActionId), Contains.Item(MenuActions.LanReconnect));

            MenuAction reconnect = withReconnect.First(a => a.ActionId == MenuActions.LanReconnect);
            Assert.That(reconnect.Label, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanReconnect)));
        }

        T CreateComponent<T>(string name) where T : Component
{
            var gameObject = new GameObject(name);
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        Button[] CreateButtons(int count)
        {
            var buttons = new Button[count];
            for (int i = 0; i < count; i++)
                buttons[i] = CreateComponent<Button>($"Button{i}");
            return buttons;
        }

        TMP_Text[] CreateTexts(int count)
        {
            var labels = new TMP_Text[count];
            for (int i = 0; i < count; i++)
                labels[i] = CreateText($"Text{i}");
            return labels;
        }

        TMP_Text CreateText(string name) => CreateComponent<TextMeshProUGUI>(name);
    }
}
