using System.Linq;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseRigPrefabTests
    {
        [Test]
        public void XrRigPrefabIsWiredForQuestControllerAnchorsAndHaptics()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<BlockiverseInputRig>(), Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Left Controller"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Right Controller"), Is.Not.Null);

            BlockiverseControllerAnchor[] anchors = prefab
                .GetComponentsInChildren<BlockiverseControllerAnchor>(true);
            BlockiverseControllerHaptics[] haptics = prefab
                .GetComponentsInChildren<BlockiverseControllerHaptics>(true);

            Assert.That(anchors, Has.Length.EqualTo(2));
            Assert.That(haptics, Has.Length.EqualTo(2));
            Assert.That(anchors.Select(anchor => anchor.Role), Is.EquivalentTo(new[]
            {
                BlockiverseControllerRole.Left,
                BlockiverseControllerRole.Right
            }));
            Assert.That(haptics.Select(controllerHaptics => controllerHaptics.Role), Is.EquivalentTo(new[]
            {
                BlockiverseControllerRole.Left,
                BlockiverseControllerRole.Right
            }));

            AssertController(prefab, "Left Controller", BlockiverseControllerRole.Left);
            AssertController(prefab, "Right Controller", BlockiverseControllerRole.Right);
        }

        [Test]
        public void XrRigRuntimeRepairsHeadPoseDriverAndContinuousMove()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                BlockiverseInputRig inputRig = instance.GetComponent<BlockiverseInputRig>();
                inputRig?.RepairRuntimeTracking();

                BlockiverseContinuousMoveLocomotion continuousMove = instance.GetComponent<BlockiverseContinuousMoveLocomotion>();
                TrackedPoseDriver poseDriver = inputRig?.HeadPoseDriver;
                BlockiverseHeadPoseTracker poseTracker = inputRig?.HeadPoseTracker;

                Assert.That(inputRig, Is.Not.Null);
                Assert.That(continuousMove, Is.Not.Null);
                Assert.That(poseDriver, Is.Not.Null);
                Assert.That(poseDriver.enabled, Is.False);
                Assert.That(poseTracker, Is.Not.Null);
                AssertBinding(poseTracker.PositionAction, "<XRHMD>/centerEyePosition");
                AssertBinding(poseTracker.RotationAction, "<XRHMD>/centerEyeRotation");
                AssertBinding(poseTracker.TrackingStateAction, "<XRHMD>/trackingState");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void XrRigPrefabIsWiredForComfortSettingsMenu()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();
            XROrigin origin = prefab.GetComponent<XROrigin>();
            BlockiverseComfortSettings settings = prefab.GetComponent<BlockiverseComfortSettings>();
            Transform menuTransform = prefab.transform.Find("Camera Offset/Comfort Settings Menu");
            BlockiverseComfortMenu menu = menuTransform?.GetComponent<BlockiverseComfortMenu>();

            Assert.That(inputRig, Is.Not.Null);
            Assert.That(origin, Is.Not.Null);
            Assert.That(settings, Is.Not.Null);
            Assert.That(origin.CameraYOffset, Is.EqualTo(settings.StandingEyeHeight).Within(0.01f));
            Assert.That(menuTransform, Is.Not.Null);
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.IsVisible, Is.False);
            Assert.That(inputRig.MenuPressed.GetPersistentEventCount(), Is.EqualTo(1));
            BlockiverseWorldSpacePanelPresenter presenter = menuTransform.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.PlaysShowFeedback, Is.True);
            Assert.That(presenter.ShowFeedbackCue, Is.EqualTo(BlockiverseAudioCue.UiConfirm));
            Assert.That(presenter.PlaysHideFeedback, Is.True);
            Assert.That(presenter.HideFeedbackCue, Is.EqualTo(BlockiverseAudioCue.UiCancel));
            Assert.That(inputRig.MenuPressed.GetPersistentTarget(0), Is.SameAs(presenter));
            Assert.That(inputRig.MenuPressed.GetPersistentMethodName(0), Is.EqualTo(nameof(BlockiverseWorldSpacePanelPresenter.ToggleVisible)));
        }

        [Test]
        public void XrRigPrefabIsWiredForInteractionPointerAndBlockMenu()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            Transform rightController = prefab.transform.Find("Camera Offset/Right Controller");
            Transform pointerLine = rightController?.Find("Ray Pointer Line");
            Transform blockMenu = prefab.transform.Find("Camera Offset/Block Menu");
            BlockiverseRayPointer pointer = rightController?.GetComponent<BlockiverseRayPointer>();
            BlockiverseCreativeInputBridge creativeInputBridge = prefab.GetComponent<BlockiverseCreativeInputBridge>();
            BlockiverseVrUiPointer uiPointer = prefab.GetComponent<BlockiverseVrUiPointer>();
            CreativeHotbar hotbar = blockMenu?.GetComponent<CreativeHotbar>();
            Canvas blockMenuCanvas = blockMenu?.GetComponent<Canvas>();
            BlockiverseWorldSpacePanelPresenter blockMenuPresenter = blockMenu?.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            LineRenderer lineRenderer = pointerLine?.GetComponent<LineRenderer>();

            Assert.That(rightController, Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Left Controller"), Is.Not.Null);
            Assert.That(pointer, Is.Not.Null);
            Assert.That(creativeInputBridge, Is.Not.Null);
            Assert.That(uiPointer, Is.Not.Null);
            Assert.That(pointerLine, Is.Not.Null);
            Assert.That(lineRenderer, Is.Not.Null);
            Assert.That(lineRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(lineRenderer.receiveShadows, Is.False);
            Assert.That(blockMenu, Is.Not.Null);
            Assert.That(hotbar, Is.Not.Null);
            Assert.That(blockMenuCanvas, Is.Not.Null);
            Assert.That(blockMenuPresenter, Is.Not.Null);
            Assert.That(blockMenuPresenter.PlaysShowFeedback, Is.True);
            Assert.That(blockMenuPresenter.ShowFeedbackCue, Is.EqualTo(BlockiverseAudioCue.InventoryOpen));
            Assert.That(blockMenuPresenter.PlaysHideFeedback, Is.True);
            Assert.That(blockMenuPresenter.HideFeedbackCue, Is.EqualTo(BlockiverseAudioCue.InventoryClose));
            Assert.That(blockMenuCanvas.enabled, Is.False);

            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();
            UnityEngine.Events.UnityEvent quickMenuEvent = inputRig.QuickMenuPressed;
            Assert.That(quickMenuEvent, Is.Not.Null);
            Assert.That(quickMenuEvent.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(quickMenuEvent.GetPersistentTarget(0), Is.SameAs(blockMenuPresenter));
            Assert.That(quickMenuEvent.GetPersistentMethodName(0), Is.EqualTo(nameof(BlockiverseWorldSpacePanelPresenter.ToggleVisible)));
        }

        [Test]
        public void XrRigPrefabHasAllM6AudioCueClipsAssigned()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseAudioCuePlayer audioCuePlayer = prefab.GetComponent<BlockiverseAudioCuePlayer>();

            Assert.That(audioCuePlayer, Is.Not.Null);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.BlockBreak), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.BlockPlace), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.UiSelect), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.UiConfirm), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.UiCancel), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.InventoryOpen), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.InventoryClose), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.CraftSuccess), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.CraftFail), Is.True);
            Assert.That(audioCuePlayer.FootstepClipCount, Is.EqualTo(2));
        }

        [Test]
        public void XrRigPrefabShowsControllerMappingPopupAndInteractiveSurvivalHud()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            Transform popup = prefab.transform.Find("Camera Offset/Controller Mapping Popup");
            Transform startupOverlay = prefab.transform.Find("Camera Offset/Startup Loading Overlay");
            Transform survivalHud = prefab.transform.Find("Camera Offset/Survival HUD");

            Assert.That(popup, Is.Not.Null);
            BlockiverseWorldSpacePanelPresenter popupPresenter = popup.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(popupPresenter, Is.Not.Null);
            Assert.That(popupPresenter.ShowOnStart, Is.True);
            Assert.That(popup.GetComponent<Canvas>()?.enabled, Is.False);
            Assert.That(popup.GetComponentsInChildren<Button>(includeInactive: true), Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(
                popup.GetComponentsInChildren<Text>(includeInactive: true)
                    .Any(label => label.text.Contains("Right trigger")),
                Is.True);

            Assert.That(startupOverlay, Is.Not.Null);
            BlockiverseWorldSpacePanelPresenter startupPresenter = startupOverlay.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(startupPresenter, Is.Not.Null);
            Assert.That(startupPresenter.ShowOnStart, Is.True);
            Assert.That(startupOverlay.GetComponent<Canvas>()?.enabled, Is.False);

            Assert.That(survivalHud, Is.Not.Null);
            Assert.That(survivalHud.GetComponentsInChildren<Button>(includeInactive: true), Has.Length.GreaterThanOrEqualTo(11));
            Assert.That(survivalHud.GetComponentInChildren<SurvivalCraftingPanel>(includeInactive: true), Is.Not.Null);
            Assert.That(survivalHud.GetComponentInChildren<SurvivalInventoryPanel>(includeInactive: true), Is.Not.Null);
        }

        [Test]
        public void XrRigPrefabHasSinglePlayerFallbackAvatarRig()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                Component avatarRig = instance.GetComponent("BlockiverseNetworkAvatarRig");

                Assert.That(avatarRig, Is.Not.Null);
                avatarRig.GetType().GetMethod("RefreshAvatarMode").Invoke(avatarRig, null);
                Assert.That(GetAvatarProperty<bool>(avatarRig, "FallbackProxyEnabled"), Is.True);
                Assert.That(GetAvatarProperty<bool>(avatarRig, "MetaAvatarAvailable"), Is.False);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "FallbackRoot"), Is.Not.Null);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "HeadAnchor"), Is.Not.Null);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "LeftHandAnchor"), Is.Not.Null);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "RightHandAnchor"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static void AssertController(GameObject prefab, string controllerName, BlockiverseControllerRole expectedRole)
        {
            Transform controller = prefab.transform.Find($"Camera Offset/{controllerName}");

            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.GetComponent<BlockiverseControllerAnchor>()?.Role, Is.EqualTo(expectedRole));
            Assert.That(controller.GetComponent<BlockiverseControllerHaptics>()?.Role, Is.EqualTo(expectedRole));
        }

        static void AssertBinding(InputActionProperty property, string expectedPath)
        {
            Assert.That(property.action, Is.Not.Null);
            Assert.That(property.action.bindings, Has.Some.Matches<InputBinding>(
                binding => binding.effectivePath == expectedPath || binding.path == expectedPath));
        }

        static void AssertBinding(InputAction action, string expectedPath)
        {
            Assert.That(action, Is.Not.Null);
            Assert.That(action.bindings, Has.Some.Matches<InputBinding>(
                binding => binding.effectivePath == expectedPath || binding.path == expectedPath));
        }

        static T GetAvatarProperty<T>(Component avatarRig, string propertyName)
        {
            return (T)avatarRig.GetType().GetProperty(propertyName).GetValue(avatarRig);
        }
    }
}
