using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blockiverse.UI.Toolkit;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Rules for the world-space UI Toolkit scene configuration (ADR 0010 §2).
    //
    // Every setting these cover is silent when wrong — a mis-configured panel renders correctly and
    // ignores the controller — so the validator is the only thing standing between a bad setting
    // and a headset session spent debugging "UI Toolkit doesn't work in VR".
    //
    // Each rule is asserted from BOTH sides, and never as a bare "no findings". A test whose only
    // assertion is an absence passes just as happily when the validator has stopped working, when
    // the fixture is malformed enough that every rule skips it, or when the rule was deleted. The
    // discriminating form is: same scene, ONE field changed, opposite verdicts.
    public sealed class XrUiToolkitConfigurationEditModeTests
    {
        const int InteractionLayer = 10;
        const int RaycastMask = 1 << InteractionLayer;

        static XrUiToolkitPanelState HealthyPanel(
            string name = "Panel",
            bool visible = true,
            bool worldSpace = true,
            bool hasCollider = true,
            bool colliderIsEffective = true,
            int layer = InteractionLayer,
            bool isNested = false) =>
            new(name, visible, true, worldSpace, hasCollider, colliderIsEffective, layer, isNested);

        static XrUiToolkitSceneState HealthyScene(
            int managers = 1,
            int configurations = 1,
            bool redirectionIsNever = true,
            bool processWorldSpaceInput = true,
            bool hasUguiInputModule = true,
            bool bypassUiToolkitEvents = false,
            bool hasUiRay = true,
            int raycastMask = RaycastMask,
            IReadOnlyList<XrUiToolkitPanelState> panels = null) =>
            new(managers, configurations, redirectionIsNever, processWorldSpaceInput,
                hasUguiInputModule, bypassUiToolkitEvents, hasUiRay, raycastMask,
                panels ?? new[] { HealthyPanel() });

        static IReadOnlyList<XrUiToolkitIssue> IssuesOf(XrUiToolkitSceneState state) =>
            XrUiToolkitConfigurationValidator.Validate(state).Select(f => f.Issue).ToList();

        // The control for every other test in this file. If this fails, the fixtures below are
        // asserting against a baseline that already has findings and their evidence is worthless.
        [Test]
        public void CorrectlyConfiguredSceneProducesNoFindings()
        {
            Assert.That(IssuesOf(HealthyScene()), Is.Empty);
        }

        // ---- Toolkit manager ------------------------------------------------

        [Test]
        public void ToolkitManagerCountIsWhatDecidesTheManagerFindings()
        {
            Assert.That(IssuesOf(HealthyScene(managers: 0)),
                Does.Contain(XrUiToolkitIssue.ToolkitManagerMissing));
            Assert.That(IssuesOf(HealthyScene(managers: 2)),
                Does.Contain(XrUiToolkitIssue.ToolkitManagerDuplicated));
            Assert.That(IssuesOf(HealthyScene(managers: 1)),
                Does.Not.Contains(XrUiToolkitIssue.ToolkitManagerMissing));
        }

        // ---- Panel input configuration --------------------------------------

        [Test]
        public void MissingPanelInputConfigurationIsReported()
        {
            Assert.That(IssuesOf(HealthyScene(configurations: 0)),
                Does.Contain(XrUiToolkitIssue.PanelInputConfigurationMissing));
        }

        // A missing component must not also be reported as two wrong settings: three findings for
        // one cause buries the one that names the fix.
        [Test]
        public void MissingPanelInputConfigurationIsReportedOnceNotThreeTimes()
        {
            IReadOnlyList<XrUiToolkitIssue> issues = IssuesOf(HealthyScene(configurations: 0));

            // Positive first — without it this passes on an empty result, the one outcome that
            // would mean the validator had stopped working entirely.
            Assert.That(issues, Does.Contain(XrUiToolkitIssue.PanelInputConfigurationMissing));
            Assert.That(issues, Does.Not.Contains(XrUiToolkitIssue.PanelInputRedirectionNotNever));
            Assert.That(issues, Does.Not.Contains(XrUiToolkitIssue.WorldSpaceInputDisabled));
        }

        [Test]
        public void DuplicatePanelInputConfigurationsAreReported()
        {
            Assert.That(IssuesOf(HealthyScene(configurations: 2)),
                Does.Contain(XrUiToolkitIssue.PanelInputConfigurationDuplicated));
            Assert.That(IssuesOf(HealthyScene(configurations: 1)),
                Does.Not.Contains(XrUiToolkitIssue.PanelInputConfigurationDuplicated));
        }

        [Test]
        public void PanelInputRedirectionMustBeNever()
        {
            Assert.That(IssuesOf(HealthyScene(redirectionIsNever: false)),
                Does.Contain(XrUiToolkitIssue.PanelInputRedirectionNotNever));
            Assert.That(IssuesOf(HealthyScene(redirectionIsNever: true)),
                Does.Not.Contains(XrUiToolkitIssue.PanelInputRedirectionNotNever));
        }

        [Test]
        public void WorldSpaceInputMustBeEnabled()
        {
            Assert.That(IssuesOf(HealthyScene(processWorldSpaceInput: false)),
                Does.Contain(XrUiToolkitIssue.WorldSpaceInputDisabled));
            Assert.That(IssuesOf(HealthyScene(processWorldSpaceInput: true)),
                Does.Not.Contains(XrUiToolkitIssue.WorldSpaceInputDisabled));
        }

        // The rule is conditional on uGUI being present, and that condition has to be load-bearing:
        // once the legacy menus are gone (Phase 6) the setting stops mattering, and a rule asserted
        // unconditionally would become a permanent false positive nobody can clear.
        [Test]
        public void UguiPresenceIsWhatGatesTheBypassRule()
        {
            Assert.That(IssuesOf(HealthyScene(hasUguiInputModule: true, bypassUiToolkitEvents: true)),
                Does.Contain(XrUiToolkitIssue.UiToolkitEventsBypassed));
            Assert.That(IssuesOf(HealthyScene(hasUguiInputModule: false, bypassUiToolkitEvents: true)),
                Does.Not.Contains(XrUiToolkitIssue.UiToolkitEventsBypassed));
        }

        // ---- Rig ------------------------------------------------------------

        // "No ray in the scene" and "a ray that raycasts Nothing" both used to collapse to a mask of
        // zero. They are opposite situations: the first is a fixture without a rig, the second is a
        // rig that can hit nothing at all — and collapsing them let the second take the first's
        // escape hatch and report clean.
        [Test]
        public void AbsentRayAndEmptyMaskAreDistinguished()
        {
            Assert.That(IssuesOf(HealthyScene(hasUiRay: false, raycastMask: 0)),
                Does.Contain(XrUiToolkitIssue.NoUiRayInScene));
            Assert.That(IssuesOf(HealthyScene(hasUiRay: false, raycastMask: 0)),
                Does.Not.Contains(XrUiToolkitIssue.UiRayRaycastsNothing));

            Assert.That(IssuesOf(HealthyScene(hasUiRay: true, raycastMask: 0)),
                Does.Contain(XrUiToolkitIssue.UiRayRaycastsNothing));
            Assert.That(IssuesOf(HealthyScene(hasUiRay: true, raycastMask: 0)),
                Does.Not.Contains(XrUiToolkitIssue.NoUiRayInScene));
        }

        [Test]
        public void WithoutARayThePanelLayerIsNotJudged()
        {
            // Control: this exact panel IS condemned when a ray exists, so the pass below is
            // attributable to the absent ray and not to the panel being fine after all.
            Assert.That(IssuesOf(HealthyScene(panels: new[] { HealthyPanel(layer: 5) })),
                Does.Contain(XrUiToolkitIssue.DocumentColliderNotRaycastable));
            Assert.That(
                IssuesOf(HealthyScene(hasUiRay: false, raycastMask: 0,
                    panels: new[] { HealthyPanel(layer: 5) })),
                Does.Not.Contains(XrUiToolkitIssue.DocumentColliderNotRaycastable));
        }

        // ---- Panels ---------------------------------------------------------

        [Test]
        public void ScreenSpacePanelSettingsIsReported()
        {
            Assert.That(IssuesOf(HealthyScene(panels: new[] { HealthyPanel(worldSpace: false) })),
                Does.Contain(XrUiToolkitIssue.PanelSettingsNotWorldSpace));
            Assert.That(IssuesOf(HealthyScene(panels: new[] { HealthyPanel(worldSpace: true) })),
                Does.Not.Contains(XrUiToolkitIssue.PanelSettingsNotWorldSpace));
        }

        [Test]
        public void PanelWithoutColliderIsReported()
        {
            Assert.That(IssuesOf(HealthyScene(panels: new[] { HealthyPanel(hasCollider: false) })),
                Does.Contain(XrUiToolkitIssue.DocumentColliderMissing));
            Assert.That(IssuesOf(HealthyScene(panels: new[] { HealthyPanel(hasCollider: true) })),
                Does.Not.Contains(XrUiToolkitIssue.DocumentColliderMissing));
        }

        // The specific trap this project has: the XRI sample puts panels on Unity layer 5 (UI), but
        // Blockiverse's rays raycast against BlockiverseInteractable only. Copying the sample's
        // layer produces a panel that renders perfectly and cannot be pointed at.
        [Test]
        public void TheColliderLayerIsWhatDecidesRaycastability()
        {
            Assert.That(IssuesOf(HealthyScene(panels: new[] { HealthyPanel(layer: 5) })),
                Does.Contain(XrUiToolkitIssue.DocumentColliderNotRaycastable));
            Assert.That(IssuesOf(HealthyScene(panels: new[] { HealthyPanel(layer: InteractionLayer) })),
                Does.Not.Contains(XrUiToolkitIssue.DocumentColliderNotRaycastable));
        }

        // The migration's end state is routed screens whose documents ship hidden until routed. A
        // layer rule gated on visibility would be suppressed on exactly those panels at author time
        // and only surface when a player opened the screen — which is the whole failure this rule
        // exists to prevent, arriving later and in a headset instead of in CI.
        [Test]
        public void AWrongLayerIsReportedEvenWhileThePanelIsHidden()
        {
            Assert.That(
                IssuesOf(HealthyScene(panels: new[]
                    { HealthyPanel(visible: false, colliderIsEffective: false, layer: 5) })),
                Does.Contain(XrUiToolkitIssue.DocumentColliderNotRaycastable));
        }

        // Collider.enabled stays true when its GameObject is deactivated, but such a collider is not
        // in the physics scene and intercepts nothing. Reporting it would be an unclearable false
        // positive whose only "fix" is disabling something already inert.
        [Test]
        public void ColliderEffectivenessIsWhatDecidesTheHiddenPanelFinding()
        {
            Assert.That(
                IssuesOf(HealthyScene(panels: new[]
                    { HealthyPanel(visible: false, colliderIsEffective: true) })),
                Does.Contain(XrUiToolkitIssue.HiddenDocumentStillRaycastable));
            Assert.That(
                IssuesOf(HealthyScene(panels: new[]
                    { HealthyPanel(visible: false, colliderIsEffective: false) })),
                Does.Not.Contains(XrUiToolkitIssue.HiddenDocumentStillRaycastable));
        }

        // Unity never gives a nested UIDocument a collider — UpdateWorldSpaceCollider returns early
        // when parentUI is set — so demanding one would give a composed screen a permanent finding
        // it could not clear.
        [Test]
        public void NestedDocumentsAreExemptFromColliderRules()
        {
            Assert.That(
                IssuesOf(HealthyScene(panels: new[]
                    { HealthyPanel(hasCollider: false, isNested: false) })),
                Does.Contain(XrUiToolkitIssue.DocumentColliderMissing));
            Assert.That(
                IssuesOf(HealthyScene(panels: new[]
                    { HealthyPanel(hasCollider: false, isNested: true) })),
                Does.Not.Contains(XrUiToolkitIssue.DocumentColliderMissing));
        }

        // ...but a nested document still has to render somewhere, so the panel-settings rules apply.
        [Test]
        public void NestedDocumentsAreStillCheckedForWorldSpaceRendering()
        {
            Assert.That(
                IssuesOf(HealthyScene(panels: new[]
                    { HealthyPanel(worldSpace: false, hasCollider: false, isNested: true) })),
                Does.Contain(XrUiToolkitIssue.PanelSettingsNotWorldSpace));
        }

        // Findings must name the panel they came from; a scene with a dozen documents produces a
        // list that is unusable if every entry says only "a panel is wrong".
        [Test]
        public void PanelFindingsCarryTheOffendingPanelName()
        {
            IReadOnlyList<XrUiToolkitFinding> findings =
                XrUiToolkitConfigurationValidator.Validate(
                    HealthyScene(panels: new[]
                    {
                        HealthyPanel("Good"),
                        HealthyPanel("Bad", hasCollider: false),
                    }));

            Assert.That(findings.Count, Is.EqualTo(1));
            Assert.That(findings[0].Subject, Is.EqualTo("Bad"));
            Assert.That(findings[0].Issue, Is.EqualTo(XrUiToolkitIssue.DocumentColliderMissing));
        }

        // Layer 31 is 1 << 31 == int.MinValue, and "Everything" is -1. Both are ordinary in a Unity
        // LayerMask and both break a naive mask test written with > 0 instead of != 0.
        [Test]
        public void LayerMaskArithmeticHandlesTheEdgeLayers()
        {
            Assert.That(
                IssuesOf(HealthyScene(raycastMask: 1 << 31, panels: new[] { HealthyPanel(layer: 31) })),
                Does.Not.Contains(XrUiToolkitIssue.DocumentColliderNotRaycastable));
            Assert.That(
                IssuesOf(HealthyScene(raycastMask: -1, panels: new[] { HealthyPanel(layer: 5) })),
                Does.Not.Contains(XrUiToolkitIssue.DocumentColliderNotRaycastable));
        }

        // ADR 0010 §1: the Toolkit assembly is the one place uGUI must never reach. Asserted against
        // the asmdef rather than by grepping sources, because a reference added for one screen would
        // let every later screen quietly use TMP and the migration would end with two UI frameworks.
        [Test]
        public void ToolkitAssemblyDoesNotReferenceUguiOrTextMeshPro()
        {
            string path = Path.Combine(
                UnityEngine.Application.dataPath,
                "Blockiverse/Scripts/UI/Toolkit/Blockiverse.UI.Toolkit.asmdef");

            Assert.That(File.Exists(path), Is.True, $"Assembly definition not found at {path}.");

            string json = File.ReadAllText(path);

            // Positive control. Two Does.Not.Contain assertions pass just as happily against an
            // empty string or the wrong file, so pin down that this is the real asmdef with its
            // real references before concluding anything from what is absent.
            Assert.That(json, Does.Contain("\"name\": \"Blockiverse.UI.Toolkit\""));
            Assert.That(json, Does.Contain("Blockiverse.Core"));

            // "UnityEngine.UI" is also a prefix of "UnityEngine.UIElements". That is intentional:
            // the UIElements module is auto-referenced and must never be listed here explicitly, so
            // either spelling appearing in this file is a finding.
            Assert.That(json, Does.Not.Contains("UnityEngine.UI"));
            Assert.That(json, Does.Not.Contains("Unity.TextMeshPro"));

            // Blockiverse.UI is the assembly that carries uGUI and TMP. Referencing it from here
            // would not import those types, but it would put the boundary one edit away from being
            // meaningless — so the boundary is drawn at the reference list, not at the using
            // directives.
            Assert.That(json, Does.Not.Contains("\"Blockiverse.UI\""));
        }
    }
}
