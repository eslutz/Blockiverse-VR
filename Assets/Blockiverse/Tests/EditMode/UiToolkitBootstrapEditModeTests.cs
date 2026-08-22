using System.Collections.Generic;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.Editor;
using Blockiverse.UI.Toolkit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Blockiverse.Tests.EditMode
{
    // What the UI Toolkit bootstrapper generates (ADR 0010 §6).
    //
    // These build into a throwaway GameObject rather than asserting the committed Boot scene,
    // because the Phase 1 proof scaffolding is opt-in: a test that read the Boot scene would pass
    // vacuously on any tree where the scaffolding had not been generated, which is most of them.
    public sealed class UiToolkitBootstrapEditModeTests
    {
        readonly List<Object> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            objectsToDestroy.Add(root);
            return root;
        }

        PanelSettings CreateConfiguredPanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            objectsToDestroy.Add(settings);
            BlockiverseProjectBootstrapper.ConfigureMenuWorldSpacePanelSettings(settings);
            return settings;
        }

        // ---- Panel settings -------------------------------------------------

        [Test]
        public void PanelSettingsRenderInWorldSpace()
        {
            Assert.That(CreateConfiguredPanelSettings().renderMode, Is.EqualTo(PanelRenderMode.WorldSpace));
        }

        // colliderUpdateMode and colliderIsTrigger have assembly-internal setters and the enum
        // itself is private, so they are written through SerializedObject and can only be read back
        // the same way. Asserted by serialized name because that is the only reachable surface.
        [Test]
        public void PanelSettingsKeepsTheColliderTheBootstrapperSized()
        {
            var serialized = new SerializedObject(CreateConfiguredPanelSettings());

            // ColliderUpdateMode.Keep == 1 ("Keep existing colliders (if any)").
            Assert.That(serialized.FindProperty("m_ColliderUpdateMode"), Is.Not.Null);
            Assert.That(serialized.FindProperty("m_ColliderUpdateMode").intValue, Is.EqualTo(1));
        }

        [Test]
        public void PanelSettingsColliderIsATrigger()
        {
            var serialized = new SerializedObject(CreateConfiguredPanelSettings());

            Assert.That(serialized.FindProperty("m_ColliderIsTrigger"), Is.Not.Null);
            Assert.That(serialized.FindProperty("m_ColliderIsTrigger").boolValue, Is.True);
        }

        // m_PixelsPerUnit is declared float32 on PanelSettings. Reading it through intValue (as an
        // earlier version of this test did) cannot work, and writing it through intValue does not
        // land — which would leave the panel the right physical size only by coincidence, because
        // the constant happens to equal the constructor default.
        [Test]
        public void PanelSettingsPixelsPerUnitMatchesTheSizingModel()
        {
            var serialized = new SerializedObject(CreateConfiguredPanelSettings());
            SerializedProperty property = serialized.FindProperty("m_PixelsPerUnit");

            Assert.That(property, Is.Not.Null);
            Assert.That(
                property.propertyType,
                Is.EqualTo(SerializedPropertyType.Float),
                "m_PixelsPerUnit changed type; the bootstrapper writes it with floatValue.");
            Assert.That(
                property.floatValue,
                Is.EqualTo(BlockiverseProjectBootstrapper.UiToolkitPixelsPerUnit).Within(0.001f));
        }

        [Test]
        public void PanelSettingsCapsTheDynamicAtlasForQuest()
        {
            Assert.That(
                CreateConfiguredPanelSettings().dynamicAtlasSettings.maxAtlasSize,
                Is.EqualTo(BlockiverseProjectBootstrapper.UiToolkitMaxAtlasSize));
        }

        // ---- Infrastructure -------------------------------------------------

        [Test]
        public void InfrastructureAddsTheToolkitManagerAndPanelInputConfiguration()
        {
            GameObject root = CreateRoot("UI Toolkit");
            BlockiverseProjectBootstrapper.EnsureUiToolkitInfrastructure(root);

            Assert.That(root.GetComponentInChildren<XRUIToolkitManager>(true), Is.Not.Null);
            Assert.That(root.GetComponentInChildren<PanelInputConfiguration>(true), Is.Not.Null);
        }

        // XRI: the EventSystem interferes with UI Toolkit input unless redirection is off. This is
        // the single setting most likely to be "fixed" by someone who does not know why it is set.
        [Test]
        public void PanelInputRedirectionIsNever()
        {
            GameObject root = CreateRoot("UI Toolkit");
            BlockiverseProjectBootstrapper.EnsureUiToolkitInfrastructure(root);

            PanelInputConfiguration configuration =
                root.GetComponentInChildren<PanelInputConfiguration>(true);

            Assert.That(
                configuration.panelInputRedirection,
                Is.EqualTo(PanelInputConfiguration.PanelInputRedirection.Never));
            Assert.That(configuration.processWorldSpaceInput, Is.True);
        }

        // Idempotency is the whole contract of the bootstrapper: it is re-run constantly, and a
        // generator that appends produces a scene with two managers, where disabling either turns
        // UI Toolkit support off globally.
        [Test]
        public void RunningInfrastructureTwiceDoesNotDuplicateComponents()
        {
            GameObject root = CreateRoot("UI Toolkit");
            BlockiverseProjectBootstrapper.EnsureUiToolkitInfrastructure(root);
            BlockiverseProjectBootstrapper.EnsureUiToolkitInfrastructure(root);

            Assert.That(root.GetComponentsInChildren<XRUIToolkitManager>(true).Length, Is.EqualTo(1));
            Assert.That(root.GetComponentsInChildren<PanelInputConfiguration>(true).Length, Is.EqualTo(1));
        }

        // ---- Proof panel ----------------------------------------------------

        UiToolkitProofPanel CreateProofPanel()
        {
            GameObject root = CreateRoot("UI Toolkit");
            BlockiverseProjectBootstrapper.EnsureUiToolkitInfrastructure(root);
            return BlockiverseProjectBootstrapper.EnsureUiToolkitProofPanel(
                root, CreateConfiguredPanelSettings());
        }

        // The trap: the XRI sample puts its panels on Unity layer 5 (UI), but this project's rays
        // raycast against VrUiRaycastLayerMask only. A panel on the sample's layer renders and is
        // unhittable, with nothing in the editor to show for it.
        [Test]
        public void ProofPanelSitsOnTheLayerTheRaysActuallyCastAgainst()
        {
            UiToolkitProofPanel proof = CreateProofPanel();

            Assert.That(proof.gameObject.layer, Is.EqualTo(BlockiverseProject.InteractionLayerIndex));
            Assert.That(
                BlockiverseProject.VrUiRaycastLayerMask & (1 << proof.gameObject.layer),
                Is.Not.Zero);
        }

        [Test]
        public void ProofPanelDocumentIsFixedSizeInWorldSpace()
        {
            UIDocument document = CreateProofPanel().GetComponent<UIDocument>();

            Assert.That(document.worldSpaceSizeMode, Is.EqualTo(WorldSpaceSizeMode.Fixed));
            Assert.That(
                document.worldSpaceSize,
                Is.EqualTo(new Vector2(
                    BlockiverseProjectBootstrapper.UiToolkitPanelWidthPixels,
                    BlockiverseProjectBootstrapper.UiToolkitPanelHeightPixels)));
        }

        // The physical size a player actually sees, computed the one documented way:
        //   metres = worldSpaceSize / pixelsPerUnit * transform.localScale
        // Pinned because it is the number the headset comfort pass will argue about, and because
        // copying the uGUI Canvas scale constant across would silently produce a different one.
        [Test]
        public void ProofPanelIsOneMetreWide()
        {
            UiToolkitProofPanel proof = CreateProofPanel();
            UIDocument document = proof.GetComponent<UIDocument>();

            float metresWide = document.worldSpaceSize.x
                / BlockiverseProjectBootstrapper.UiToolkitPixelsPerUnit
                * proof.transform.localScale.x;
            float metresHigh = document.worldSpaceSize.y
                / BlockiverseProjectBootstrapper.UiToolkitPixelsPerUnit
                * proof.transform.localScale.y;

            Assert.That(metresWide, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(metresHigh, Is.EqualTo(0.7f).Within(0.001f));
        }

        // The collider is in local units and the transform scale is applied on top; getting this
        // wrong by the scale factor gives a 10 m hit box around a 1 m panel, which is invisible in
        // the editor and catches every ray in the room on device.
        [Test]
        public void ProofPanelColliderMatchesTheDocumentInWorldUnits()
        {
            UiToolkitProofPanel proof = CreateProofPanel();
            var collider = proof.GetComponent<BoxCollider>();
            UIDocument document = proof.GetComponent<UIDocument>();

            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.isTrigger, Is.True);

            Vector3 worldSize = Vector3.Scale(collider.size, proof.transform.localScale);
            float documentMetresWide = document.worldSpaceSize.x
                / BlockiverseProjectBootstrapper.UiToolkitPixelsPerUnit
                * proof.transform.localScale.x;

            Assert.That(worldSize.x, Is.EqualTo(documentMetresWide).Within(0.001f));
        }

        [Test]
        public void RunningProofPanelGenerationTwiceDoesNotDuplicateIt()
        {
            GameObject root = CreateRoot("UI Toolkit");
            PanelSettings settings = CreateConfiguredPanelSettings();

            BlockiverseProjectBootstrapper.EnsureUiToolkitInfrastructure(root);
            BlockiverseProjectBootstrapper.EnsureUiToolkitProofPanel(root, settings);
            BlockiverseProjectBootstrapper.EnsureUiToolkitProofPanel(root, settings);

            Assert.That(root.GetComponentsInChildren<UIDocument>(true).Length, Is.EqualTo(1));
            Assert.That(root.GetComponentsInChildren<BoxCollider>(true).Length, Is.EqualTo(1));
        }

        // Hiding must move the collider with the document. Split, a hidden panel keeps a live
        // collider and silently intercepts every world ray aimed past where it used to be.
        // Hiding must move the collider with the panel. Split, a hidden panel keeps a live collider
        // and silently intercepts every world ray aimed past where it used to be.
        //
        // Note what SetVisible must NOT do: disable the UIDocument. UIDocument.OnDisable nulls
        // rootVisualElement and OnEnable rebuilds the tree with new element instances, and because
        // UIDocument is a separate component that never runs this component's OnDisable, one
        // hide/show cycle would leave the panel rendered, unstyled and completely inert.
        [Test]
        public void HidingTheProofPanelDisablesItsColliderAndKeepsTheDocumentEnabled()
        {
            UiToolkitProofPanel proof = CreateProofPanel();
            var collider = proof.GetComponent<BoxCollider>();
            UIDocument document = proof.GetComponent<UIDocument>();

            proof.SetVisible(false);
            Assert.That(collider.enabled, Is.False);
            Assert.That(proof.IsVisible, Is.False);
            Assert.That(document.enabled, Is.True, "SetVisible must not disable the UIDocument.");

            proof.SetVisible(true);
            Assert.That(collider.enabled, Is.True);
            Assert.That(proof.IsVisible, Is.True);
            Assert.That(document.enabled, Is.True);
        }

        // The status path, exercised without a headset. Report() tolerates a null status label, so
        // this runs in EditMode where the document has not built a visual tree.
        //
        // It deliberately asserts the MESSAGE as well as the count: "an activation happened" is a
        // generic fact that an unrelated Report call would also satisfy, whereas the message
        // carrying the current count could only have been written by OnButtonClicked.
        [Test]
        public void ButtonActivationsAreCountedAndReported()
        {
            UiToolkitProofPanel proof = CreateProofPanel();

            Assert.That(proof.ButtonActivationCount, Is.Zero);

            proof.SimulateButtonActivation();
            Assert.That(proof.ButtonActivationCount, Is.EqualTo(1));
            Assert.That(proof.LastStatusMessage, Does.Contain("Activations: 1"));

            proof.SimulateButtonActivation();
            Assert.That(proof.ButtonActivationCount, Is.EqualTo(2));
            Assert.That(proof.LastStatusMessage, Does.Contain("Activations: 2"));
        }

        // A panel whose UXML did not load presents as a healthy blank rectangle — it attaches, and
        // every counter reports success. IsBound is the flag that discriminates, and this pins that
        // it is false in EditMode (where no visual tree is built) rather than defaulting to true.
        [Test]
        public void AnUnboundPanelDoesNotClaimToBeBound()
        {
            UiToolkitProofPanel proof = CreateProofPanel();

            Assert.That(
                proof.IsBound,
                Is.False,
                "EditMode builds no visual tree, so the panel cannot have found its elements. " +
                "IsBound reporting true here would mean it is not actually checking.");
        }

        // The end-to-end claim: what the bootstrapper generates is what the validator accepts.
        // Without this the two could drift apart and each would keep passing its own tests.
        [Test]
        public void GeneratedConfigurationSatisfiesTheValidator()
        {
            UiToolkitProofPanel proof = CreateProofPanel();
            var collider = proof.GetComponent<BoxCollider>();
            UIDocument document = proof.GetComponent<UIDocument>();

            var state = new XrUiToolkitSceneState(
                proof.transform.root.GetComponentsInChildren<XRUIToolkitManager>(true).Length,
                proof.transform.root.GetComponentsInChildren<PanelInputConfiguration>(true).Length,
                true,
                true,
                true,
                false,
                hasUiRay: true,
                BlockiverseProject.VrUiRaycastLayerMask,
                new[]
                {
                    new XrUiToolkitPanelState(
                        proof.gameObject.name,
                        true,
                        document.panelSettings != null,
                        document.panelSettings != null &&
                            document.panelSettings.renderMode == PanelRenderMode.WorldSpace,
                        collider != null,
                        collider != null && collider.enabled &&
                            collider.gameObject.activeInHierarchy,
                        proof.gameObject.layer),
                });

            Assert.That(XrUiToolkitConfigurationValidator.Validate(state), Is.Empty);

            // Control: the same generated panel moved to the XRI sample's layer 5 MUST be
            // condemned. Without this, "Is.Empty" above would pass equally if the layer rule had
            // been deleted, and the layer is the single most likely thing to be wrong.
            var onSampleLayer = new XrUiToolkitSceneState(
                proof.transform.root.GetComponentsInChildren<XRUIToolkitManager>(true).Length,
                proof.transform.root.GetComponentsInChildren<PanelInputConfiguration>(true).Length,
                true, true, true, false,
                hasUiRay: true,
                BlockiverseProject.VrUiRaycastLayerMask,
                new[]
                {
                    new XrUiToolkitPanelState(
                        proof.gameObject.name, true, true, true, true, true, colliderLayer: 5),
                });

            Assert.That(
                XrUiToolkitConfigurationValidator.Validate(onSampleLayer)
                    .Select(f => f.Issue).ToList(),
                Does.Contain(XrUiToolkitIssue.DocumentColliderNotRaycastable));
        }
    }
}
