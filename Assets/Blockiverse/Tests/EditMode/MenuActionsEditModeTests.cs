using System.Collections.Generic;
using System.Linq;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Was ActionMenuEditModeTests. The four tests that built a BlockiverseActionMenu or a
    // BlockiverseLocalizedText went with the uGUI panels; what is left never touched uGUI at
    // all — it is the MenuActions catalogue and the BlockiverseLocalization display-name
    // contract, both of which the UI Toolkit frontend consumes unchanged.
    public sealed class MenuActionsEditModeTests
    {
        [TearDown]
        public void TearDown()
        {
            BlockiverseLocalization.ClearOverridesForTesting();
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

        // The only test anywhere of the ui.value.canonical.* / ui.value.<enum>.* override path.
        // The Toolkit screens each reimplement that prefix+humanize resolution locally rather
        // than calling DisplayName*, so this is what keeps the contract they copied honest.
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
    }
}
