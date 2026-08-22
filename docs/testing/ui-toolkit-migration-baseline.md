# UI Toolkit Migration Baseline

The reference state the UI Toolkit migration is measured against ([ADR 0010](../adr/0010-ui-toolkit-runtime-ui.md),
[migration matrix](../ui/menu-migration-matrix.md)).

Every later phase compares to the numbers here. A number recorded without the commit it was
measured on is worse than no number — three parallel sessions produced three different, all
legitimate, test totals on the same afternoon, and each was meaningless without its tree.

**Baseline commit: `94bec837` (`main`, 2026-08-22).**
The branch was merged up to that commit before measuring, so the totals below are comparable to
main's rather than to an older base.

---

## 1. Environment

| Item | Version | Source |
|---|---|---|
| Unity | `6000.5.8f1` | `ProjectSettings/ProjectVersion.txt` |
| XR Interaction Toolkit | `3.5.1` | `Packages/manifest.json` |
| OpenXR | `1.17.1` | `Packages/manifest.json` |
| Input System | `1.20.0` | `Packages/manifest.json` |
| URP | `17.5.0` | `Packages/manifest.json` |
| Meta XR SDK | `205.0.0` | `Packages/manifest.json` (Interaction, Interaction OVR, Platform) |
| Meta XR SDK core | `205.0.0`, **embedded** | `Packages/com.meta.xr.sdk.core/package.json` — the manifest entry is `file:com.meta.xr.sdk.core` and carries no version |
| uGUI | `2.5.0` | `Packages/manifest.json` |
| UI Toolkit | built-in `com.unity.modules.uielements` | `Packages/manifest.json` |

Both documented minimums for native world-space UI Toolkit are met: Unity 6.2 and XRI 3.2.0
(`Documentation~/ui-world-space-ui-toolkit-support.md`). Ray-based interaction specifically landed
in XRI 3.4.0.

**No package upgrade is required for this migration, and none may be made as part of it.**

---

## 2. Starting UI inventory

`Assets/Blockiverse/Scripts/UI/` — 27 files, 409,037 bytes.

| File | Bytes |
|---|---|
| `BlockiverseMenuController.cs` | 50,539 |
| `BlockiverseWorldSessionController.cs` | 46,425 |
| `BlockiverseLocalization.cs` | 43,906 |
| `BlockiverseMultiplayerSessionMenu.cs` | 38,774 |
| `SurvivalCraftingPanel.cs` | 23,805 |
| `BlockiverseCreativeToolsPanel.cs` | 22,101 |
| `BlockiverseWorldSpacePanelPresenter.cs` | 18,906 |
| `BlockiverseComfortMenu.cs` | 18,692 |
| `SurvivalHudController.cs` | 17,271 |
| `BlockiverseStationPanel.cs` | 15,903 |
| *(17 more)* | |

UI Toolkit assets in the tree at baseline: **zero**. PR #324's migration was reverted by `fe73bdbf`.

Bootstrapper partials that generate UI:
`BlockiverseProjectBootstrapper.Menus.cs` (74,113 B), `.GameMenus.cs` (64,837 B),
`.XrRig.cs` (52,947 B), `.Scenes.cs` (40,987 B).

---

## 3. Test gate

Run with `scripts/unity/run-tests.sh` (EditMode then PlayMode), editor closed.

> **Do not hand-roll the Unity invocation.** The script passes `-nographics` for EditMode only. A
> PlayMode run under `-nographics` segfaults (exit 139) and — worse — can make tests *fail* rather
> than merely crash, because a test resolving a position through a camera it creates behaves
> differently with no graphics device. That has already been misread as a real regression once.
> Use `--platform` / `--filter` instead.

| Tree | EditMode | PlayMode | Source |
|---|---|---|---|
| `main` @ `94bec837` | 1145 | 146 | reported by a parallel session |
| This branch, Phase 0 + 1 | **1182** | **146** | `TestResults/Unity/{EditMode,PlayMode}.xml`, this session |

The two reconcile exactly: this branch adds 37 EditMode tests and no PlayMode tests, and
1145 + 37 = 1182. That arithmetic is the check worth doing — a total that cannot be accounted for
means either the count or the branch is not what it is believed to be.

Phase 0/1 adds **37 EditMode tests** across two files:

| File | Tests | Covers |
|---|---|---|
| `XrUiToolkitConfigurationEditModeTests.cs` | 20 | validator rules, each asserted from both sides |
| `UiToolkitBootstrapEditModeTests.cs` | 17 | generated PanelSettings, infrastructure, proof panel, idempotency, counters |

### Known coverage gaps, recorded rather than papered over

- **`UiToolkitProofPanel.CallbackRegistrationBalance` across enable/disable cycles.**
  `UIDocument.rootVisualElement` is not built in EditMode, so `Attach` takes its early return and
  the balance never leaves 0. An EditMode test would pass without the code under test ever running.
  Belongs in PlayMode; Phase 2 work.
- **`XrUiToolkitSceneInspector` has no tests.** It is the only thing that turns a real scene into
  the snapshot the validator judges, so a bug there makes the whole validator report on fiction.
  It needs a scene to exercise, which is PlayMode or an additive-scene EditMode fixture. Phase 2.
- **The proof panel's real ray interaction.** By construction — that is what the device pass is
  for, and it is the reason Phase 1 does not end at "tests pass".

### Why each rule is tested twice

Every validator rule has both a "correct configuration produces no finding" case and a "breaking
exactly this field produces it" case. A single-sided suite here would pass just as happily if
`Validate()` returned an empty list unconditionally — and this is precisely the failure mode that
recently shipped elsewhere in this project, where a peer-event test used a single client, so the
code path never executed and every assertion passed by vacuum.

---

## 4. Post-run hygiene

A Unity batchmode run dirties files nobody touched. **Diff the whole tree, not the paths you
expected to change**, and revert these rather than reasoning about whether they look intentional:

| Artefact | What happens | Action |
|---|---|---|
| `ProjectSettings/ProjectSettings.asset` | `SENTIS_ANALYTICS_ENABLED` moves between build-target define lists during the PlayMode target switch. **Direction varies** — it has been observed moving onto Standalone and off Android on different runs. | revert |
| `Assets/UniversalRenderPipelineGlobalSettings.asset` | `m_RuntimeSettings.m_List` occasionally emptied. Must contain **13** entries. | revert unconditionally |
| `Assets/InitTestScene<guid>.unity` (+ `.meta`) | left behind by PlayMode | delete; never `git add -A` |

The URP one is the highest-consequence and the least obvious. Those 13 entries are the runtime
resource pointers URP carries into a player build — XR runtime resources, variable rate shading
(this project pins foveation), and shader stripping among them. Committed empty, that is a
device-only rendering regression that is clean in the editor. It is intermittent; clean runs are
the norm and prove nothing.

---

## 5. Device baseline

| Device | Available | Notes |
|---|---|---|
| Quest 3 (`eureka`, `2G0YC1ZG4106F1`) | yes | `hzdb device list` |
| Quest 3S | **no** | not attached in this session |

**Quest 3S is the lower-performance target and therefore the one that decides the performance
questions.** Every claim in this migration about 3S behaviour is unvalidated until a 3S is
available. Phase 1's exit criteria require both; only the Quest 3 half can be satisfied here.

### Performance baseline

`docs/testing/performance/` holds **no committed capture**. There is therefore nothing for "no
sustained frame-rate regression" to be measured against yet, for UI or for anything else — the
lighting and water work carry the same open gate.

Captures needed before Phase 4 (the first phase with large lists):

- Static routed menu open, seated, fixed pose and seed
- Long list scroll (load-world list; later inventory/crafting)
- Rapid route changes
- Idle HUD, and HUD under a multiplayer snapshot burst
- Managed memory after repeated menu open/close cycles
- Dynamic atlas dimensions and memory

Method: `ovrgpuprofiler` on one seed and pose, per `docs/testing/performance/README.md`.

---

## 6. Phase 1 exit criteria and their status

| Criterion | Status |
|---|---|
| Code compiles | **met** — one error across ~1250 new lines (a named argument) |
| Full existing suite green | **met** — EditMode 1182/1182, PlayMode 146/146 |
| Generated configuration is what the validator accepts | **met** — asserted end to end, with a control proving the layer rule fires |
| No custom input or rendering code introduced | **met** — the Toolkit assembly references only `Blockiverse.Core` |
| Text renders crisply in headset | pending device run |
| Both controllers hover and activate | pending device run |
| One press produces one activation | pending device run |
| Scroll works without a tiny handle | pending device run |
| Quest keyboard opens for `TextField` | pending device run — **the least certain of these**, see below |
| Hidden UI cannot intercept world rays | covered by EditMode test; pending device confirmation |
| Quest 3 | pending — APK building |
| Quest 3S | **blocked, no device attached** |

### Tests were checked for discrimination, not trusted for being green

With `Validate()` gutted to return an empty list, **17 of 20** validator tests failed. The three
survivors were the correct-configuration case and the asmdef check — neither of which reads a
finding — plus `LayerMaskArithmeticHandlesTheEdgeLayers`, which asserted only absences and so
passed against a validator that reported nothing at all. That test now leads with a control
showing the same rule firing on the same fixture, and the sabotage now leaves exactly the two
expected survivors.

The same check was applied to the governance test: restoring the superseded shared-Quad wording
to the ruleset makes it fail, and removing it makes it pass. A rule that cannot be shown failing
is not evidence of anything.

**The keyboard criterion deserves separate attention.** The uGUI path needed a custom
`BlockiverseSystemKeyboardField` component and `TMP_InputField.shouldHideSoftKeyboard = true` to
make Quest text entry work at all, which is evidence the default path did *not* work there. Whether
UI Toolkit's `TextField` invokes the system keyboard natively is unproven for this project. The
OpenXR feature is already enabled (`enableSystemKeyboard: 1`, enforced by the bootstrapper), and the
Android activity is deliberately kept as Classic rather than GameActivity because GameActivity
breaks the keyboard handshake — so the platform side is in place. The UI Toolkit side is the
unknown, and it is the single most likely thing to send Phase 1 back for more work.
