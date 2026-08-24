# ADR 0010: UI Toolkit Is The Runtime UI Framework

## Status

Accepted 2026-08-22. Supersedes the framework half of [ADR 0006](0006-quest-openxr-rendering-and-asset-policy.md)'s
2026-08-13 menu amendment; that amendment's *placement* findings survive intact and are restated below.

**Amended 2026-08-23 — Phases 2–5 implemented.** All 25 screen documents, their controllers,
and the host/coordinator seam shipped in one branch at Eric's direction (single batch gate
rather than per-phase gates). The shape differs from the phase plan in three deliberate ways:

- **The rig prefab is untouched.** UI Toolkit panels are generated into the Boot scene only
  (`BlockiverseProjectBootstrapper.UiToolkitMenus.cs`, table-driven from `[UiToolkitScreen]`
  attributes), so the #324 unreviewable-prefab-diff failure mode cannot recur. The uGUI menus
  remain in the rig prefab as the development fallback.
- **`BlockiverseMenuController` is not yet dismantled.** During coexistence it remains the
  coordinator; a registered `IBlockiverseMenuFrontend` (implemented by `UiToolkitMenuHost`)
  receives every outward push and answers every pending-state read, and its presence hides
  every uGUI presenter. Disabling the host component is the whole fallback switch. The
  §5 redistribution completes at cutover, when the uGUI half is deleted.
- **Zilla Slab and Barlow are embedded** (OFL 1.1, license files beside the .ttfs in
  `Assets/Blockiverse/UI/Fonts/`), wired in `Base.uss`; weight comes from the SemiBold faces,
  not the bold style flag. Non-Latin fallback (matrix §3.2) remains open. Tabular figures are
  an OpenType feature USS cannot enable — measured judgement deferred to headset.

One validator exemption was added with the HUD family: a `NonInteractive` screen declaration
generates **no collider at all** (mining bar, status toast — read-only strips sharing the
routed gameplay screen; an enabled trigger collider would intercept the XRI ray all session).

**Amended 2026-08-23 — the cutover happened, ahead of device validation, at Eric's direction.**

The amendment above said the cutover "happens only after device validation." That condition was
not met. Eric asked for the uGUI deletion on the migration branch as one revertible commit, was
told the keyboard risk and the missing device evidence, and confirmed. Recording the contradiction
rather than quietly restating the rule: the ordering this ADR argued for was overridden by the
project owner, deliberately and with the trade-off stated.

What that costs is specific. The fallback this ADR leaned on is gone. "Disable the host component"
is no longer a switch back to a working menu stack, because there is no second stack — the revert
path is `git revert` of one commit, which is coarser (it takes the whole removal, including the
behaviour ports below) and only available before further work lands on top.

What was removed: the 20 routed uGUI panels and the Survival HUD, their bootstrapper generators,
17 panel classes, and the dual-backend mirroring inside `BlockiverseMenuController`. The rig prefab
went 96,949 → 6,760 lines (3,519 → 179 serialized objects); `MultiplayerTest.unity` 3,020 → 1,031,
verified as a clean deletion with every surviving fileID preserved, so no external reference into
either file was invalidated.

What deliberately survives, and why, because each looks like an oversight and is not:

- **`IBlockiverseMenuFrontend`.** Not a shim. `UiToolkitMenuHost` implements it, and it is how the
  router is initialised outside Play mode — nine Toolkit test fixtures depend on that. Only the
  dual-backend *mirroring* died.
- **`BlockiverseWorldSpacePanelPresenter`, `BlockiverseStartupOverlay`, `BlockiverseLocalizedText`,
  `BlockiverseTmpFontFallbackBootstrapper`.** The boot splash and the Block Menu stay uGUI; the
  Block Menu is not a menu at all despite the name, it carries the scene `CreativeHotbar` that
  decides which block gets placed.
- **`Blockiverse.UI.asmdef` keeps `UnityEngine.UI` and `Unity.TextMeshPro`** for exactly those.

Three behaviours had to be ported *out* of the menus before the menus could go, because each was
gameplay wiring that had lodged in a UI component and would have failed silently:

- Container auto-loot. `SurvivalHudController.Bind` was the only caller in the repository of
  `CreativeWorldManager.SetActivePlayerInventory`; without it, breaking a crate would have deleted
  its contents with nothing going red. Now resolved on demand from the survival sync, and pinned by
  `ContainerAutoLootEditModeTests`.
- The item icon library, generated only by the uGUI survival-HUD builder but resolved by two
  surviving Toolkit screens.
- Keyboard hand-visibility, which read a static event on a uGUI component. It now reads
  `TouchScreenKeyboard.visible`, which is what it always meant.

One structural hazard is worth carrying forward because it bit twice: **the bootstrapper only ever
*ensures*.** `EnsureXrRigPrefab` re-authors the existing prefab, so deleting a generator does not
delete what it generated — the objects stay serialized forever. Removal has to be explicit
(`RetiredUguiMenuPanelNames`), and for a component riding an object that *survives*, removal has to
be by missing-script rather than by type, since the type is gone.

**The Open section still stands: there is still no headset evidence.** Device validation is now the
only thing between this and a shipped regression, and it must cover text entry first — naming a
world, entering a seed, typing a LAN address — because UI Toolkit's `TouchScreenKeyboard` path over
a world-space panel has never been exercised on a Quest in this project.

## Context

Blockiverse VR's runtime menus, dialogs and HUD are uGUI: world-space `Canvas` +
TextMeshPro, generated by the bootstrapper, driven by `BlockiverseMenuController`.
That line is headset-validated and shipping. Nothing here says it is broken.

What it is, is unmaintainable in the specific way that matters for the remaining
screens. `BlockiverseMenuController` is 50 KB and holds router lifecycle, presenter
discovery, uGUI/TMP references, input subscriptions, route application, first-run
flows, per-screen mutable state, session commands, save-list state, LAN state and
modal state in one type. Every new screen widens it. The layouts are built in C#,
so there is no artefact a person can look at to know what a screen contains.

### The previous attempt, and why it is not evidence against UI Toolkit

UI Toolkit has been tried here once. [PR #324](https://github.com/eslutz/Blockiverse-VR/pull/324)
(`715007a6`, 2026-06-26) migrated the in-game menus and was reverted by `fe73bdbf`
(#325, 2026-08-18). At the baseline commit `4251dcca` the tree contains **zero**
`.uxml`, `.uss` or `PanelSettings` assets; the only ones present now are the Phase 1
proof scaffolding this ADR introduces.

The revert is easy to misread as a verdict on the framework, and the honest reading
matters more than the convenient one — so it is worth stating what #324 did **not**
lack:

- It was **not** on the wrong platform. `git show 715007a6:Packages/manifest.json`
  bumps XRI from 3.3.2 to **3.5.1** inside #324 itself, and its editor is
  **6000.3.18f1** — Unity 6.3. Both documented minimums for native world-space
  UI Toolkit (Unity 6.2, XRI 3.2.0) were already met, and so was the 3.4.0 in which
  ray-based support actually landed. An earlier draft of this ADR claimed the
  opposite; it was wrong.

What #324 actually lacked was **isolation and device validation**, and it lost a
mainline reconciliation on exactly that basis:

- It was not a UI change. `git show --stat 715007a6` is a cross-project diff: a
  76,309-line rewrite of `BlockiverseXRRig.prefab`, 4,872 lines of `Boot.unity`, the
  deletion of `BlockiverseProjectBootstrapper.CompositionLayers.cs`, CI workflow
  edits, art assets, and an Android Gradle postprocessor. Its own controller went
  from 43,473 to 87,266 bytes in one commit. Nothing in that diff can be attributed.
- Meanwhile a parallel line of work had stabilised the *uGUI* menus on direct
  world-space canvases and had **headset verification** for it — tagged
  `quest-menu-good-2026-07-02`, recorded in ADR 0006's 2026-08-13 amendment.
- #325 reconciled the two and kept the validated one. Its message is explicit:
  "revert UI Toolkit migration, land validated world-space menu line". #324's
  Quest alpha upload had failed; the uGUI line had a known-good build.

So the lesson from #324 is about diff scope and about validating on hardware before
a line of work becomes load-bearing — not about UI Toolkit, which was never given a
fair test. That is precisely why this ADR's Phase 1 is a standalone proof panel that
touches no production screen and must pass on a headset before anything else moves.

#324's assets may be read for screen inventory, wording and test cases. It must not
be cherry-picked or replayed.

### What is actually available now

Verified firsthand against the installed packages and the Unity 6000.5.8f1 editor
assemblies, not from documentation alone:

| Fact | Evidence |
|---|---|
| `PanelRenderMode.WorldSpace = 1` | `UnityEngine.UIElementsModule.dll`, IL disassembly |
| `WorldSpaceSizeMode { Dynamic = 0, Fixed = 1 }` | same |
| `ColliderUpdateMode { MatchBoundingBox = 0, Keep = 1, MatchDocumentRect = 2 }` | same; enum is `private`, reachable only through the serialized field |
| `PanelInputConfiguration.PanelInputRedirection { AutoSwitch = 0, Never = 1, Always = 2 }` | same; `Never` carries `[InspectorName("No input redirection")]` |
| `XRUIToolkitManager` exists and only toggles `XRUIToolkitHandler.uiToolkitSupportEnabled` | `Runtime/UI/XRUIToolkitManager.cs`, 32 lines |
| `UIInputModule.bypassUIToolkitEvents` exists | `Runtime/UI/UIInputModule.cs:131`, consumed at `:333` |
| Minimum Unity 6.2 / XRI 3.2.0 | `Documentation~/ui-world-space-ui-toolkit-support.md` |

The project is on Unity 6000.5.8f1 and XRI 3.5.1, past both minimums. A working
reference configuration ships in the XRI **World Space UI** sample and is recorded
in [the migration matrix](../ui/menu-migration-matrix.md#appendix-a--verified-native-configuration).

## Decision

### 1. UI Toolkit is the only runtime UI framework for Blockiverse-owned surfaces

All Blockiverse menus, dialogs, HUD elements, hotbars and interactive overlays move
to UI Toolkit: UXML for static structure, USS for presentation, C# controllers for
behaviour. uGUI survives only as a temporary, development-only fallback behind a
backend switch, and is removed in the cutover phase.

This is scoped to *Blockiverse-owned* surfaces. It is not licence to remove
`com.unity.ugui` or the `EventSystem`, which other packages may depend on.

### 2. Interaction is native XRI, and custom input code is out of bounds

The panel is a `UIDocument` plus a `Collider`, with `PanelSettings.renderMode =
WorldSpace`. Input arrives through `XRUIToolkitManager` and a `PanelInputConfiguration`
with `panelInputRedirection = Never`. No RenderTexture compositor, no pointer-event
synthesis, no ray-to-panel projection, no parallel input module.

`Never` is not a stylistic preference: XRI's own documentation states the Event
System interferes with UI Toolkit input unless redirection is off, and the sample
scene ships `m_PanelInputRedirection: 1`. While uGUI and UI Toolkit coexist,
`XRUIInputModule.bypassUIToolkitEvents` must stay **disabled**. Both settings get an
automated configuration test rather than a comment, because both are silent when wrong:
the failure mode is a panel that renders correctly and ignores the controller.

### 3. Routed menus stay direct world-space surfaces; composition layers stay off them

ADR 0006's 2026-08-13 amendment established, after on-headset failures, that routed
menus are direct world-space surfaces and composition layers are retained **only**
for the startup splash. That finding is about the *surface*, not the *framework*,
and it carries over unchanged. The startup splash keeps its composition-layer path.

### 4. The router and the ID vocabulary are the compatibility contract, and they do not move

`UiScreenRouter` is 138 lines of plain C# with no `UnityEngine` UI dependency —
two stacks, six mutations, four derived properties. It already models everything the
migration needs and is framework-neutral by construction. It is retained as-is.

`MenuActions`' 22 screen IDs and 39 action IDs are the persistence-and-wire vocabulary
of the menu system, and they are retained verbatim. Renaming them for aesthetics is
explicitly out of scope; a typed wrapper, if one is ever wanted, wraps these strings
rather than replacing them.

This is what makes the migration reviewable: at every commit, the same route IDs
resolve and the same action IDs dispatch, so parity is a testable property rather
than a judgement call.

### 5. Screens are declarative, and the controller is dismantled rather than ported

Each screen is its own UXML document with a dedicated controller. There is no generic
shell that every screen fills in — that was #324's shape and it is how a 50 KB
controller becomes an 87 KB one.

`BlockiverseMenuController`'s responsibilities are redistributed: a framework-neutral
`BlockiverseUiCoordinator` owns routing and domain commands and never touches a
`VisualElement`; a `UiToolkitMenuHost` owns the document, layers and screen lifecycle;
per-screen controllers own their own elements and nothing else.

### 6. Scene changes go through the bootstrapper, in a new partial

UI Toolkit scene generation lands in a **new** `BlockiverseProjectBootstrapper.UiToolkit.cs`
rather than in the existing menu partials. The existing partials are large
(`Menus.cs` 74 KB, `GameMenus.cs` 64 KB) and under concurrent edit by other work;
a new file keeps the legacy and replacement paths independently reviewable and
avoids merge conflicts during the phases where both exist.

### 7. No new third-party dependencies

Nothing in this migration adds a UI framework, reactive library, tweening package,
DI container, event bus or accessibility package. A new dependency requires a written
gap analysis naming the missing Unity capability, why Meta does not provide it, and
an exit strategy.

### 8. The visual language is Hearthstone, and it is a decision rather than an inheritance

Until 2026-08-22 this project had never made a design decision. What shipped was what
accumulated: TextMesh Pro's default LiberationSans, a charcoal-and-teal palette written inline in
the bootstrapper, and a sprite generator drawing a 2px border because something had to be drawn.
An earlier draft of this ADR treated that as a design system to honour, which was wrong — it
laundered defaults into doctrine.

Four directions were drawn and **Hearthstone** was chosen (Eric, 2026-08-22): the panel hewn from
the world's own material, sandstone and basalt faces with 3px extruded edges lit from above, and a
single ember light. Its tokens live in `Assets/Blockiverse/UI/Styles/Tokens.uss`; the shared
presentation in `Base.uss`.

Three properties are load-bearing rather than decorative:

- **Extrusion, not tint.** Each surface is lit on its top and left edges and shadowed on its right
  and bottom, so a control reads as a face of a block. This is why there are four border colours
  per surface. The pressed state *inverts* that light rather than tinting, which is the only state
  that survives being looked at from an angle — a VR panel is rarely viewed square on.
- **Measured contrast, not judged.** The direction's own risk note said warm-on-warm runs low
  contrast, and it did: eight foreground/background pairs failed 4.5:1 in the first pass, including
  disabled text at 2.91:1. The surfaces were darkened rather than the palette washed out. All 25
  shipping pairs now clear it, worst case 5.20:1. `check-contrast.py` in the design working set
  recomputes this; any new pair must be run through it.
- **Four signals, none of which works alone.** Ember (act), moss (confirmed), ochre (refused),
  oxide (rejected), each shipping in two values because the fill tones are too dark to read as
  type. Every signal also carries an icon and a word — an accessibility floor, and also the only
  thing that reliably separates ember from oxide at 0.95 m, since both are warm.

Type is Zilla Slab for anything with a name and Barlow for everything else, with Barlow's tabular
figures carrying every quantity rather than importing a monospace. A third family would be a third
thing to embed in the font atlas.

**What this costs, and it is not nothing:** the generated sprite set (panels, hotbar frame, slot,
pip, toast) is drawn by `scripts/art/generate-art-assets.py` from hard-coded colours and must be
regenerated; the runtime palette lives inline in the bootstrapper and the scene is generated from
it, so the change lands as a regenerated scene and prefab rather than a stylesheet edit; and
shipping a real typeface means embedding it and rebuilding its atlas, plus a non-Latin fallback
story that the TMP default currently provides for free.

**Unvalidated:** none of this has been seen in a headset, which is the only test that settles a
typeface or a contrast ratio at 0.95 m.

## Consequences

- **Both UI systems exist simultaneously for several phases.** This is deliberate —
  it is what allows per-screen device comparison — but it means the scene carries an
  `EventSystem`, an `XRUIInputModule`, a `PanelInputConfiguration` and an
  `XRUIToolkitManager` at once, in a configuration where two of the four settings are
  silently wrong-able. The configuration validator is not optional.
- **UXML holds literal text, and the project has a localization layer.** Every uGUI
  label today resolves through `BlockiverseLocalization.Text(key, fallback)`, and nearly
  every `MenuActions` factory entry carries a `Keys.*` label key — the exception is
  `MenuActions.Confirm(confirmLabel, cancelLabel)`, which builds keyless actions when a
  caller supplies custom labels. UXML text is static by
  nature, so each screen controller must apply localized strings after cloning the
  document. This is a real cost of the migration and is called out per-screen in the
  matrix rather than discovered later.
- **Physical panel size is now computed, not a Canvas scale constant.** Width in metres
  is `UIDocument.worldSpaceSize.x / PanelSettings.pixelsPerUnit * transform.localScale.x`.
  The current Canvas scale constant must not be copied across; panel dimensions are
  re-derived and re-validated in headset.
- **Tests split into two populations.** Framework-neutral tests (route IDs, action
  dispatch, router semantics, session behaviour) must keep passing untouched — they
  are the parity contract. uGUI-specific tests (component counts, Canvas presence,
  TMP text values) are rewritten against UI Toolkit equivalents, never deleted to
  make a phase pass.
- **`Run()` switches the active build target to Android** via `ConfigureAndroidPlayer`.
  Any bootstrapper work that regenerates menus inherits that, and a run started on a
  non-Android target will silently switch it.
- **Device validation cannot be deferred to the end.** Each phase validates on device
  before the next begins. Simulator-only validation is explicitly not sufficient, and
  the acceptance matrix covers Quest 3 and Quest 3S separately because 3S is the
  lower-performance target.

## Open

- **No device validation exists yet for any of this.** This ADR ratifies a direction
  and records a verified API surface; it records no headset evidence. The Phase 1 proof
  panel is what converts it from reasoned to measured, and until that proof passes on
  hardware, no production screen may be migrated.
- **Quest 3S is not currently attachable.** Only a Quest 3 (`eureka`) is connected.
  Every claim in this migration about 3S is unvalidated until a 3S is available, and
  3S is the target that decides the performance questions.
- **No UI performance baseline exists.** `docs/testing/performance/` still holds no
  committed capture, so "no sustained regression" has nothing to compare against yet.
  Capturing that baseline is Phase 0 work and is tracked in
  [the baseline document](../testing/ui-toolkit-migration-baseline.md).
- **Dynamic atlas sizing is a guess.** The XRI sample ships `m_MaxAtlasSize: 4096`;
  this project starts at 2048 on memory-constrained Quest hardware. That number is
  reasoned from Unity's mobile guidance, not measured against this project's icon
  inventory.
