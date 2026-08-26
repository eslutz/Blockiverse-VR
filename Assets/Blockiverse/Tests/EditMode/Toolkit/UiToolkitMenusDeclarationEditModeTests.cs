using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blockiverse.Editor;
using Blockiverse.UI;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // The [UiToolkitScreen] declaration table is the whole registration mechanism for the
    // UI Toolkit menus (ADR 0010): the bootstrapper generates one panel per declaration and
    // the host indexes controllers by screen id. These tests catch the cross-screen
    // integration failures no single screen's own tests can see — a missing document file, a
    // typo'd screen id, two screens claiming one document.
    public sealed class UiToolkitMenusDeclarationEditModeTests
    {
        static List<BlockiverseProjectBootstrapper.UiToolkitScreenDeclaration> Declarations =>
            BlockiverseProjectBootstrapper.EnumerateUiToolkitScreenDeclarations();

        static readonly HashSet<string> KnownScreenIds = new(System.StringComparer.Ordinal)
        {
            MenuActions.TitleScreen, MenuActions.NewWorldScreen, MenuActions.LoadWorldScreen,
            MenuActions.WorldDetailsScreen, MenuActions.WorldLoadingScreen,
            MenuActions.ControllerMappingScreen, MenuActions.GameplayHudScreen,
            MenuActions.PauseScreen, MenuActions.SettingsScreen, MenuActions.ComfortSettingsScreen,
            MenuActions.AudioSettingsScreen, MenuActions.ControlsScreen,
            MenuActions.CreativeToolsScreen, MenuActions.DeathScreen, MenuActions.LanMultiplayerScreen,
            MenuActions.StationMenuScreen, MenuActions.ConfirmModal, MenuActions.ErrorModal,
            MenuActions.InventoryScreen, MenuActions.CraftingScreen, MenuActions.CatalogScreen,
            MenuActions.StationCrateScreen,
            // The pause menu's single "Screens" row opens this hub, which carries the guaranteed
            // route into inventory, crafting, the shared crate and the block catalog now that the
            // action bar lives on the support wrist.
            MenuActions.GameplayScreensScreen,
        };

        // Positive control for the whole suite: with zero declarations every other test here
        // passes by vacuum. The migration matrix declares 25 documents across 23 screen ids
        // (CreativeHotbarController, the Creative quick block menu, was retired 2026-08-26: it
        // duplicated the catalog screen already reachable from the wrist menu and the support
        // grip's screens hub).
        [Test]
        public void TheFullScreenCatalogIsDeclared()
        {
            List<BlockiverseProjectBootstrapper.UiToolkitScreenDeclaration> declarations = Declarations;

            Assert.That(declarations.Count, Is.GreaterThanOrEqualTo(23),
                "The screen catalog lost declarations — a controller class or its attribute went missing.");

            var declaredIds = new HashSet<string>(
                declarations.Select(d => d.Attribute.ScreenId), System.StringComparer.Ordinal);

            Assert.That(declaredIds, Is.SupersetOf(KnownScreenIds),
                "Every MenuActions screen id must have a UI Toolkit screen declaration.");
        }

        [Test]
        public void EveryDeclaredDocumentExistsOnDisk()
        {
            var missing = Declarations
                .Where(d => !File.Exists(d.Attribute.DocumentAssetPath))
                .Select(d => $"{d.ControllerType.Name}: {d.Attribute.DocumentAssetPath}")
                .ToList();

            Assert.That(missing, Is.Empty,
                "Declared UXML documents missing from disk — the screen would render blank:\n" +
                string.Join("\n", missing));
        }

        [Test]
        public void EveryScreenIdIsACanonicalMenuActionsId()
        {
            var unknown = Declarations
                .Where(d => !KnownScreenIds.Contains(d.Attribute.ScreenId))
                .Select(d => $"{d.ControllerType.Name}: '{d.Attribute.ScreenId}'")
                .ToList();

            Assert.That(unknown, Is.Empty,
                "Screen ids must be MenuActions constants, verbatim (ADR 0010 §4):\n" +
                string.Join("\n", unknown));
        }

        // Several HUD-family panels legitimately share the gameplay_hud id, but two screens
        // claiming one DOCUMENT means one of them renders the wrong tree.
        [Test]
        public void NoTwoScreensShareADocument()
        {
            var duplicates = Declarations
                .GroupBy(d => d.Attribute.DocumentAssetPath, System.StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.That(duplicates, Is.Empty,
                "Documents claimed by more than one screen controller:\n" + string.Join("\n", duplicates));
        }

        // Outside the HUD family, a duplicated screen id would make the host's routed
        // visibility show two panels for one route.
        [Test]
        public void OnlyTheHudFamilySharesAScreenId()
        {
            var duplicated = Declarations
                .GroupBy(d => d.Attribute.ScreenId, System.StringComparer.Ordinal)
                .Where(g => g.Count() > 1 && g.Key != MenuActions.GameplayHudScreen)
                .Select(g => g.Key)
                .ToList();

            Assert.That(duplicated, Is.Empty,
                "Screen ids duplicated outside the HUD family:\n" + string.Join("\n", duplicated));
        }

        [Test]
        public void PanelSizesArePlausiblePhysicalSizes()
        {
            foreach (BlockiverseProjectBootstrapper.UiToolkitScreenDeclaration declaration in Declarations)
            {
                // metres = px / 100 ppu × 0.1 scale = px / 1000. Anything outside 5 cm–2 m is
                // a unit mistake (most likely the uGUI Canvas pixel sizes carried across).
                float widthMeters = declaration.Attribute.WidthPixels / 1000f;
                float heightMeters = declaration.Attribute.HeightPixels / 1000f;

                Assert.That(widthMeters, Is.InRange(0.05f, 2f),
                    $"{declaration.ControllerType.Name} width {widthMeters}m is not a plausible panel size.");
                Assert.That(heightMeters, Is.InRange(0.05f, 2f),
                    $"{declaration.ControllerType.Name} height {heightMeters}m is not a plausible panel size.");
            }
        }

        [Test]
        public void HudFamilyPanelsUseTheHudProfile()
        {
            foreach (BlockiverseProjectBootstrapper.UiToolkitScreenDeclaration declaration in Declarations)
            {
                if (declaration.Attribute.ScreenId != MenuActions.GameplayHudScreen)
                    continue;

                // Hud OR Wrist. Both are RIG-ANCHORED — the point of this assertion is that a
                // gameplay-route panel must not be world-placed, drifting while its siblings ride
                // the player. The action menu moved to the support wrist on 2026-08-25, which is
                // still rig-anchored, just to the hand rather than the head.
                Assert.That(
                    declaration.Attribute.PlacementProfile,
                    Is.EqualTo(UiToolkitPlacementProfile.Hud)
                        .Or.EqualTo(UiToolkitPlacementProfile.Wrist),
                    $"{declaration.ControllerType.Name} shares the gameplay_hud id but is neither " +
                    "Hud- nor Wrist-profile; it would be world-placed while its siblings ride the rig.");
            }
        }

        [Test]
        public void WorldLoadingIsTheOnlyOverlayProfileScreen()
        {
            var overlays = Declarations
                .Where(d => d.Attribute.PlacementProfile == UiToolkitPlacementProfile.Overlay)
                .Select(d => d.Attribute.ScreenId)
                .ToList();

            Assert.That(overlays, Is.EqualTo(new[] { MenuActions.WorldLoadingScreen }),
                "Overlay profile means 'never accepts input'; only the world-loading screen may use it.");
        }
    }
}
