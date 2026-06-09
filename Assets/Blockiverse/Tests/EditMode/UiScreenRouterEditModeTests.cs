using Blockiverse.UI;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class UiScreenRouterEditModeTests
    {
        static readonly ScreenRoute Title = new("title_menu", pauseGame: true, allowWorldInput: false);
        static readonly ScreenRoute Hud = new("gameplay_hud", pauseGame: false, allowWorldInput: true);
        static readonly ScreenRoute Pause = new("pause_menu", pauseGame: true, allowWorldInput: false);

        [Test]
        public void NewRouterStartsAtRoot()
        {
            var router = new UiScreenRouter(Title);

            Assert.That(router.ActiveScreen, Is.EqualTo(Title));
            Assert.That(router.ScreenDepth, Is.EqualTo(1));
            Assert.That(router.HasModal, Is.False);
            Assert.That(router.InputTarget, Is.EqualTo("title_menu"));
        }

        [Test]
        public void PushAndPopScreenChangesActiveScreen()
        {
            var router = new UiScreenRouter(Hud);

            router.PushScreen(Pause);
            Assert.That(router.ActiveScreen, Is.EqualTo(Pause));
            Assert.That(router.ScreenDepth, Is.EqualTo(2));

            Assert.That(router.PopScreen(), Is.True);
            Assert.That(router.ActiveScreen, Is.EqualTo(Hud));
            Assert.That(router.ScreenDepth, Is.EqualTo(1));
        }

        [Test]
        public void RootScreenCannotBePopped()
        {
            var router = new UiScreenRouter(Hud);

            Assert.That(router.PopScreen(), Is.False);
            Assert.That(router.ActiveScreen, Is.EqualTo(Hud));
            Assert.That(router.ScreenDepth, Is.EqualTo(1));
        }

        [Test]
        public void ReplaceScreenSwapsTopWithoutGrowingStack()
        {
            var router = new UiScreenRouter(new ScreenRoute("main_menu"));

            router.ReplaceScreen(new ScreenRoute("new_world"));
            Assert.That(router.ActiveScreen.ScreenId, Is.EqualTo("new_world"));
            Assert.That(router.ScreenDepth, Is.EqualTo(1));
        }

        [Test]
        public void ClearToRootResetsScreenAndModalStacks()
        {
            var router = new UiScreenRouter(Hud);
            router.PushScreen(Pause);
            router.PushModal("confirm_dialog");

            router.ClearToRoot(Title);

            Assert.That(router.ActiveScreen, Is.EqualTo(Title));
            Assert.That(router.ScreenDepth, Is.EqualTo(1));
            Assert.That(router.HasModal, Is.False);
        }

        [Test]
        public void ModalTakesInputPriorityAndBlocksWorldInput()
        {
            var router = new UiScreenRouter(Hud);
            Assert.That(router.InputTarget, Is.EqualTo("gameplay_hud"));
            Assert.That(router.AllowWorldInput, Is.True);

            router.PushModal("confirm_dialog");
            Assert.That(router.InputTarget, Is.EqualTo("confirm_dialog"), "Open modal must receive input first (§2.2).");
            Assert.That(router.AllowWorldInput, Is.False, "A modal suppresses world input.");

            Assert.That(router.PopModal(), Is.True);
            Assert.That(router.InputTarget, Is.EqualTo("gameplay_hud"));
            Assert.That(router.AllowWorldInput, Is.True);
            Assert.That(router.PopModal(), Is.False);
        }

        [Test]
        public void PauseStateFollowsActiveScreen()
        {
            var router = new UiScreenRouter(Hud);
            Assert.That(router.IsGamePaused, Is.False);

            router.PushScreen(Pause);
            Assert.That(router.IsGamePaused, Is.True);

            router.PopScreen();
            Assert.That(router.IsGamePaused, Is.False);
        }

        [Test]
        public void ChangedEventFiresOnStackMutations()
        {
            var router = new UiScreenRouter(Hud);
            int changes = 0;
            router.Changed += () => changes++;

            router.PushScreen(Pause);   // 1
            router.PushModal("confirm"); // 2
            router.PopModal();           // 3
            router.PopScreen();          // 4
            router.ReplaceScreen(Title); // 5
            router.ClearToRoot(Hud);     // 6

            Assert.That(changes, Is.EqualTo(6));
        }
    }
}
