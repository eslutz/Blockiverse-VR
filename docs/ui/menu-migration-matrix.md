# UI Toolkit Menu Migration Matrix

Companion to [ADR 0010](../adr/0010-ui-toolkit-runtime-ui.md). This is the per-screen migration
checklist: what exists, where it goes, and what must not be lost on the way.

**Status: cut over (2026-08-23). UI Toolkit is the only menu frontend; device validation still
pending.** 24 of the 25 documents in §2 exist with dedicated controllers under
`Assets/Blockiverse/Scripts/UI/ToolkitScreens/`, and the Boot scene generates one world-space
panel per `[UiToolkitScreen]` declaration. The LAN screen additionally gained the saved-servers
bookmark rows (previously a dead API — see §2 note on row 14).

The uGUI menus are **gone**: 20 routed panels and the Survival HUD removed from the rig prefab
(96,949 → 6,760 lines), their bootstrapper generators deleted, 17 panel classes deleted, and the
dual-backend mirroring taken out of `BlockiverseMenuController`. Eric directed this ahead of
device validation; [ADR 0010](../adr/0010-ui-toolkit-runtime-ui.md) records the overridden
ordering and what it costs.

Two consequences that change how to read the rest of this document:

- **There is no fallback switch any more.** "Disable `UiToolkitMenuHost`" used to hand the menus
  back to uGUI. It does not; nothing else draws them. The way back is reverting the removal commit.
- **`IBlockiverseMenuFrontend` stayed.** It is not the shim it looks like — `UiToolkitMenuHost`
  implements it, and it is how the router is initialised outside Play mode, which nine Toolkit
  test fixtures rely on. Only the *dual-backend mirroring* died with the panels.

Some world-space uGUI deliberately survives and is not an oversight: the boot splash
(`BlockiverseStartupOverlay`), and the scene `CreativeHotbar` component, which despite the name
is not a menu — it decides which block gets placed and is read/written by `CatalogScreenController`
and `HotbarStripController`. `Blockiverse.UI.asmdef` therefore still references `UnityEngine.UI`
and `Unity.TextMeshPro`.

**Row 24 retired (2026-08-26).** The Creative-only quick block menu (`CreativeHotbarController`,
`CreativeHotbar.uxml`) duplicated the block catalog already reachable from the wrist menu, so it
was deleted rather than kept as a 25th screen. Arbitrary block/tool selection now happens either
by ray + trigger directly on the hotbar strip (row 21's `GameplayHudController` family) or via the
catalog screen (row 17).

### The HUD is migrated, not redesigned (2026-08-25)

Rows 21-24 are cut over and the panels are real, but they are **ports**. The controllers say so
themselves — `StatusToastController` keeps "`SurvivalHudController`'s `SetStatusText` semantics",
`GameplayStatsController` is "uGUI: `SurvivalHealthPanel` bound by `SurvivalHudController`". The one
design change since is the two-panel split, made after Eric reported the centred panel blocked his
view.

That is worth stating because "the HUD is done" and "the HUD follows the FPV research report" are
different claims, and only the first is true. The report Eric supplied on 2026-08-24 asks for a
persistent hotbar strip, vitals as meters rather than a sentence, prioritised transient messages,
and a small view-referenced comfort cue. None of those shipped in #344. The gap is enumerated in
[ADR 0010's 2026-08-25 amendment](../adr/0010-ui-toolkit-runtime-ui.md#amendment-2026-08-25-the-hud-against-the-fpv-research-report),
along with three findings that hold regardless — chief among them that this game aims with the
**controller**, so a centre-of-view reticle would be actively misleading and must not be added.

### A per-screen stylesheet is reachable only by document name

The bootstrapper loads `Styles/Screens/<documentName>.uss`, and the per-screen sheet is optional by
design. So splitting a document without moving its rules leaves every one of the new document's
classes inert, rendering an unstyled panel that reports itself healthy. `e19de17e` did exactly that:
`GameplayStats.uxml` uses `gh-*`, which live in `GameplayHud.uss`, and `GameplayStats.uss` does not
exist — its health bar has no height and no fill colour.

`HudFamilyEditModeTests` did not catch it because it asserts `fill.style.width`, the *inline* value
the controller writes, and inline styles resolve with or without a sheet.
`ScreenStyleSheetReachabilityEditModeTests` now fails on any class a document uses that is defined
in another screen's sheet. **When splitting a document, move its USS rules into a sheet named for
the new document.**

Line references are against `main` at `4251dcca` and will drift; treat them as "look here", not as
addresses.

---

## 1. Contract that must survive every phase

`Assets/Blockiverse/Scripts/UI/MenuActions.cs` defines **22 screen IDs** and **39 action IDs**.
`Assets/Blockiverse/Scripts/UI/UiScreenRouter.cs` is 138 lines of plain C# with no UI-framework
dependency. Both are retained verbatim (ADR 0010 §4).

### Screen IDs

| Constant | Value | Destination |
|---|---|---|
| `TitleScreen` | `title_menu` | `TitleScreen.uxml` |
| `NewWorldScreen` | `new_world` | `NewWorldScreen.uxml` |
| `LoadWorldScreen` | `load_world` | `LoadWorldScreen.uxml` |
| `WorldDetailsScreen` | `world_details` | `WorldDetailsScreen.uxml` |
| `WorldLoadingScreen` | `world_loading` | `WorldLoadingScreen.uxml` |
| `ControllerMappingScreen` | `controller_mapping` | `ControllerMappingScreen.uxml` |
| `GameplayHudScreen` | `gameplay_hud` | `GameplayHud.uxml` (wrist menu) + the HUD family |
| `GameplayScreensScreen` | `gameplay_screens` | `GameplayScreensScreen.uxml` (pause fallback hub) |
| `PauseScreen` | `pause_menu` | `PauseScreen.uxml` |
| `SettingsScreen` | `settings` | `SettingsHubScreen.uxml` |
| `ComfortSettingsScreen` | `settings_comfort` | `ComfortSettingsScreen.uxml` |
| `AudioSettingsScreen` | `settings_audio` | `AudioSettingsScreen.uxml` |
| `ControlsScreen` | `controls` | `ControlsScreen.uxml` |
| `CreativeToolsScreen` | `creative_tools` | `CreativeToolsScreen.uxml` |
| `DeathScreen` | `death_screen` | `DeathScreen.uxml` |
| `LanMultiplayerScreen` | `lan_multiplayer` | `LanMultiplayerScreen.uxml` |
| `StationMenuScreen` | `station_menu` | `StationScreen.uxml` |
| `ConfirmModal` | `confirm_dialog` | `ConfirmDialog.uxml` (modal layer) |
| `ErrorModal` | `error_dialog` | `ErrorDialog.uxml` (modal layer) |
| `InventoryScreen` | `inventory` | `InventoryScreen.uxml` |
| `CraftingScreen` | `crafting` | `CraftingScreen.uxml` |
| `CatalogScreen` | `catalog` | `CatalogScreen.uxml` |
| `StationCrateScreen` | `station_crate` | `CrateScreen.uxml` |

### Action ID inconsistencies that must be preserved

Three action IDs do not follow their neighbours' convention. They are load-bearing strings, so
ADR 0010 §4 forbids renaming them for tidiness — but someone will want to, so they are recorded
here as deliberate:

| Constant | Value | Why it looks wrong |
|---|---|---|
| `ErrorClose` | `error_dialog.close` | prefixed with the full screen ID, where `ConfirmAccept`/`ConfirmCancel` use the short `confirm.` |
| `CreativeToolsClose` | `creative_tools.close` | declared under the Pause actions comment block, not with a creative-tools group |
| `LoadWorldDetails` | `load_world.open_details` | declared in its own trailing block rather than with the other `load_world.*` actions |

**Modal vs screen is not declarative.** There is no set, enum or attribute. A modal is a bare
`string` on `modalStack`; a screen is a `ScreenRoute` on `screenStack`. The two IDs that are
*only* ever pushed as modals are `ConfirmModal` and `ErrorModal`, and that is a convention held
in the call sites, not a property of the ID. The screen catalog introduced in Phase 2 is the first
place this becomes checkable — hence the validation rule "modal IDs are not registered as full
screens".

---

## 2. Per-screen migration

Phase numbers are from the migration spec. "Owner" is the controller that will own the screen.

| # | Current implementation | Destination | Phase | Owner |
|---|---|---|---|---|
| 1 | `BlockiverseActionMenu` (title instance) | `TitleScreen.uxml` | 2 | `TitleScreenController` |
| 2 | `BlockiverseActionMenu` (pause instance) | `PauseScreen.uxml` | 2 | `PauseScreenController` |
| 3 | `BlockiverseActionMenu` (confirm instance) | `ConfirmDialog.uxml` | 2 | `UiToolkitModalHost` |
| 4 | `BlockiverseActionMenu` (error instance) | `ErrorDialog.uxml` | 2 | `UiToolkitModalHost` |
| 5 | `BlockiverseProjectBootstrapper.Menus.cs` controller-mapping popup | `ControllerMappingScreen.uxml` | 3 | `ControllerMappingScreenController` |
| 6 | `BlockiverseStartupOverlay.cs` (105 lines) | `WorldLoadingScreen.uxml` | 3 | `WorldLoadingScreenController` |
| 7 | `BlockiverseNewWorldPanel.cs` (324) + `NewWorldConfig.cs` (116) | `NewWorldScreen.uxml` | 3 | `NewWorldScreenController` |
| 8 | `BlockiverseLoadWorldPanel.cs` (278) + `SaveListModel.cs` (134) | `LoadWorldScreen.uxml` | 3 | `LoadWorldScreenController` |
| 9 | `BlockiverseWorldDetailsPanel.cs` (69) | `WorldDetailsScreen.uxml` | 3 | `WorldDetailsScreenController` |
| 10 | `BlockiverseActionMenu` (settings instance) | `SettingsHubScreen.uxml` | 3 | `SettingsHubScreenController` |
| 11 | `BlockiverseComfortMenu.cs` (434) | `ComfortSettingsScreen.uxml` | 3 | `ComfortSettingsScreenController` |
| 12 | `BlockiverseAudioSettingsPanel.cs` (138) | `AudioSettingsScreen.uxml` | 3 | `AudioSettingsScreenController` |
| 13 | controls panel (`GameMenus.cs:467`) | `ControlsScreen.uxml` | 3 | `ControlsScreenController` |
| 14 | `BlockiverseMultiplayerSessionMenu.cs` (996) | `LanMultiplayerScreen.uxml` | 3 | `LanMultiplayerScreenController` |
| 15 | `SurvivalInventoryPanel.cs` (388) | `InventoryScreen.uxml` | 4 | `InventoryScreenController` |
| 16 | `SurvivalCraftingPanel.cs` (602) | `CraftingScreen.uxml` | 4 | `CraftingScreenController` |
| 17 | `BlockiverseCatalogBrowserPanel.cs` (203) | `CatalogScreen.uxml` | 4 | `CatalogScreenController` |
| 18 | `BlockiverseCreativeToolsPanel.cs` (558) | `CreativeToolsScreen.uxml` | 4 | `CreativeToolsScreenController` |
| 19 | `SurvivalCratePanel.cs` (192) | `CrateScreen.uxml` | 4 | `CrateScreenController` |
| 20 | `BlockiverseStationPanel.cs` (399) | `StationScreen.uxml` | 4 | `StationScreenController` |
| 21 | `SurvivalHealthPanel.cs` (147) | `GameplayHud.uxml` | 5 | `GameplayHudController` |
| 22 | `SurvivalHudController.cs` (412) mining slider | `MiningProgress.uxml` | 5 | `GameplayHudController` |
| 23 | `SurvivalHudController.cs` status label | `StatusToast.uxml` | 5 | `StatusToastController` |
| 24 | ~~`CreativeHotbar`~~ | ~~`CreativeHotbar.uxml`~~ | 5 | **retired 2026-08-26** — see note below |
| 25 | `BlockiverseActionMenu` (death instance) | `DeathScreen.uxml` | 5 | `DeathScreenController` |

Cross-cutting, not a screen:

| Current | Destination | Phase |
|---|---|---|
| `BlockiverseWorldSpacePanelPresenter.cs` (512) | `WorldSpaceUiPlacementController` | 2 |
| `BlockiversePanelPlacement.cs` (105, already pure) | reused unchanged | 2 |
| `BlockiverseMenuController.cs` (1078) | `BlockiverseUiCoordinator` + `UiToolkitMenuHost` + per-screen controllers | 2→6 |
| `BlockiverseWorldSessionController.cs` (1131) | retained; coordinator calls it | — |

---

## 3. Two gaps the migration spec does not anticipate

These were found reading the current code, not the spec, and both are cheap now and expensive later.

### 3.1 Localization is reverse-looked-up from literal English, and UXML breaks that

Today a generated label carrying literal English gets a localized binding **automatically**:
`BlockiverseLocalization.EnglishKeys` is an inverted display-text → key dictionary, and
`BlockiverseProjectBootstrapper.Menus.cs` (`ConfigureLocalizedTextBinding`) looks each authored
string up in it and attaches a `BlockiverseLocalizedText` only when a key is found.

UXML carries literal text in markup, so that upgrade path does not exist. Doing nothing yields
screens that are silently English-only — which will look correct to every reviewer, because the
fallback *is* English.

Options, to be decided before Phase 2 authors its first screen:
1. Put `ui.*` keys directly in UXML and resolve at bind time.
2. Reimplement the reverse lookup as a post-clone pass over every `Label`/`Button`.

Note that action-list menus are already safe: `MenuAction.Label` resolves lazily through
`Text(LabelKey, fallbackLabel)`, so the key travels with the action and never gets baked.

### 3.2 UI Toolkit does not read `TMP_Settings.fallbackFontAssets`

**STATUS (2026-08-24): discovered in Phase 6 anyway, and now live with no mitigation.** This
section said the gap "must not be discovered in Phase 6" — Phase 6 (the uGUI deletion, `ac5468a7`)
landed on `feature/ui-toolkit-migration` without it being resolved, and the uGUI fallback that used
to render this text correctly no longer exists to fall back to. Flagged again on review of PR #344
(Codex, 2026-08-24, P2) against `NewWorldScreenController`/`LanMultiplayerScreenController`: a
world or LAN server name containing Arabic, CJK, Thai, or Devanagari characters renders as tofu on
the only remaining menu backend, and nothing currently sanitizes or restricts that input — verified
by grepping `NewWorldScreenController.cs`/`LanMultiplayerScreenController.cs` for validation and
finding none. This is a real, currently-shipping regression versus the deleted uGUI screens, not a
theoretical one.

`BlockiverseTmpFontFallbackBootstrapper.cs` builds dynamic `TMP_FontAsset` fallbacks from OS fonts
(Noto CJK, Arabic, Thai, Devanagari …) into `TMP_Settings.fallbackFontAssets`. UI Toolkit has its
own font pipeline and does not consult that list, so a migrated screen renders non-Latin text as
tofu even though the uGUI screen beside it used to render it correctly.

**Why this was not fixed blind in the same PR (2026-08-24 investigation, no live Editor session
available):**

- `PanelSettings.textSettings` (type `PanelTextSettings`) is confirmed public and settable —
  Unity's bundled 6000.5 ScriptReference documents it directly, and the project already has one
  shared instance, `Assets/Blockiverse/UI/Settings/MenuWorldSpacePanelSettings.asset`.
- The fallback-font list itself is **not** part of `PanelTextSettings`'s public scripting API in
  6000.5. `UnityEngine.UIElementsModule.dll` contains a `get_fallbackOSFontAssets` accessor, but
  the ScriptReference index for `UIElements.PanelTextSettings` lists no public property beyond
  `hideFlags`/`name` inherited from `Object` — it is Inspector/serialization-layer surface, not a
  documented runtime API. Wiring it therefore needs either `SerializedProperty` field-poking with
  an exact underlying field name that could not be confirmed without a live Editor session to test
  against, or hand-authoring a persisted `PanelTextSettings` asset through the Inspector.
- The dynamic-OS-font trick `BlockiverseTmpFontFallbackBootstrapper` uses
  (`Font.CreateDynamicFontFromOSFont`) cannot simply be reused for a *persisted* `PanelTextSettings`
  asset even if the field name were confirmed: a dynamically created `Font` is a transient runtime
  object with no asset path, and is not something `AssetDatabase.SaveAssets()` can serialize into
  the `.asset` file. The TMP path only works because it runs fresh every session from a
  `MonoBehaviour.Awake()`, never persisted.
- That same OS-font-name list's coverage on **Quest's Android image is itself unverified.** The
  existing TMP bootstrapper's own ordering comment — "Android/Quest families first; desktop
  families help editor validation" — was written as an assumption, not something confirmed on
  device. A UI Toolkit fix built on the same names would inherit that same unverified assumption,
  which is a second thing to get wrong on top of the API uncertainty above.

**What actually closing this needs**, in one pass with live Editor and device access: (1) confirm
the real serialized field name and type for `PanelTextSettings`'s fallback list via
`SerializedObject` inspection in a running Editor; (2) decide bundling real Noto subset `.ttf`
files (adds binary weight and OFL licence tracking, alongside Barlow/Zilla Slab, but works
regardless of what fonts ship on a given Quest OS image) versus the OS-dynamic-font approach (no
new assets, but unverified device coverage) — bundling is very likely the more defensible choice
given the device-coverage unknown; (3) build and verify on a real Quest that a world/server name
containing each target script (Arabic, CJK, Thai, Devanagari) renders a glyph, not tofu.

Out of scope for Phase 1. Was supposed to be in scope no later than Phase 6; is not, and is now a
live gap rather than a theoretical one.

---

## 4. Behaviours that must not be lost

Ordered by consequence. Each is a rewrite hazard, not a style preference.

**Correctness / multiplayer**

1. **Creative-tools authority gating** — region fill/replace/delete/copy/paste/undo bypass the
   per-block authority channel and are legal only offline, in Creative, with a world loaded. Time
   and weather controls are separately host-gated and **revert the slider before reporting**.
   Losing any of this lets a client desync a shared world.
2. **Host-authoritative command routing** in crafting, crate and station: submit, treat
   accepted-or-pending as UI success, but fire the domain change event **only on `Accepted`**. A
   rewrite that optimistically mutates the local mirror reintroduces item duplication.
3. **`EffectiveStationFor` returns `None` rather than substituting** — the panel must let
   `CraftingService` reject, never decide station validity itself.
4. **Discovered-port adoption before join** — the port is signed into the approval payload;
   skipping it makes a non-default-port host listable but unjoinable (`InvalidJoinPayload`).
5. **Pause is suppressed while a LAN session is live** — one player's menu must not freeze a
   shared world's clock.
6. **`OnStationRemoved` force-closes the station panel** when the backing block disappears,
   including from a host snapshot.

**Lifecycle — the ones that leak**

7. **LAN discovery listening is keyed on presenter visibility, not `OnEnable`/`OnDisable`.**
   Presenters hide by disabling the `Canvas`, so lifecycle callbacks fire once at scene load. The
   prior bug left a UDP socket and receive loop alive for an entire headset session. **UI Toolkit
   changes the hiding mechanism**, so this must be re-derived rather than transliterated.
8. **Exact-subscription bookkeeping** for `SelectionChanged` and discovery button closures. Both
   sites carry comments about the N-times-firing failure mode.
9. **Confirm-callback re-entrancy**: the callback is swapped to a local and nulled *before*
   `PopModal`, because the callback may push another modal.
10. **Known defect, do not port**: `BlockiverseComfortMenu.UnregisterControlCallbacks()` omits 8 of
    its 20 registered controls. Fix it in the rewrite; do not reproduce it.

**VR comfort — regressions a test will not catch**

11. **Menu anchor preservation across screen transitions.** Without it every navigation step
    teleports the panel in front of the player.
12. **Three placement modes, and the `WorldFixed` "never derive from headset" rule.** Title menus
    are world fixtures at a spawn-relative pose; in-session menus lazily follow with a 30°/1.5 m
    threshold and 0.35 s exponential glide, never pitch-locked to gaze.
13. **`Recenter()` reparents to null before posing**, or the panel is dragged around by the rig.
14. **Settings push-down uses `SetValueWithoutNotify`/`SetIsOnWithoutNotify` everywhere.** Plain
    assignment creates an echo loop. The Glide/Teleport radio pair additionally prevents both-off.

**Performance**

15. **Render-diff caches are load-bearing, not clutter**: `SlotRenderState`,
    `RecipeRowRenderState` + inventory fingerprint, `lastContentVersion`, the health panel's
    last-value gates, and the pre-formatted 0..99 stack-count table. They exist because text
    rebuilds are too expensive per frame on Quest. "Set text every frame" is a measurable
    regression.
16. **Polling exists where the domain has no event** — vitals (0.5 s), station proximity (0.5 s),
    `SmeltingStationModel.ContentVersion`, `ComfortSettings.UiScale`,
    `CreativeInteractionController.CurrentTarget` (pressing a UI button clears the live target),
    `NetworkManager.IsListening`, discovery at 1 Hz. Preserve the cadence; adding domain events is
    a separate refactor.

**Platform**

17. **`TMP_InputField.shouldHideSoftKeyboard = true`** — `BlockiverseSystemKeyboardField` owns the
    system keyboard and a competing TMP keyboard breaks text entry on device. The UI Toolkit
    equivalent is unproven; **this is a Phase 1 exit criterion**.
18. **First-run gating via `PlayerPrefs`** — `Blockiverse.ControllerMappingPopupSeen` and
    `Blockiverse.ComfortScreenSeen`, plus the ray-intersection close fallback.
19. **`CanQuit()` is editor-only** — Quest builds must not show an in-app quit action, and
    `PauseReturnToTitle` must route to title, never `Application.Quit`.

**Structural**

20. **Name-convention runtime reference resolution** (`Action 1..N`, `Panel/Save 1..6`,
    `Panel/Row <Field>/<Back|Next|Value>`) exists because scenes are generated and serialized refs
    go stale. Every one of those names is a contract with `BlockiverseProjectBootstrapper.Menus.cs`.
    UXML replaces it with element-name queries — which is an improvement, but the *pairs* must be
    migrated together or the generated scene and the controller disagree silently.

---

## 5. Existing tests as a rewrite oracle

These pinned the pre-migration behaviour and were the parity contract. Framework-neutral ones kept
passing unchanged; uGUI-specific ones were rewritten against the Toolkit screens, never deleted to
make a phase pass. The uGUI oracles have now been consumed — each name below that no longer exists
is followed by the file that inherited its assertions, and every one of those files carries a
header comment naming the oracle it came from, so the trail stays walkable.

Still present and unchanged: `UiScreenRouterEditModeTests`, `SaveListModelEditModeTests`,
`NewWorldConfigEditModeTests`, `BlockiversePanelPlacementEditModeTests`,
`WorldSessionControllerEditModeTests`, `CompositionLayerUiEditModeTests`,
`BlockiverseTmpFontFallbackEditModeTests`.

Consumed at the uGUI removal:

| uGUI oracle | Inherited by |
| --- | --- |
| `ActionMenuEditModeTests` | `MenuActionsEditModeTests`, `Toolkit/ModalDialogsEditModeTests`, `Toolkit/ActionMenuScreensEditModeTests` |
| `MenuRuntimeWiringEditModeTests` | `MenuControllerRoutingEditModeTests`, `BlockiverseKeyboardHandVisibilityEditModeTests`, `WorldSessionControllerEditModeTests`, `Toolkit/ControlsScreensEditModeTests`, `Toolkit/WorldManageScreensEditModeTests` |
| `SurvivalUiEditModeTests` | `Toolkit/HudFamilyEditModeTests`, `Toolkit/InventoryCrateScreensEditModeTests`, `Toolkit/ToolkitScreenFeedbackEditModeTests` |
| `SurvivalCraftingPanelEditModeTests` | `Toolkit/CraftingStationScreensEditModeTests` |
| `SurvivalHudFeedbackEditModeTests` | `Toolkit/HudFamilyEditModeTests` |
| `BlockiverseMultiplayerSessionMenuEditModeTests` | `Toolkit/LanMultiplayerScreenEditModeTests` |
| `LocalizationPrefabCharacterizationEditModeTests` | retired with the rig prefab's uGUI menus (it counted `localizationKey:` lines in a subtree that no longer exists) |

All under `Assets/Blockiverse/Tests/EditMode/`.

---

## Appendix A — Verified native configuration

Established firsthand against Unity 6000.5.8f1 and XRI 3.5.1 in this checkout: enum values from the
IL of `UnityEngine.UIElementsModule.dll`, serialized values from the XRI **World Space UI** sample.
This is the recipe the bootstrapper implements.

### Enum values (IL-verified)

| Type | Members |
|---|---|
| `UnityEngine.UIElements.PanelRenderMode` | `ScreenSpaceOverlay = 0`, `WorldSpace = 1` |
| `UnityEngine.UIElements.WorldSpaceSizeMode` | `Dynamic = 0`, `Fixed = 1` |
| `UnityEngine.UIElements.ColliderUpdateMode` **(private type)** | `MatchBoundingBox = 0`, `Keep = 1`, `MatchDocumentRect = 2` |
| `UnityEngine.UIElements.Pivot` | `Center = 0`, `TopLeft = 1`, … `BottomRight = 8` |
| `PanelInputConfiguration.PanelInputRedirection` | `AutoSwitch = 0`, **`Never = 1`** (`[InspectorName("No input redirection")]`), `Always = 2` |

### API reachability

`PanelSettings.renderMode`, `scaleMode`, `referenceSpritePixelsPerUnit`, `scale`,
`themeStyleSheet`, `dynamicAtlasSettings` have **public** setters.
`colliderUpdateMode`, `colliderIsTrigger` and `pixelsPerUnit` have **`assembly`-internal** setters,
and `ColliderUpdateMode` is a private type — they are unreachable from user code and must be
written through `SerializedObject`.

### World space ignores the scale mode

`PanelSettings.ApplyScale` branches on `renderMode`: when it is `WorldSpace` it stores
`m_ResolvedScale = 1` directly and never calls `ResolveScale`. So `scaleMode`, `referenceDpi`,
`fallbackDpi`, `referenceResolution`, `screenMatchMode` and `match` have **no effect** on a
world-space panel. The only sizing model is:

```
metres = UIDocument.worldSpaceSize / PanelSettings.pixelsPerUnit * transform.localScale
```

Blockiverse uses 1000 × 700 px at 100 ppu and 0.1 scale → **1.00 m × 0.70 m**.
Do not carry the uGUI Canvas scale constant (0.0013) across; it describes a different unit system.

### Two API facts that are easy to get wrong

Both were found by review after the first draft of the Phase 1 code had them wrong, and both fail
silently.

**`PanelSettings.m_PixelsPerUnit` is `float32`.** Writing it through
`SerializedProperty.intValue` does not land. It is currently invisible because the project's chosen
value (100) equals the constructor default, so the panel is the right physical size by coincidence —
change the constant and the collider, the document size and the size tests all move to the new model
while the panel settings stay at 100. Write it with `floatValue`.

**`UIDocument.OnDisable` sets `rootVisualElement` to null**, and `OnEnable` rebuilds the tree from
the `VisualTreeAsset`, producing brand-new element instances. So **a screen controller must never
hide its panel by disabling the UIDocument**: the controller is a different component, its own
`OnDisable` does not run, and it is left holding references into a discarded tree with its callbacks
still nominally bound. One hide/show cycle produces a panel that renders, unstyled, and is completely
inert — no exception, no warning. Hide by collapsing the root element's `display` (and disabling the
collider alongside it), or re-attach on rebuild.

### Scene requirements

| Object | Component | Setting |
|---|---|---|
| any | `XRUIToolkitManager` | enabled. It only sets `XRUIToolkitHandler.uiToolkitSupportEnabled`; without it XRI ignores every UI Toolkit panel |
| any | `PanelInputConfiguration` | `panelInputRedirection = Never`, `processWorldSpaceInput = true` |
| `Boot Event System` | `XRUIInputModule` | `bypassUIToolkitEvents = false` **while uGUI coexists** |
| panel | `UIDocument` + `Collider` | `PanelSettings.renderMode = WorldSpace` |

### The layer trap

The XRI sample puts its panels on Unity layer **5 (UI)**. Blockiverse's ray interactors raycast
against `BlockiverseProject.VrUiRaycastLayerMask` = `BlockiverseInteractable (10) | BlockiverseFluid (13)`
only. **A panel copied onto the sample's layer renders perfectly and cannot be pointed at**, with
nothing in the editor to show for it. Panels go on layer 10.

### Known tension, unresolved

`Boot Event System`'s `EventSystem.sendNavigationEvents` is **false**, with a comment explaining
why: navigation/submit events let the UI Press action fire the auto-selected Button a second time.
The migration spec asks for navigation/submit input for editor and non-XR test coverage (§7.5) and
for the focus coordinator (§11.3). Those are in direct conflict while uGUI is still in the scene.
Phase 2 must either keep navigation events off and test focus another way, or prove the double-fire
does not occur for UI Toolkit — not assume it.
