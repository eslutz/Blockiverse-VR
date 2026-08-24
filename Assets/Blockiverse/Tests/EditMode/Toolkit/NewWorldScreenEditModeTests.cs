using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.UI;
using Blockiverse.UI.Toolkit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // Screen-level mirror of NewWorldConfigEditModeTests: the same model behaviours
    // (defaults, cycling/wrap, seed rules, validation) observed through the UI Toolkit
    // screen — element state in the instantiated document, and dispatch through the real
    // BlockiverseMenuController seam. UIDocument builds no tree in EditMode, so the
    // document is instantiated directly and attached via AttachForTest; clicks are driven
    // through the controller's public handler seams (CycleSelector/SubmitCreate/
    // SubmitCancel) because ClickEvent dispatch needs a live panel.
    public sealed class NewWorldScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/NewWorldScreen.uxml";

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        NewWorldScreenController CreateAttachedController(out VisualElement root)
        {
            var gameObject = new GameObject("New World Screen Under Test");
            objectsToDestroy.Add(gameObject);
            NewWorldScreenController controller = gameObject.AddComponent<NewWorldScreenController>();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {DocumentPath}");

            root = tree.Instantiate();
            controller.AttachForTest(root);
            return controller;
        }

        // Real router + menu controller behind the screen, so dispatch assertions exercise
        // the same path a headset click takes: screen → host → controller.DispatchAction.
        NewWorldScreenController CreateRoutedController(
            out VisualElement root, out BlockiverseMenuController menu, out List<string> forwarded)
        {
            var hostObject = new GameObject("Toolkit Host Under Test");
            objectsToDestroy.Add(hostObject);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            menu = hostObject.AddComponent<BlockiverseMenuController>();
            host.Configure(menu);
            // Initializes the router and replays state pushes, same as the Boot scene does.
            menu.RegisterFrontend(host);

            NewWorldScreenController controller = CreateAttachedController(out root);
            controller.ConfigureHost(host);

            var captured = new List<string>();
            menu.ActionRequested += actionId => captured.Add(actionId);
            forwarded = captured;
            return controller;
        }

        [Test]
        public void AttachBindsEveryElementAndAnEmptyTreeRefusesToBind()
        {
            NewWorldScreenController controller = CreateAttachedController(out _);

            Assert.That(controller.IsBound, Is.True,
                "Every named element in NewWorldScreen.uxml must resolve.");
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1),
                "Attach must leave exactly one callback registration.");

            // Negative control: a controller that returned true from OnAttach without
            // querying anything would also 'pass' the real document — it must fail here.
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var strayObject = new GameObject("Empty Tree Control");
                objectsToDestroy.Add(strayObject);
                NewWorldScreenController stray = strayObject.AddComponent<NewWorldScreenController>();
                stray.AttachForTest(new VisualElement());
                Assert.That(stray.IsBound, Is.False,
                    "An empty tree has none of the screen's elements; IsBound must be false.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [Test]
        public void ResetForNewWorldRendersTheSpecificationDefaults()
        {
            NewWorldScreenController controller = CreateAttachedController(out VisualElement root);

            controller.ResetForNewWorld();

            Assert.That(root.Q<TextField>("bv-name-field").value, Is.EqualTo("New World"));
            string seedText = root.Q<TextField>("bv-seed-field").value;
            Assert.That(ulong.TryParse(seedText, out _), Is.True,
                "ResetForNewWorld randomizes a numeric seed into the field.");
            Assert.That(controller.Config.SeedText, Is.EqualTo(seedText));

            Assert.That(controller.Config.GameMode, Is.EqualTo("survival"));
            Assert.That(controller.Config.Difficulty, Is.EqualTo("normal"));
            Assert.That(controller.Config.WorldSize, Is.EqualTo("small"));
            Assert.That(controller.Config.WorldPreset, Is.EqualTo(WorldPresetIds.SurvivalTerrain));
            Assert.That(controller.Config.StartingBiome, Is.EqualTo("balanced"));

            // Rows whose canonical-value entries already exist assert the authored English;
            // the rest assert the UiText resolution so the test tracks central key additions.
            Assert.That(root.Q<Label>("bv-world-size-value").text, Is.EqualTo("Small (128x128)"));
            Assert.That(root.Q<Label>("bv-texture-set-value").text, Is.EqualTo("Enhanced"));
            string gameModeText = root.Q<Label>("bv-game-mode-value").text;
            Assert.That(gameModeText, Is.EqualTo(UiText.Get("ui.value.canonical.survival")));
            Assert.That(gameModeText, Is.Not.Empty,
                "A do-nothing controller would leave the value label blank.");
        }

        [Test]
        public void SelectorsCycleThroughOptionsWrapAndUpdateTheirValueLabels()
        {
            NewWorldScreenController controller = CreateAttachedController(out VisualElement root);
            controller.ResetForNewWorld();

            string survivalText = root.Q<Label>("bv-game-mode-value").text;
            controller.CycleSelector(0, forward: true);
            Assert.That(controller.Config.GameMode, Is.EqualTo("creative"));
            Assert.That(root.Q<Label>("bv-game-mode-value").text, Is.Not.EqualTo(survivalText),
                "Cycling must re-render the row's value label.");
            controller.CycleSelector(0, forward: true);
            Assert.That(controller.Config.GameMode, Is.EqualTo("survival"),
                "Game mode should wrap back to survival.");
            Assert.That(root.Q<Label>("bv-game-mode-value").text, Is.EqualTo(survivalText));

            controller.CycleSelector(1, forward: false);
            Assert.That(controller.Config.Difficulty, Is.EqualTo("easy"),
                "Back-cycling from normal lands on easy.");

            controller.CycleSelector(2, forward: true);
            Assert.That(controller.Config.WorldSize, Is.EqualTo("medium"));
            Assert.That(root.Q<Label>("bv-world-size-value").text, Is.EqualTo("Medium (192x192)"));
            controller.CycleSelector(2, forward: true);
            Assert.That(controller.Config.WorldSize, Is.EqualTo("small"),
                "World size should wrap small→medium→small.");
            Assert.That(root.Q<Label>("bv-world-size-value").text, Is.EqualTo("Small (128x128)"));

            controller.CycleSelector(3, forward: true);
            Assert.That(controller.Config.WorldPreset, Is.EqualTo(WorldPresetIds.FlatBuilder));

            controller.CycleSelector(4, forward: true);
            Assert.That(controller.Config.StartingBiome, Is.EqualTo("meadow"));

            controller.CycleSelector(5, forward: true);
            Assert.That(controller.Config.TextureSet, Is.EqualTo("ai_simplified"));
            Assert.That(root.Q<Label>("bv-texture-set-value").text, Is.EqualTo("AI Simplified"));
        }

        [Test]
        public void NameAndSeedFieldTextReachesTheConfig()
        {
            NewWorldScreenController controller = CreateAttachedController(out VisualElement root);
            controller.ResetForNewWorld();

            root.Q<TextField>("bv-name-field").value = "Meadow Home";
            root.Q<TextField>("bv-seed-field").value = "12345";

            Assert.That(controller.Config.Name, Is.EqualTo("Meadow Home"));
            Assert.That(controller.Config.SeedText, Is.EqualTo("12345"));
            Assert.That(controller.Config.Seed, Is.EqualTo(12345UL),
                "Numeric seed text passes through unhashed.");

            root.Q<TextField>("bv-seed-field").value = "meadow-home";
            Assert.That(controller.Config.Seed, Is.EqualTo(NewWorldConfig.HashSeed("meadow-home")),
                "Text seeds hash deterministically through the same model rule.");
        }

        [Test]
        public void ResetRestoresDefaultsAndClearsTheStatusAfterEdits()
        {
            NewWorldScreenController controller = CreateAttachedController(out VisualElement root);
            controller.ResetForNewWorld();

            controller.CycleSelector(0, forward: true);
            root.Q<TextField>("bv-name-field").value = "   ";
            root.Q<TextField>("bv-seed-field").value = "abc";
            controller.SubmitCreate();
            Assert.That(root.Q<Label>("bv-status").text, Is.Not.Empty,
                "Invalid create must surface the model's error before the reset under test.");

            controller.ResetForNewWorld();

            Assert.That(root.Q<TextField>("bv-name-field").value, Is.EqualTo("New World"));
            Assert.That(controller.Config.GameMode, Is.EqualTo("survival"));
            Assert.That(controller.Config.Difficulty, Is.EqualTo("normal"));
            Assert.That(ulong.TryParse(root.Q<TextField>("bv-seed-field").value, out _), Is.True);
            Assert.That(root.Q<Label>("bv-status").text, Is.Empty,
                "Reset clears a stale validation message.");
        }

        [Test]
        public void CreateWithAValidConfigDispatchesTheCreateAction()
        {
            NewWorldScreenController controller = CreateRoutedController(
                out VisualElement root, out _, out List<string> forwarded);
            controller.ResetForNewWorld();
            root.Q<TextField>("bv-name-field").value = "Meadow Home";

            controller.SubmitCreate();

            Assert.That(forwarded, Is.EqualTo(new[] { MenuActions.NewWorldCreate }),
                "A valid config dispatches exactly new_world.create.");
            Assert.That(controller.Config.IsValid(out _), Is.True,
                "The config the session controller would read must be valid at dispatch time.");
            Assert.That(controller.Config.Name, Is.EqualTo("Meadow Home"));
        }

        [Test]
        public void CreateWithAnInvalidConfigShowsTheErrorAndDoesNotDispatch()
        {
            NewWorldScreenController controller = CreateRoutedController(
                out VisualElement root, out _, out List<string> forwarded);
            controller.ResetForNewWorld();

            root.Q<TextField>("bv-name-field").value = "   ";
            controller.SubmitCreate();
            Assert.That(forwarded, Is.Empty, "A blank name must not dispatch new_world.create.");
            Label status = root.Q<Label>("bv-status");
            Assert.That(status.text, Is.Not.Empty);
            Assert.That(status.ClassListContains("hs-status--rejected"), Is.True,
                "Validation failures carry the rejected signal class alongside the message.");

            // Survival + builder preset is the second model rule (matrix row 6).
            root.Q<TextField>("bv-name-field").value = "Meadow Home";
            controller.CycleSelector(3, forward: true);
            Assert.That(controller.Config.WorldPreset, Is.EqualTo(WorldPresetIds.FlatBuilder));
            controller.SubmitCreate();
            Assert.That(forwarded, Is.Empty);
            Assert.That(status.text, Does.Contain("Survival Terrain"));

            // Positive control: switching to creative makes the same preset valid.
            controller.CycleSelector(0, forward: true);
            controller.SubmitCreate();
            Assert.That(forwarded, Is.EqualTo(new[] { MenuActions.NewWorldCreate }));
            Assert.That(status.text, Is.Empty, "A successful create clears the error.");
            Assert.That(status.ClassListContains("hs-status--rejected"), Is.False);
        }

        [Test]
        public void CancelDispatchPopsTheRouterBackToTheTitleScreen()
        {
            NewWorldScreenController controller = CreateRoutedController(
                out _, out BlockiverseMenuController menu, out _);

            menu.DispatchAction(MenuActions.TitleNewWorld);
            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.NewWorldScreen));

            controller.SubmitCancel();

            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen),
                "new_world.cancel routes as a screen pop, verbatim id required.");
        }

        // Mirrors the uGUI panel's null-Config guards: before the host's first reset the
        // screen must swallow interaction instead of throwing or dispatching.
        [Test]
        public void SubmittingBeforeTheFirstResetDoesNothing()
        {
            NewWorldScreenController controller = CreateRoutedController(
                out VisualElement root, out _, out List<string> forwarded);

            controller.SubmitCreate();
            controller.CycleSelector(0, forward: true);

            Assert.That(controller.Config, Is.Null);
            Assert.That(forwarded, Is.Empty);
            Assert.That(root.Q<Label>("bv-status").text, Is.Empty);
        }
    }

    // Matrix row 7: the routed world-loading surface is a static, input-inert overlay. The
    // controller is deliberately trivial, so what these tests pin is the document contract —
    // it binds, and nothing in it can ever take a ray.
    public sealed class WorldLoadingScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/WorldLoadingScreen.uxml";

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        [Test]
        public void AttachBindsTheDocumentAndAnEmptyTreeRefusesToBind()
        {
            var gameObject = new GameObject("World Loading Screen Under Test");
            objectsToDestroy.Add(gameObject);
            WorldLoadingScreenController controller = gameObject.AddComponent<WorldLoadingScreenController>();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {DocumentPath}");
            controller.AttachForTest(tree.Instantiate());

            Assert.That(controller.IsBound, Is.True);

            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var strayObject = new GameObject("Empty Tree Control");
                objectsToDestroy.Add(strayObject);
                WorldLoadingScreenController stray = strayObject.AddComponent<WorldLoadingScreenController>();
                stray.AttachForTest(new VisualElement());
                Assert.That(stray.IsBound, Is.False);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [Test]
        public void TheDocumentIsCompletelyInputInert()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {DocumentPath}");
            VisualElement root = tree.Instantiate();
            VisualElement screenRoot = root.Q<VisualElement>("bv-screen-root");

            Assert.That(screenRoot, Is.Not.Null);
            Assert.That(screenRoot.pickingMode, Is.EqualTo(PickingMode.Ignore),
                "The overlay root must never intercept a ray.");

            Assert.That(root.Query<Button>().ToList(), Is.Empty);
            Assert.That(root.Query<TextField>().ToList(), Is.Empty);
            Assert.That(root.Query<Toggle>().ToList(), Is.Empty);
            Assert.That(root.Query<Slider>().ToList(), Is.Empty);

            var pickable = new List<string>();
            screenRoot.Query<VisualElement>().ForEach(element =>
            {
                if (element.pickingMode != PickingMode.Ignore)
                    pickable.Add(element.name);
            });
            Assert.That(pickable, Is.Empty,
                "Every element of the loading overlay must be picking-mode Ignore.");
        }
    }
}
