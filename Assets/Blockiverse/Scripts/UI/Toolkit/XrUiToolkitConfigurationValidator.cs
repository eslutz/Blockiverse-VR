using System;
using System.Collections.Generic;

namespace Blockiverse.UI.Toolkit
{
    // Why this exists: every setting it checks is silent when wrong. A world-space UI Toolkit panel
    // with the wrong input redirection, a missing XRUIToolkitManager, or a collider on a layer the
    // ray does not raycast renders perfectly and ignores the controller. There is no exception and
    // no warning — the panel just does nothing, which reads as "UI Toolkit does not work in VR".
    // ADR 0010 §2 makes this validator non-optional for exactly that reason.
    public enum XrUiToolkitIssue
    {
        // Scene infrastructure (XRI: ui-world-space-ui-toolkit-support.md, "Scene Configuration").
        ToolkitManagerMissing,
        ToolkitManagerDuplicated,
        PanelInputConfigurationMissing,
        PanelInputConfigurationDuplicated,
        PanelInputRedirectionNotNever,
        WorldSpaceInputDisabled,
        UiToolkitEventsBypassed,

        // Rig.
        NoUiRayInScene,
        UiRayRaycastsNothing,

        // Per-panel (XRI: "Creating World Space UI Toolkit Panel").
        PanelSettingsMissing,
        PanelSettingsNotWorldSpace,
        DocumentColliderMissing,
        DocumentColliderNotRaycastable,
        HiddenDocumentStillRaycastable,
    }

    public readonly struct XrUiToolkitFinding
    {
        public XrUiToolkitFinding(XrUiToolkitIssue issue, string subject, string detail)
        {
            Issue = issue;
            Subject = subject;
            Detail = detail;
        }

        public XrUiToolkitIssue Issue { get; }

        // The panel name, or null for scene-wide findings.
        public string Subject { get; }

        public string Detail { get; }

        public override string ToString() =>
            Subject == null ? $"{Issue}: {Detail}" : $"{Issue} [{Subject}]: {Detail}";
    }

    // One world-space UI Toolkit panel, reduced to the facts that can be wrong.
    public readonly struct XrUiToolkitPanelState
    {
        public XrUiToolkitPanelState(
            string name,
            bool visible,
            bool hasPanelSettings,
            bool panelSettingsIsWorldSpace,
            bool hasCollider,
            bool colliderIsEffective,
            int colliderLayer,
            bool isNested = false)
        {
            Name = name;
            Visible = visible;
            HasPanelSettings = hasPanelSettings;
            PanelSettingsIsWorldSpace = panelSettingsIsWorldSpace;
            HasCollider = hasCollider;
            ColliderIsEffective = colliderIsEffective;
            ColliderLayer = colliderLayer;
            IsNested = isNested;
        }

        public string Name { get; }

        // Whether the panel is currently meant to be shown.
        public bool Visible { get; }

        public bool HasPanelSettings { get; }
        public bool PanelSettingsIsWorldSpace { get; }
        public bool HasCollider { get; }

        // Enabled AND on an active GameObject. Collider.enabled stays true when its GameObject is
        // deactivated, but a collider on an inactive object is not in the physics scene and
        // intercepts nothing — reporting it would be an unclearable false positive whose only
        // "fix" is disabling something already inert.
        public bool ColliderIsEffective { get; }

        public int ColliderLayer { get; }

        // A UIDocument nested under a parent document (Unity's composition model). Unity itself
        // never gives a nested document a collider — UIDocument.UpdateWorldSpaceCollider returns
        // early when parentUI is set — so collider rules do not apply to it.
        public bool IsNested { get; }
    }

    public readonly struct XrUiToolkitSceneState
    {
        public XrUiToolkitSceneState(
            int enabledToolkitManagerCount,
            int enabledPanelInputConfigurationCount,
            bool panelInputRedirectionIsNever,
            bool processWorldSpaceInput,
            bool hasUguiInputModule,
            bool bypassUiToolkitEvents,
            bool hasUiRay,
            int rayRaycastLayerMask,
            IReadOnlyList<XrUiToolkitPanelState> panels)
        {
            EnabledToolkitManagerCount = enabledToolkitManagerCount;
            EnabledPanelInputConfigurationCount = enabledPanelInputConfigurationCount;
            PanelInputRedirectionIsNever = panelInputRedirectionIsNever;
            ProcessWorldSpaceInput = processWorldSpaceInput;
            HasUguiInputModule = hasUguiInputModule;
            BypassUiToolkitEvents = bypassUiToolkitEvents;
            HasUiRay = hasUiRay;
            RayRaycastLayerMask = rayRaycastLayerMask;
            Panels = panels ?? Array.Empty<XrUiToolkitPanelState>();
        }

        // Enabled instances only. PanelInputConfiguration.current is assigned in OnEnable and
        // cleared in OnDisable, so a disabled instance is not merely redundant — it is absent, and
        // counting it would let the one configuration in a scene sit disabled while the validator
        // reported the scene clean.
        public int EnabledToolkitManagerCount { get; }
        public int EnabledPanelInputConfigurationCount { get; }

        public bool PanelInputRedirectionIsNever { get; }
        public bool ProcessWorldSpaceInput { get; }

        // True while the legacy uGUI menus still exist in the scene. XRI requires
        // bypassUIToolkitEvents to be OFF only in that mixed configuration.
        public bool HasUguiInputModule { get; }
        public bool BypassUiToolkitEvents { get; }

        // Whether any UI-enabled ray exists at all, kept separate from the mask so that "no rig in
        // this fixture" and "a rig whose rays raycast Nothing" are distinguishable. Collapsing them
        // to mask == 0 lets a rig that can hit nothing report clean.
        public bool HasUiRay { get; }

        // The Unity *physics* layer mask the controller rays cast against
        // (BlockiverseProject.VrUiRaycastLayerMask). A panel collider outside this mask is unhittable.
        public int RayRaycastLayerMask { get; }

        public IReadOnlyList<XrUiToolkitPanelState> Panels { get; }
    }

    // Pure: no UnityEngine types, no scene access, no statics. Everything here is decided from the
    // snapshot it is handed, so the rules are unit-testable without a scene or a headset.
    public static class XrUiToolkitConfigurationValidator
    {
        public static IReadOnlyList<XrUiToolkitFinding> Validate(XrUiToolkitSceneState state)
        {
            var findings = new List<XrUiToolkitFinding>();

            ValidateInfrastructure(state, findings);
            ValidateRig(state, findings);

            foreach (XrUiToolkitPanelState panel in state.Panels)
                ValidatePanel(panel, state, findings);

            return findings;
        }

        static void ValidateInfrastructure(XrUiToolkitSceneState state, List<XrUiToolkitFinding> findings)
        {
            if (state.EnabledToolkitManagerCount == 0)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.ToolkitManagerMissing,
                    null,
                    "No enabled XRUIToolkitManager. XRUIToolkitHandler.uiToolkitSupportEnabled stays false, " +
                    "so XRI interactors ignore every UI Toolkit panel in the scene."));
            }
            else if (state.EnabledToolkitManagerCount > 1)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.ToolkitManagerDuplicated,
                    null,
                    $"{state.EnabledToolkitManagerCount} enabled XRUIToolkitManager components. " +
                    "The component is [DisallowMultipleComponent] per GameObject but not per scene, and " +
                    "each one's OnDisable turns UI Toolkit support off globally — so disabling either " +
                    "kills input for both."));
            }

            if (state.EnabledPanelInputConfigurationCount == 0)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.PanelInputConfigurationMissing,
                    null,
                    "No enabled PanelInputConfiguration. PanelInputConfiguration.current is assigned in " +
                    "OnEnable, so a disabled one does not count — without an enabled instance the " +
                    "EventSystem interferes with UI Toolkit input."));

                // The two settings below are read off a component that does not exist. Reporting
                // them as well would be three findings for one cause, and would bury the one that
                // names the fix.
                return;
            }

            if (state.EnabledPanelInputConfigurationCount > 1)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.PanelInputConfigurationDuplicated,
                    null,
                    $"{state.EnabledPanelInputConfigurationCount} enabled PanelInputConfiguration components. " +
                    "First-to-enable wins and Unity disables the loser at runtime, so which settings " +
                    "apply depends on scene load order."));
            }

            if (!state.PanelInputRedirectionIsNever)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.PanelInputRedirectionNotNever,
                    null,
                    "Panel Input Redirection must be PanelInputRedirection.Never (\"No input redirection\"). " +
                    "Any other value lets the EventSystem interfere with UI Toolkit input."));
            }

            if (!state.ProcessWorldSpaceInput)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.WorldSpaceInputDisabled,
                    null,
                    "Process World Space Input is off, so world-space panels receive no input at all."));
            }

            // Only meaningful while both UI systems coexist. Once uGUI is gone (Phase 6) this rule
            // stops applying, so it is gated on the uGUI module actually being present rather than
            // asserted unconditionally and then becoming a permanent false positive nobody can clear.
            if (state.HasUguiInputModule && state.BypassUiToolkitEvents)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.UiToolkitEventsBypassed,
                    null,
                    "XRUIInputModule.bypassUIToolkitEvents is on while uGUI and UI Toolkit coexist. " +
                    "XRI requires it off in mixed scenes."));
            }
        }

        static void ValidateRig(XrUiToolkitSceneState state, List<XrUiToolkitFinding> findings)
        {
            if (!state.HasUiRay)
            {
                // Not necessarily wrong — an isolated panel fixture legitimately has no rig. It is
                // reported so that "no layer findings" is never mistaken for "layers checked and
                // fine"; the panel layer rules below are skipped in this state.
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.NoUiRayInScene,
                    null,
                    "No XRRayInteractor with enableUIInteraction. Panel collider layers cannot be " +
                    "checked, because there is no raycast mask to check them against."));
                return;
            }

            if (state.RayRaycastLayerMask == 0)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.UiRayRaycastsNothing,
                    null,
                    "Every UI-enabled ray has an empty raycastMask, so no panel on any layer can be " +
                    "pointed at."));
            }
        }

        static void ValidatePanel(
            XrUiToolkitPanelState panel,
            XrUiToolkitSceneState state,
            List<XrUiToolkitFinding> findings)
        {
            if (!panel.HasPanelSettings)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.PanelSettingsMissing,
                    panel.Name,
                    "UIDocument has no PanelSettings, so it renders nowhere."));
            }
            else if (!panel.PanelSettingsIsWorldSpace)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.PanelSettingsNotWorldSpace,
                    panel.Name,
                    "PanelSettings.renderMode is ScreenSpaceOverlay. In a VR player that draws the panel " +
                    "over both eyes as a flat overlay rather than in the world."));
            }

            // Unity never gives a nested document a collider, so demanding one produces a permanent
            // finding a composed screen cannot clear.
            if (panel.IsNested)
                return;

            if (!panel.HasCollider)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.DocumentColliderMissing,
                    panel.Name,
                    "World-space UI Toolkit panels need a Collider for ray interaction."));
                return;
            }

            // Deliberately NOT gated on Visible. The migration's end state is routed screens that
            // ship disabled until routed, and a wrong layer on one of those would be suppressed at
            // author time and only surface when a player opens the screen. A layer is wrong
            // regardless of whether the panel happens to be showing right now.
            if (state.HasUiRay && state.RayRaycastLayerMask != 0 &&
                (state.RayRaycastLayerMask & (1 << panel.ColliderLayer)) == 0)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.DocumentColliderNotRaycastable,
                    panel.Name,
                    $"Collider is on layer {panel.ColliderLayer}, which is outside the ray raycast mask " +
                    $"0x{state.RayRaycastLayerMask:X}. The panel renders but cannot be pointed at."));
            }

            if (!panel.Visible && panel.ColliderIsEffective)
            {
                findings.Add(new XrUiToolkitFinding(
                    XrUiToolkitIssue.HiddenDocumentStillRaycastable,
                    panel.Name,
                    "Panel is hidden but its collider is still live, so it intercepts world rays " +
                    "aimed at whatever is behind it."));
            }
        }
    }
}
