using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Networking;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // Replaces MenuRuntimeWiringEditModeTests.SystemKeyboardFieldOwnsOnlyOneActiveFieldAtATime,
    // which asserted by reflection that BlockiverseSystemKeyboardField exposed
    // AnyKeyboardVisible and KeyboardVisibilityChanged. That component was the uGUI
    // TMP_InputField bridge; UI Toolkit opens the system keyboard itself, so the signal now
    // comes from TouchScreenKeyboard directly.
    //
    // The failure this guards is silent by construction: a controller subscribed to an event
    // nobody raises any more compiles, runs, and simply never hides the hands. Only a device
    // session would show it, so the source of truth is pinned here instead.
    public sealed class BlockiverseKeyboardHandVisibilityEditModeTests
    {
        GameObject avatarObject;

        [TearDown]
        public void TearDown()
        {
            if (avatarObject != null)
                Object.DestroyImmediate(avatarObject);
        }

        static void Invoke(object target, string methodName, params object[] args)
        {
            MethodInfo method = target
                .GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"{target.GetType().Name}.{methodName} was renamed.");
            method.Invoke(target, args);
        }

        static bool HandsVisible(BlockiverseNetworkAvatarRig rig) =>
            rig.FallbackRoot
                .GetComponentsInChildren<Renderer>(includeInactive: true)
                .Any(renderer => renderer.transform.name == "Fallback Left Hand" && renderer.enabled);

        [Test]
        public void KeyboardVisibilityDrivesFallbackHandSuppressionAndReleasesItOnDisable()
        {
            avatarObject = new GameObject("Keyboard Hand Visibility Test");
            BlockiverseNetworkAvatarRig rig = avatarObject.AddComponent<BlockiverseNetworkAvatarRig>();
            rig.ConfigureFirstPersonFallbackVisuals(true);
            rig.SetMetaAvatarAvailable(false);

            BlockiverseKeyboardHandVisibilityController controller =
                avatarObject.AddComponent<BlockiverseKeyboardHandVisibilityController>();
            controller.Configure(rig);

            // No system keyboard in an EditMode run, so Configure must leave the hands alone.
            Assert.That(BlockiverseKeyboardHandVisibilityController.KeyboardVisible, Is.False,
                "Fixture guard: an EditMode run has no system keyboard on screen.");
            Assert.That(HandsVisible(rig), Is.True);

            Invoke(controller, "Apply", true);

            Assert.That(HandsVisible(rig), Is.False,
                "The fallback hands must not float in front of the system keyboard.");

            Invoke(controller, "OnDisable");

            Assert.That(HandsVisible(rig), Is.True,
                "Leaving the controller disabled while suppressed would strand the player with neither hands nor a keyboard.");
        }

        [Test]
        public void KeyboardVisibilityReadsTheSystemKeyboardRatherThanAUiComponent()
        {
            Assert.That(
                BlockiverseKeyboardHandVisibilityController.KeyboardVisible,
                Is.EqualTo(TouchScreenKeyboard.visible),
                "The controller must report the platform keyboard's own state.");

            // The discriminating half. Re-coupling this to whatever UI stack happens to own the
            // focused text field is exactly the regression that dropped out of the uGUI deletion:
            // the property above would still answer correctly while the controller polled a dead
            // source, so only the source itself can be asserted.
            string source = File.ReadAllText(Path.Combine(
                Application.dataPath, "Blockiverse", "Scripts", "VR",
                "BlockiverseKeyboardHandVisibilityController.cs"));

            Assert.That(source, Does.Contain("TouchScreenKeyboard.visible"));
            Assert.That(source, Does.Not.Contain("BlockiverseSystemKeyboardField.KeyboardVisibilityChanged"),
                "Hand visibility must not depend on a UI component's static event again.");
        }
    }
}
