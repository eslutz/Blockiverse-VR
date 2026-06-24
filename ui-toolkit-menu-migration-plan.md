# UI Toolkit Menu Migration Plan

Project: `/Users/ericslutz/Developer/Code/Blockiverse/Blockiverse-VR`

Legacy comparison source: `/Users/ericslutz/Developer/Code/Blockiverse VR`

Date: 2026-06-22

## Evidence Used

- Current project instructions: `CLAUDE.md` and `MEMORIES.md`.
- Current menu audit: `blockiverse-vr-current-menu-flow.md`.
- Legacy menu audit: `blockiverse-vr-legacy-menu-flow.md`.
- Menu ruleset target: `docs/rulesets/voxel_survival_menus.md`.
- Product direction: `docs/roadmap/blockiverse_vr_execution_plan.md`.
- Live Unity MCP check:
  - Active scene: `Assets/Blockiverse/Scenes/Boot.unity`.
  - Scene roots include `Boot Event System` with `XRUIInputModule`, one `XR Interaction Manager`, and `BlockiverseXRRig` with `BlockiverseMenuController` and `BlockiverseWorldSessionController`.
  - `manage_ui list` found `0` UI Toolkit assets, so the current runtime menus are still UGUI/Canvas-based.
  - Unity custom tools are available for `manage_ui`, `manage_scene`, `read_console`, `run_tests`, `manage_asset`, `manage_build`, and related validation.
- Package baseline:
  - Unity `6000.3.16f1`.
  - XRI `3.3.2`.
  - Input System `1.19.0`.
  - Netcode for GameObjects `2.11.2`.
  - URP `17.0.1`.
  - UGUI `2.0.0`.
  - UI Toolkit runtime module is present through `com.unity.modules.uielements`.

## Decision

The current project should be the architectural base. Its `MenuActions`, `UiScreenRouter`, `BlockiverseMenuController`, and `BlockiverseWorldSessionController` are closer to modern Unity menu practice than the legacy menu router because they already use explicit screen IDs, explicit action IDs, a stack/modal router, and a narrow world-session bridge.

The legacy project should be treated as a feature and content source, not as an architecture source. Its useful player, context, diagnostics, avatar, policy, network-status, survival-rejection, farming, inventory, crafting, and station surfaces should migrate into the current route/action system with modern UI Toolkit presentation. Its generic string-model router, hidden return behavior, and known no-op world-details close route should not be carried forward.

## Target Architecture

Project tier: long-lived VR game. Use a small architecture that keeps the existing generated Boot scene workflow but moves menu presentation out of generated UGUI RectTransform trees.

Recommended modules:

| Module | Responsibility |
| --- | --- |
| `Blockiverse.UI.Routing` | Owns screen IDs, action IDs, route graph, modal rules, pause/world-input flags, and route tests. This starts from the current `MenuActions` and `UiScreenRouter` behavior. |
| `Blockiverse.UI.Models` | Plain C# menu view models for title, saves, settings, inventory, crafting, station, LAN, diagnostics, status, and modal prompts. No `GameObject`, `Transform`, or scene lookup. |
| `Blockiverse.UI.Toolkit` | UI Toolkit presenters that bind view models to UXML/USS and emit action IDs. Thin `MonoBehaviour` bridges only. |
| `Blockiverse.UI.XR` | XR-specific menu surface, tracked pointer/focus handling, dominant-hand ownership, menu pose, and input suppression. |
| `Blockiverse.UI.Generated` | Bootstrapper-owned scene composition for UIDocuments, PanelSettings, menu surfaces, and fallback legacy UGUI during migration. |
| `Blockiverse.UI.Tests` | EditMode route/action/binding tests and PlayMode runtime wiring tests. |

Scene/bootstrap plan:

- Keep `Assets/Blockiverse/Scenes/Boot.unity` as the single runtime scene.
- Do not hand-author generated scene menu wiring. Change `BlockiverseProjectBootstrapper.Run()` and regenerate.
- Keep the existing roots:
  - `Boot Event System` with `XRUIInputModule`.
  - `XR Interaction Manager`.
  - `BlockiverseXRRig`.
  - `Blockiverse Network Manager`.
- Add bootstrapper-generated UI Toolkit roots under the XR rig/camera offset:
  - `Blockiverse UITK Menu Surface`: primary world-space menu surface with `UIDocument`.
  - `Blockiverse UITK HUD Surface`: gameplay HUD and small overlays.
  - `Blockiverse UITK Modal Surface`: confirm/error/status overlays that must sit above the active route.
  - `Blockiverse Menu Panel Settings`: shared `PanelSettings` asset for the primary VR menu surface.
- Keep the current UGUI menu roots only as a migration fallback until screen parity and runtime validation pass.

Data ownership:

- Author UXML/USS assets under a new `Assets/Blockiverse/UI/` tree.
- Keep stable static route/action IDs in code, but add a registry test that proves every declared action is bound by a presenter or intentionally system-only.
- Use `ScriptableObject` only for authored UI theme/config such as spacing, colors, typography, layout scale, and menu surface defaults.
- Keep runtime menu state in plain C# view models owned by controllers/session systems, not in `ScriptableObject` assets.

Communication rules:

- UI Toolkit buttons emit action IDs to the menu controller.
- The menu controller owns routing and modal behavior.
- `BlockiverseWorldSessionController` remains the world/session bridge for save, load, create, respawn, quit, and return-to-title actions.
- Use C# events or direct explicit references for menu actions; do not add a global event bus.
- Route transitions must be explicit. Avoid "return to captured source" behavior unless the source route is recorded in the route stack and covered by tests.

## Modern UI Toolkit Standards For This Migration

- Use UXML for structure, USS for style, and `UIDocument` plus `PanelSettings` for runtime attachment.
- Prefer one `UIDocument` per independent surface class: main menu surface, HUD surface, and modal/toast surface. Use reusable `VisualTreeAsset` templates for repeated rows, cards, tabs, sliders, and station slots.
- Do not recreate the UGUI pattern of code-building hundreds of RectTransform controls in the bootstrapper.
- Do not mix new UI Toolkit screens with `Canvas`, `CanvasScaler`, `GraphicRaycaster`, or `TrackedDeviceGraphicRaycaster` assumptions. Those remain legacy fallback details until removed.
- Use stable element names/classes in UXML so MCP `manage_ui get_visual_tree`, binding tests, and automation can find every control.
- Keep USS within Unity-supported USS features. Avoid unsupported web CSS such as grid, box-shadow, calc, gradients, z-index, and pseudo-elements.
- Every user-visible control must map to one explicit action ID, one setting binding, or one view-model selection command.
- Every screen must declare:
  - route ID,
  - purpose,
  - pause/world-input policy,
  - allowed parent routes,
  - close/back behavior,
  - required view-model fields,
  - required buttons/options,
  - validation coverage.

## Visual Design And Readability Standards

The migration is not complete just because the routes work. Each menu must also pass a visual quality bar for a human wearing the headset.

Design source of truth:

- Create a small Blockiverse menu style guide before converting the full menu set.
- Define shared tokens for:
  - typography scale,
  - font weight,
  - line height,
  - text color,
  - panel background,
  - focus/hover/pressed states,
  - danger/warning/success states,
  - spacing,
  - row height,
  - button hit area,
  - panel width/height,
  - world-space distance,
  - world-space scale.
- Use those tokens in USS. Do not let each menu invent its own font sizes, spacing, colors, or button styles.
- Use the same visual hierarchy across all route types:
  - title/header,
  - one clear primary action area,
  - secondary actions,
  - detail/status area,
  - footer/back/close actions.

Readability requirements:

- Text must be crisp in headset, with no blurry scaling, clipped glyphs, or tiny secondary labels.
- Body text, button labels, status labels, and metadata rows must have explicit minimum sizes from the shared type scale.
- Text contrast must be checked against its actual panel background, including disabled and warning/error states.
- Button and row hit targets must be large enough for controller-ray selection and must have visible hover/focus/pressed feedback.
- Labels must not truncate unless the control has an intentional overflow strategy such as marquee, tooltip, details popover, or wrapping.
- Dense screens such as Inventory, Crafting, Container, Station, LAN, and Diagnostics must use grouped sections, tabs, paging, or filtering instead of one long unscannable list.
- Modal prompts must show one clear primary decision and one clear escape/cancel path.
- Dangerous actions such as Delete, Return to Title, and Quit must use consistent warning treatment.
- No menu may rely on color alone to communicate enabled, disabled, selected, dangerous, or error state.

Programmatic checks can enforce many of these rules, but not all of them. Automated validation should prove objective quality constraints such as contrast, text size, clipping, layout overlap, missing focus states, blank renders, and golden-image regressions. Human review is still required for subjective quality: visual taste, information hierarchy, wording clarity, and whether a screen feels calm and understandable in headset.

## Target Menu Inventory

The end state should include every current implemented screen, every ruleset-required gameplay screen, and the useful legacy status/context surfaces.

| Target menu | Purpose | Source and migration action |
| --- | --- | --- |
| Boot / Startup Loading | Brand/loading/status while app starts or a world loads. | Preserve current screen, rebuild as UITK loading route. |
| Controller Mapping | First-run controller reference and close path. | Preserve current functionality; rebuild as UITK popup. |
| Title | Continue, new world, load world, LAN, settings, platform-safe quit. | Preserve current actions and availability rules. |
| New World | World name, seed, game mode, difficulty, size, preset, biome, texture set, create/cancel. | Preserve current richer current-project implementation. Consider legacy pre-create settings shortcut only if it improves flow. |
| Load World | Save selection, paging, load, details, cancel. | Preserve current implementation and add stronger empty/error states. |
| World Details | Save metadata, play, rename, duplicate, delete, back. | Preserve current implementation. Do not import legacy no-op close behavior. |
| Confirm / Error Dialog | Destructive action confirmation, OK/error prompts. | Preserve current modal contract; add typed prompt variants. |
| LAN Multiplayer | Host, join, stop, address, status, close. | Preserve current implementation and migrate useful legacy diagnostics/status links. |
| Gameplay HUD | Health/vitals, mining progress, hotbar, key survival status, pinned recipe/status overlays. | Preserve current HUD data, split heavy inventory/crafting/container functions into dedicated player menus. |
| Pause | Resume, save, mode switch, creative tools, settings, return title, quit where valid. | Preserve current route. Fix any ambiguous quit/save routing during implementation. |
| Settings Hub | Comfort, audio, controls, accessibility/feedback, close. | Preserve current hub and migrate useful legacy controls for primary hand, haptics, toasts, subtitles, and texture set if still user-facing. |
| Comfort | Locomotion, vignette, turn, move speed, hand preference, height/UI scale. | Preserve current richer current-project implementation. |
| Audio / Feedback | Volume, haptics, reduced flash/particles, feedback preferences. | Preserve current implementation and add legacy subtitle/toast toggles if still relevant. |
| Controls | Controller reference and remapping/status. | Preserve current reference; add actionable remap/reset/status affordances if supported by the input layer. |
| Inventory | Player inventory, equip/use/drop/split/move, hotbar assignment, links to crafting/context/status. | Migrate from current folded HUD plus legacy dedicated inventory panel into first-class route/tab. |
| Vitals / Status | Health, hunger, thirst, stamina, current effects. | Migrate useful legacy Vitals as either HUD detail screen or Player Status tab. |
| Crafting | Recipe browser, craftability, ingredients, craft count, pin recipe, craft/cancel. | Migrate from current HUD recipe rows and legacy crafting summary into first-class route. |
| Container | Container inventory, transfer one/all, deposit/withdraw, close. | Migrate from current shared-crate HUD controls plus legacy container panel. |
| Station Shell | Common station frame for input/fuel/output/progress/recipe/transfer. | Preserve current station behavior and generalize as UITK station route. |
| Campfire | Cooking/light fuel station variant. | Add ruleset-required station variant using Station Shell. |
| Clay Kiln | Clay/ceramic smelting station. | Preserve current generic station functionality, specialize labels/recipes. |
| Bellows Forge | Forge/smelting station. | Preserve current generic station functionality, specialize labels/recipes. |
| Prep Board | Food/prep station. | Add ruleset-required station variant using Station Shell. |
| Mend Bench | Repair/mending station. | Add ruleset-required station variant using Station Shell. |
| Map / Wayflag | World map, marker/wayflag selection, waypoint actions. | Add missing ruleset route. |
| Item Details Popover | Item stats, actions, context hints. | Add reusable modal/popover template for inventory, crafting, container, station, and HUD. |
| Recipe Pin Overlay | Pinned recipe ingredient tracking while playing. | Add HUD overlay tied to Crafting route. |
| Block Menu / Block Catalog | Creative block selection/search/category/page. | Preserve current quick block menu, convert to UITK and share catalog row templates with Creative Tools. |
| Creative Tools | Region selection, fill/replace/delete/copy/paste, undo/redo, structures, time/weather. | Preserve current implementation and convert to UITK. |
| Death | Respawn at bedroll/world spawn, return title. | Preserve current death route and pause policy. |
| Player Hub | Fast access to Inventory, Vitals, Crafting, Context, Status. | Migrate useful legacy Player Panels as tabbed navigation, not as a generic router. |
| Context Hub | Container, farming, station, creative/world context. | Migrate useful legacy context structure if it reduces controller-menu travel. |
| Farming Summary | Crop/soil/seed/ready-state summary. | Migrate useful legacy farming summary into Context/Farming route. |
| Farming Action Popup | Till, plant, harvest, close. | Migrate useful legacy contextual popup. |
| Avatar Status | Local/remote avatar session and fallback state. | Migrate legacy surface into Status/Diagnostics area, gated for relevance. |
| Meta Policy Status | Platform/social/avatar policy decisions. | Migrate legacy surface into Status/Diagnostics area. |
| Diagnostics | Runtime trace, networking, scene/session, performance evidence. | Migrate legacy diagnostics as developer/status route; hide or gate in release builds. |
| Network Command Status | Accepted/duplicate/rejected network command feedback. | Migrate as non-blocking status modal/toast with route hints. |
| Survival Rejection | Blocking rejection with retry/dismiss/open hinted panel. | Migrate as a modal that can deep-link to Inventory, Crafting, Container, Farming, Station, HUD, or LAN. |

## Legacy Migration Rules

Migrate:

- Player hub concepts: Primary, Context, and Status should become tabs or route groups inside the modern menu system.
- Dedicated Inventory, Vitals, Crafting, Container, Farming, and Station summaries.
- Diagnostics, Avatar Status, Meta Policy Status, Network Command Status, and Survival Rejection, with release gating where appropriate.
- Farming Action Popup.
- Useful preferences: primary hand, haptics, toasts, subtitles, texture set if still part of the game design.
- LAN status links to diagnostics/avatar/policy where they support debugging or multiplayer readiness.

Do not migrate:

- The legacy generic `BlockiverseWorldSpacePanelModel` as the main router architecture.
- Hidden "captured source" return behavior unless represented by the current route stack.
- The World Details close no-op route.
- Five-button generic panel limits.
- Status/diagnostic screens as unavoidable first-level player flow in release builds.

## Implementation Phases

### Phase 0 - Freeze The Contract

1. Create a `MenuMigrationChecklist` document or test fixture from `docs/rulesets/voxel_survival_menus.md`, `blockiverse-vr-current-menu-flow.md`, and `blockiverse-vr-legacy-menu-flow.md`.
2. Mark every target menu as one of:
   - existing current route,
   - current folded HUD feature,
   - legacy feature to migrate,
   - new ruleset-required feature.
3. Add route/action inventory tests around `MenuActions` before changing presentation.
4. Add tests for existing high-risk flows:
   - title to new/create/load/details/LAN/settings/quit,
   - settings opened from title and pause,
   - pause resume/save/creative/settings/return title/quit,
   - death respawn/title,
   - station open/close,
   - confirm modal accept/cancel.
5. Record the known current console noise separately so later validation does not confuse local tooling warnings with menu regressions.

Exit criteria:

- Current route graph is documented and test-covered.
- Every current action ID has an expected route or world-session behavior.
- Every target menu has an owner phase.

### Phase 1 - Extract View Models And Action Binding

1. Keep `MenuActions` as the canonical public contract initially.
2. Add typed wrappers or generated constants only if they reduce missed string IDs without breaking existing callers.
3. Move screen-specific data into plain C# view models:
   - `TitleMenuViewModel`,
   - `NewWorldViewModel`,
   - `LoadWorldViewModel`,
   - `WorldDetailsViewModel`,
   - `SettingsViewModel`,
   - `InventoryViewModel`,
   - `CraftingViewModel`,
   - `ContainerViewModel`,
   - `StationViewModel`,
   - `LanMenuViewModel`,
   - `DiagnosticsViewModel`,
   - `StatusViewModel`.
4. Keep `BlockiverseMenuController` responsible for routing, modal ownership, pause/world-input policy, and action dispatch.
5. Keep `BlockiverseWorldSessionController` responsible for save/load/create/respawn/title/quit side effects.
6. Add a common `IMenuActionSink` or equivalent narrow interface used by UITK presenters and temporary UGUI presenters.

Exit criteria:

- Current UGUI presenters can still work through the extracted view models/action sink.
- Route decisions no longer depend on UGUI component state.
- EditMode tests can instantiate menu models without scene objects.

### Phase 2 - Build The UI Toolkit Foundation

1. Add UI Toolkit asset folders:
   - `Assets/Blockiverse/UI/UXML/`
   - `Assets/Blockiverse/UI/USS/`
   - `Assets/Blockiverse/UI/PanelSettings/`
   - `Assets/Blockiverse/Scripts/UI/Toolkit/`
2. Create shared USS:
   - tokens,
   - type scale,
   - surface layout,
   - buttons,
   - sliders/toggles,
   - list rows,
   - tabs,
   - modal/popover,
   - HUD overlays.
3. Create reusable UXML templates:
   - menu shell,
   - header/body/footer,
   - button row,
   - icon/value row,
   - slider row,
   - toggle row,
   - paged list,
   - inventory slot,
   - item detail,
   - recipe row,
   - station slot,
   - status card,
   - modal prompt.
4. Create `PanelSettings` assets for:
   - primary world-space menu,
   - HUD overlay,
   - modal/status overlay.
5. Add bootstrapper generation for UIDocument surfaces without removing UGUI yet.
6. Add a `BlockiverseUITKMenuSurface` presenter that can show one route, hide others, bind view models, and emit action IDs.
7. Add `BlockiverseUITKInputAdapter` for XR pointer/focus/back/menu behavior and active-hand ownership.

Exit criteria:

- `manage_ui list` shows the expected UXML/USS/PanelSettings assets.
- The Boot scene contains generated UIDocument roots.
- UI Toolkit surfaces can render a smoke-test screen in editor without taking over the live game flow.

### Phase 3 - Convert App Shell Menus First

Convert the lowest-risk non-gameplay flows:

1. Boot / Startup Loading.
2. Controller Mapping.
3. Title.
4. Confirm / Error Dialog.
5. New World.
6. Load World.
7. World Details.
8. World Loading.

Implementation notes:

- Keep UGUI fallback behind a single feature flag such as `UseUiToolkitMenus`.
- Do not change save/load/create semantics during the visual conversion.
- Preserve the current richer New World controls.
- Add empty/error/loading states that are explicit in the view model, not inferred from missing rows.

Exit criteria:

- A user can launch, dismiss controller mapping, create a world, load a world, inspect details, delete with confirmation, and return to title using UITK.
- UGUI fallback still works while the rest of the menu suite is being migrated.

### Phase 4 - Convert Pause, Settings, LAN, And Controls

1. Convert Pause.
2. Convert Settings Hub.
3. Convert Comfort.
4. Convert Audio / Feedback.
5. Convert Controls.
6. Convert LAN Multiplayer.
7. Add legacy-derived settings only if they map to real current systems:
   - primary hand,
   - haptics,
   - toasts,
   - subtitles,
   - texture set.
8. Fix or explicitly test pause quit/return-title behavior:
   - `PauseReturnToTitle` saves and returns to title.
   - `PauseQuit` confirms, saves, and quits only on platforms where quit is valid.
   - Quest builds should not expose unusable quit actions.

Exit criteria:

- Settings preserves the caller route when opened from title or pause.
- Menu/back button behavior matches current stack semantics.
- LAN can host/join/stop/close and report connection failures through the shared modal/status system.

### Phase 5 - Split Gameplay HUD Into Proper Player Menus

1. Convert Gameplay HUD as a lightweight in-world UITK HUD.
2. Move inventory-heavy interactions out of the HUD into `Inventory`.
3. Move crafting-heavy interactions out of the HUD into `Crafting`.
4. Add `Vitals / Status` as a focused player screen or status tab.
5. Add `Item Details Popover` shared across Inventory, Crafting, Container, Station, and HUD.
6. Add `Recipe Pin Overlay` to keep tracked recipe ingredients visible during gameplay.
7. Add `Player Hub` tabs if testing shows they reduce controller travel:
   - Inventory,
   - Vitals,
   - Crafting,
   - Context,
   - Status.

Exit criteria:

- The HUD remains readable and lightweight.
- Inventory and Crafting are first-class routes with explicit controls, not hidden HUD subregions.
- Existing hotbar, recipe, repair, and crate functions still work or have an intentional migrated target.

### Phase 6 - Convert Context, Station, Farming, And Map Menus

1. Convert current `Station Panel` to a UITK station shell.
2. Add station variants:
   - Campfire,
   - Clay Kiln,
   - Bellows Forge,
   - Prep Board,
   - Mend Bench.
3. Add `Container` route with transfer actions.
4. Add `Context Hub` if useful for grouping container/farming/station/creative actions.
5. Add `Farming Summary` and `Farming Action Popup`.
6. Add `Map / Wayflag`.
7. Ensure every world interaction that opens a menu has:
   - active route,
   - close route,
   - blocked-input policy,
   - stale-target behavior if the object disappears,
   - multiplayer rejection behavior.

Exit criteria:

- Each station has correct recipes, slots, transfer buttons, progress state, and close behavior.
- Context routes can be reached from gameplay and return to gameplay without trapping the player.
- Map/wayflag and farming actions are reachable only when runtime systems support them.

### Phase 7 - Migrate Useful Legacy Status And Diagnostics

1. Add `Status Hub` or developer/status tab group.
2. Migrate:
   - Avatar Status,
   - Meta Policy Status,
   - Diagnostics,
   - Network Command Status,
   - Survival Rejection.
3. Gate diagnostics by build type or developer setting.
4. Make network/survival rejection panels actionable:
   - Dismiss,
   - Retry when available,
   - Open hinted panel.
5. Link LAN, Avatar, Policy, and Diagnostics without making them required first-level release-player flow.

Exit criteria:

- Multiplayer/platform/debug information is available when needed.
- Release-facing flows are not cluttered by debug status panels.
- Rejection/error panels can deep-link to the relevant recovery route.

### Phase 8 - Remove Legacy UGUI Menu Generation

Only start this phase after UITK parity passes.

1. Remove UGUI menu root generation from bootstrapper files.
2. Keep non-menu UGUI only where still needed for unrelated effects or known engine limitations.
3. Remove temporary UGUI presenter fallback wiring.
4. Delete unused UGUI-specific menu components after references are gone.
5. Keep `com.unity.ugui` only if other systems still need it; otherwise evaluate package removal separately.
6. Update docs:
   - current menu flow markdown,
   - ruleset implementation status,
   - agent instructions if new validation commands are canonical.

Exit criteria:

- Boot scene regenerates with UITK menu roots and no stale UGUI menu roots.
- `manage_ui list` and scene hierarchy show UITK as the menu presentation source.
- Search shows no active references to removed UGUI menu presenters.

## Validation Plan

Run validation at the end of each phase and the full set before declaring migration complete.

### Static Validation

- `git diff --check`
- `rg -n "Canvas|CanvasScaler|GraphicRaycaster|TrackedDeviceGraphicRaycaster" Assets/Blockiverse/Scripts/UI Assets/Blockiverse/Scripts/Editor`
  - expected to shrink phase by phase,
  - allowed only for fallback or unrelated non-menu systems until Phase 8.
- `rg -n "UIDocument|PanelSettings|VisualTreeAsset" Assets/Blockiverse/Scripts Assets/Blockiverse/UI`
  - expected to grow as UITK screens land.
- Route/action tests:
  - every screen ID is registered,
  - every action ID is handled or documented as view-only,
  - every menu button emits a valid action ID,
  - every route has back/close behavior,
  - every modal returns to the underlying route.
- UXML/USS binding tests:
  - required named elements exist,
  - required buttons exist,
  - slider/toggle bindings exist,
  - no duplicate element names within a screen where unique lookup is expected,
  - UXML uses `ui:Style`, not bare `Style`.

### Automated Visual Quality Validation

Automation cannot prove a menu is beautiful, but it can block most objective visual regressions before human review.

Add a menu visual audit that renders every route with representative data states:

- empty,
- normal,
- long labels,
- disabled actions,
- warning/error,
- modal over active route,
- dense list/page,
- selected/focused/hovered row.

The audit should capture screenshots through Unity MCP `manage_ui render_ui` in editor and through Quest runtime capture for final validation. Store approved screenshots as visual baselines after human review.

Programmatic checks to add:

- Blank/failed render check:
  - screenshot has non-background content,
  - expected panel region is occupied,
  - primary route title/control labels are visible.
- Text-size check:
  - required text classes use approved USS typography tokens,
  - no menu uses ad hoc tiny font sizes,
  - metadata labels use the approved secondary-text token or larger.
- Contrast check:
  - text/background pairs meet the project contrast threshold,
  - disabled text is readable enough to identify the disabled option,
  - danger/warning/success states remain readable.
- Layout overlap check:
  - named controls have non-overlapping bounding boxes unless explicitly layered,
  - buttons do not cover labels,
  - modal surfaces obscure or disable underlying route input intentionally.
- Clipping/truncation check:
  - required labels are not clipped,
  - wrapped text stays inside its container,
  - long save names, item names, recipes, addresses, diagnostics rows, and error messages have defined overflow behavior.
- Hit-target check:
  - buttons, toggles, sliders, tabs, inventory slots, recipe rows, station slots, and paging controls meet minimum world-space and panel-space sizes.
- Focus-state check:
  - hover/focus/pressed/selected/disabled states are visually distinct in screenshots and in the visual tree class state.
- Visual-regression check:
  - compare each representative screenshot to the approved baseline with a tolerance for dynamic data,
  - require human approval before updating baselines.
- Palette/style consistency check:
  - screens use shared USS tokens,
  - no screen-specific hardcoded color/type/spacing values without an explicit reason.

Human visual review remains a required gate:

- Review screenshots for every route on desktop.
- Review the same core route set in headset.
- Confirm menus are clean, crisp, readable, understandable, and consistent with Blockiverse's game tone.
- Approve or reject baseline screenshots before visual regression tests can treat them as canonical.

### Unity EditMode And PlayMode Validation

- Run the repo gate:
  - `scripts/unity/run-tests.sh`
- Add targeted EditMode tests for:
  - route graph,
  - view-model defaults,
  - action binding,
  - settings persistence,
  - inventory/crafting/station model state,
  - rejection/status deep links.
- Add targeted PlayMode tests for:
  - Boot scene has required roots,
  - exactly one `XRInteractionManager`,
  - one `EventSystem` with `XRUIInputModule`,
  - UIDocument surfaces exist and are enabled/disabled by route,
  - menu button opens Pause from Gameplay,
  - menu/back button closes non-terminal routes,
  - confirm modal blocks underlying input,
  - station close returns to Gameplay HUD,
  - death route cannot be dismissed without respawn/title action.

### Unity MCP Validation

Use Unity MCP after each converted phase:

- `mcpforunity://custom-tools`
  - confirm expected tools remain available.
- `manage_scene get_active`
  - active scene is still `Assets/Blockiverse/Scenes/Boot.unity`.
- `manage_scene get_hierarchy`
  - required UIDocument roots exist,
  - legacy UGUI roots only exist while fallback is intentionally enabled.
- `manage_scene validate`
  - no scene contract errors.
- `manage_ui list`
  - UXML/USS/PanelSettings assets exist.
- `manage_ui get_visual_tree`
  - active route contains expected named controls.
- `manage_ui render_ui`
  - capture representative screens for visual sanity checks.
- `read_console`
  - no new compile/runtime menu errors.
- `run_tests`
  - use MCP test tool when useful, but keep `scripts/unity/run-tests.sh` as the repo-level gate unless the repo instruction changes.

Expected XR invariants:

- One `XRInteractionManager`.
- One `XROrigin`.
- Main camera under `BlockiverseXRRig/Camera Offset/Main Camera`.
- One `EventSystem` using `XRUIInputModule`.
- Controller ray interaction still works for menus.
- Active tool hand/dominant hand owns menu pointer focus.
- Gameplay input is suppressed when modal/menu route says `pauseGame` or disallows world input.

### Runtime Flow Validation

Walk every route on desktop Editor Play Mode first, then on Quest.

Required route walks:

1. Launch -> Startup Loading -> Controller Mapping -> Title.
2. Title -> New World -> Create -> World Loading -> Gameplay.
3. Title -> Load World -> World Details -> Play -> Gameplay.
4. Title -> Load World -> World Details -> Delete -> Confirm cancel/accept.
5. Title -> LAN -> Host -> Gameplay.
6. Title -> LAN -> Join failure -> Status/Error -> LAN.
7. Title -> Settings -> Comfort/Audio/Controls -> Title.
8. Gameplay -> Menu button -> Pause -> Resume.
9. Gameplay -> Pause -> Save -> status remains sane.
10. Gameplay -> Pause -> Settings -> Close -> Pause.
11. Gameplay -> Pause -> Return to Title.
12. Gameplay -> Pause -> Creative Tools -> Close -> Pause.
13. Gameplay -> Quick Menu -> Block Catalog -> select/search/page -> Gameplay.
14. Gameplay -> Inventory -> Item Details -> close/back.
15. Gameplay -> Crafting -> Recipe Pin -> Gameplay overlay.
16. Gameplay -> Container -> transfer -> close.
17. Gameplay -> each station variant -> deposit/fuel/output/close.
18. Gameplay -> Farming Action -> till/plant/harvest/close.
19. Gameplay -> Map/Wayflag -> set/clear marker -> close.
20. Gameplay -> player death -> Death -> respawn bedroll/world spawn.
21. Gameplay -> player death -> Death -> Return to Title.
22. Gameplay/LAN -> Network Command Status -> dismiss/retry/open hinted panel.
23. Gameplay/LAN -> Survival Rejection -> dismiss/retry/open hinted panel.
24. Status Hub -> Avatar/Policy/Diagnostics -> back/close.

Quest validation:

- Build/install with the repo's current Quest install flow.
- Verify tracked controller ray hover, click, scroll, slider drag, toggle, text entry, menu/back, quick menu, and dominant-hand behavior.
- Verify menu surfaces are readable at expected scale, distance, and head pose.
- Verify no menu text or controls overlap at Quest resolution.
- Verify modals block underlying menu/gameplay input.
- Verify world input resumes after closing menus.
- Verify LAN flows with two devices or one device plus editor host when practical.

## Completion Criteria

The migration is complete only when all of these are true:

- Every target menu in this plan either exists as a UITK route/surface or is explicitly deferred with a product decision.
- Every menu option/button has a documented purpose and a bound action/setting/model command.
- Current project functionality is preserved:
  - create/load/save/world-details/delete,
  - LAN host/join/stop,
  - gameplay HUD,
  - pause/settings/death,
  - creative tools/block catalog,
  - station interaction.
- Useful legacy functionality is migrated:
  - player/context/status structure,
  - inventory/crafting/container/farming summaries,
  - diagnostics/avatar/policy/network/rejection surfaces.
- Boot scene regeneration owns all menu wiring.
- UGUI menu fallback is removed or explicitly retained with a documented engine/runtime reason.
- `scripts/unity/run-tests.sh` passes.
- Unity MCP scene/UI/console validation passes.
- Quest runtime route walk passes.

## Risks And Mitigations

| Risk | Mitigation |
| --- | --- |
| UI Toolkit world-space/XR pointer behavior differs from UGUI tracked-device raycasting. | Validate with a smoke-test UIDocument before broad conversion. Keep UGUI fallback until Quest interaction is proven. |
| Big-bang UI replacement breaks save/load or session flow. | Convert by phase behind one feature flag and keep route/action tests green. |
| Generated scene wiring drifts from source code. | Make bootstrapper the only source of generated menu scene objects and add scene-contract tests. |
| Legacy diagnostics clutter player flow. | Gate diagnostics/status screens by build type, developer setting, or secondary status hub. |
| Hidden string action mistakes create dead buttons. | Keep stable action IDs, add binding tests, and assert unknown action IDs in development builds. |
| Inventory/crafting/station scope expands too far. | Implement model contracts first, then convert one route at a time with explicit exit criteria. |
| Quest performance/regression risk from multiple live documents. | Use a small number of UIDocuments, template reuse, controlled list virtualization/paging, and profile representative routes. |

## Recommended First Implementation Slice

Start with the smallest slice that proves the whole architecture:

1. Add route/action inventory tests around existing `MenuActions` and `UiScreenRouter`.
2. Add UITK foundation assets and one bootstrapper-generated `UIDocument` surface.
3. Convert Confirm/Error and Title first.
4. Validate Title -> Confirm cancel/accept and Title -> Settings fallback interaction.
5. Convert New World and Load World only after the first slice passes in Editor and on Quest.

This slice proves UXML/USS authoring, UIDocument bootstrap wiring, XR menu pointer interaction, action dispatch, modal priority, and route stack behavior before touching high-risk gameplay HUD, inventory, crafting, stations, and multiplayer status routes.
