# Codebase Review — Accessibility Expert

> Workflow run `wf_53b36881-009`, agent `af19a298a40ba0ca1`. Raw expert output, pre-verification.

## Area Reviewed

Accessibility review of Blockiverse VR: VR comfort options and their defaults (BlockiverseComfortSettings, BlockiverseVignetteSettingsDriver, TunnelingVignette wiring), locomotion and turning (BlockiverseInputRig, BlockiverseLocomotionMode), height calibration and seated play (BlockiverseHeightReset, XROrigin tracking mode), input accessibility (input-action generation in the bootstrapper, hand-role assignments, hold-to-mine), settings persistence (BlockiverseSettingsPersistence), the generated settings UI (comfort menu, audio/feedback panel, controls reference in BlockiverseProjectBootstrapper), multi-channel feedback (audio cues, haptics, VFX, status text, vitals HUD), colorblind/readability concerns (UI palette, font sizes, crop-stage atlas tiles), and difficulty options versus what the simulation actually consumes. The foundation is genuinely good for this stage — teleport/glide modes, snap-vs-smooth turn, vignette with strength control, per-category volumes, haptics toggle+intensity, reduced flash/particles, text-first menus with strong contrast, and persisted settings. The biggest verified gaps: the default comfort vignette is mathematically a no-op and its slider label is inverted; hand roles are hard-coded with no left-handed or one-handed support; damage/death and several audio-only events are single-channel; the difficulty selector changes nothing in the simulation; and the spec-mandated feedback-toast layer, text/UI scale options, move-speed and eye-height controls, and toggle-to-mine alternative are all absent.

## Findings (18)

### 1. Default comfort vignette is effectively disabled (aperture 1.0) and the 'Strength' slider is inverted

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseVignetteSettingsDriver.cs`
- **Impact:** New players — the population most vulnerable to VR motion sickness — receive no tunneling vignette at all by default, despite the comfort menu showing 'Motion Vignette' enabled. Additionally, dragging the 'Strength' slider to maximum removes the vignette while dragging to minimum maximizes it, the opposite of what the label communicates, so players trying to increase comfort protection will turn it off.
- **Evidence:** BlockiverseComfortSettings.cs line 24 defaults `vignetteStrength = 1.0f`; line 73 `public float VignetteAperture => vignetteEnabled ? 0.6f + vignetteStrength * 0.4f : 1.0f;` — with defaults this yields apertureSize 1.0, which in XRI's TunnelingVignetteController means the transparent aperture covers the full view (no vignette). The bootstrapper comment at BlockiverseProjectBootstrapper.cs line 2253 claims 'aperture 0.85 is subtler than the XRI default (0.7)' but line 2251 computes `vignetteSettings.VignetteAperture` = 1.0 whenever settings exist. EnsureVignetteSlider (line 3366) labels the slider 'Strength' with LeftToRight direction, so slider max = aperture 1.0 = vignette off.
- **Recommended fix:** In BlockiverseComfortSettings, either invert the mapping (aperture = 1.0 - strength * 0.4) so higher 'Strength' means stronger vignette and pick a default that produces a visible-but-subtle aperture (~0.85, i.e. strength ≈ 0.37 under the inverted mapping), or rename the slider label in EnsureVignetteSlider to 'Aperture / Openness'. Add a PlayerPrefs migration in BlockiverseSettingsPersistence.LoadSettings so existing saved values map to the new semantics. Re-run the bootstrapper to regenerate the comfort menu.

### 2. Hand roles are hard-coded; no left-handed mode, and the game is unplayable one-handed

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseSettingsPersistence.cs`
- **Impact:** Left-handed players cannot swap movement to the right stick or break/place to the left hand. Players with use of only one hand are locked out entirely: a right-hand-only player cannot move in glide mode (left stick only), open the pause menu (left controller menu button), or open the blocks menu (left grip); a left-hand-only player cannot turn (right stick only), break/place (right trigger/grip), or jump (right A).
- **Evidence:** BlockiverseInputRig.ConfigureXriProviderInputs (lines 577–618): `continuousMoveProvider.rightHandMoveInput = CreateUnusedVector2Reader("Right Hand Move")` and `snapTurnProvider.leftHandTurnInput = CreateUnusedVector2Reader("Left Hand Snap Turn")` — the off-hand inputs are explicitly Unused. RefreshCachedActions (lines 305–310) binds break/place to RightHandMap Select/Activate and quick menu to LeftHandMap Activate. Bootstrapper line 1060 binds Menu to `<XRController>{LeftHand}/menuButton`, line 1061 Jump to `{RightHand}/primaryButton`. docs/rulesets/voxel_survival_menus.md §6.20 declares 'Bindings are fixed to the Quest controller layout (no remapping)'. No handedness field exists in BlockiverseComfortSettings or BlockiverseSettingsPersistence.
- **Recommended fix:** Add a `DominantHand` (or `SwapHands`) setting to BlockiverseComfortSettings, persist it in BlockiverseSettingsPersistence, expose a toggle in the comfort menu (bootstrapper EnsureXrRigComfortMenu), and honor it in BlockiverseInputRig.ConfigureXriProviderInputs / RefreshCachedActions by swapping which hand map feeds move/turn/break/place/jump, and in EnsureXrRigFeedback's FindControllerHaptics(rig, Right) so 'dominant hand' haptics follow the setting. Longer term, consider a one-handed preset (move+turn on one stick via mode toggle, teleport already works from either stick). Update the Controls reference text to reflect the active mapping.

### 3. Damage, low health, and death have no audio or haptic channel — HUD text is the only feedback

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs`, `Assets/Blockiverse/Scripts/Gameplay/BlockiverseAudioCuePlayer.cs`, `scripts/audio/generate-audio.py`
- **Impact:** Taking hazard damage (campfire, thornbrush), starvation/dehydration damage, reaching critical health, and dying produce no sound, no haptic pulse, and no in-view flash; the only signal is a number/bar changing on the body-locked Survival HUD panel, which refreshes on a 0.5 s cadence and may not be in the player's view. Players with low vision, attention differences, or simply looking elsewhere die without warning. This violates the project's own multi-channel rule (voxel_audio_vfx_ruleset §15: important events need at least two feedback channels).
- **Evidence:** SurvivalVitalsRuntime applies damage (line 142 `Vitals.ApplyDamage(starvationDamage)`, lines 188–195 hazard ApplyTick) with no feedback call. The only consumers of PlayerVitals.HealthChanged/Died are SurvivalHealthPanel.Refresh (text/slider, lines 127–130) and the death-screen route in BlockiverseMenuController.HandleLocalPlayerDied. The BlockiverseAudioCue enum (BlockiverseAudioCuePlayer.cs lines 8–39) contains no damage/hurt/low-health/death cue, and Assets/Blockiverse/Audio/ contains no hurt clip (generate-audio.py has no such generator). SurvivalHealthPanel's 'Critical' state (line 137) is plain text with no color, sound, or haptic escalation.
- **Recommended fix:** Add DamageTaken / LowHealthWarning / PlayerDied cues to BlockiverseAudioCue, generate clips in scripts/audio/generate-audio.py, assign them in the bootstrapper's ConfigureGeneratedAudioClips, and have SurvivalVitalsRuntime (or a small vitals feedback bridge like SurvivalFeedbackBridge) subscribe to Vitals.HealthChanged/Died to play the cue plus a haptic pattern (scaled by BlockiverseFeedbackSettings.ResolveHapticIntensity, respecting ReducedFlash for any visual pulse). Add a periodic low-health/low-hunger/low-thirst warning when values cross thresholds.

### 4. Difficulty selector (easy/normal/hard) has no gameplay effect — no actual assist/easy mode exists

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`, `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- **Impact:** Players who select 'easy' for accessibility reasons (slower vitals drain, reduced hazard damage) get a simulation identical to 'hard'. The setting is shown in New World creation, saved to the manifest, and displayed in World Details, which misleads players into believing an accessibility-relevant difficulty system exists.
- **Evidence:** NewWorldConfig.cs line 13 defines `DifficultyOptions = { "easy", "normal", "hard" }`. Repo-wide grep shows Difficulty is only consumed by UI display (BlockiverseWorldDetailsPanel.cs line 49), save manifest round-trip (WorldSaveService.cs lines 254, 429; WorldSaveDirectorySchema.cs line 19), and session metadata (BlockiverseWorldSessionController.cs lines 195, 274, 636). Nothing in Survival, SurvivalHealth, WorldGen, or Gameplay (SurvivalVitals.Tick, BlockHazards, FarmingService, ResourceHarvestService) reads it.
- **Recommended fix:** Wire difficulty into the vitals/hazard simulation: pass the loaded world's difficulty from BlockiverseWorldSessionController into SurvivalVitalsRuntime and scale SurvivalVitals depletion rates and hazard damage (e.g. easy = 0.5x drain/damage, or a 'peaceful/assist' tier that disables starvation damage). Until implemented, either remove the selector from the New World panel (bootstrapper EnsureNewWorldMenuPanel) or label it as not yet functional, and document actual behavior in the wiki.

### 5. Spec-mandated feedback-toast/subtitle layer is absent; several events are audio-only with no visual channel

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs`, `docs/rulesets/voxel_audio_vfx_ruleset.md`, `docs/rulesets/voxel_survival_menus.md`
- **Impact:** Deaf and hard-of-hearing players miss information that is only ever played as sound: wrong-tool harvest rejection (ToolWrong cue), multiplayer peer join/leave stingers, thunder (the lightning flash is suppressible via Reduced Flash, making storms nearly invisible-audio-only), and teleport confirmation. The project's own rulesets require a 'Subtitles/Feedback Toasts toggle' and a BlockiverseSubtitleToastPanel accessibility layer, plus toast messages like 'This tool is not strong enough.' — none are implemented.
- **Evidence:** voxel_audio_vfx_ruleset.md line 81 lists `BlockiverseSubtitleToastPanel  // optional accessibility layer`, line 115 'Subtitles/Feedback Toasts toggle', and §15 requires 'Feedback Toasts: Shows text/icon confirmation for important audio-only events'. Grep for 'Subtitle|Toast' across Assets/Blockiverse/Scripts finds no runtime implementation. SurvivalFeedbackBridge.cs lines 113–116 plays only `BlockiverseAudioCue.ToolWrong` for InsufficientTool rejections (no status text), and lines 152–166 play MultiplayerJoin/Leave cues with no visual counterpart. voxel_survival_menus.md §10.4 specifies toast strings ('This tool is not strong enough.', 'Game saved.') that have no UI surface.
- **Recommended fix:** Implement a world-space toast panel (new UI script + bootstrapper generation alongside the Survival HUD) that subscribes to MultiplayerSurvivalSync.CommandFeedback, NetworkManager connect/disconnect, and save events, showing the §10.4 strings; add a 'Feedback Toasts' toggle to BlockiverseFeedbackSettings, persist it in BlockiverseSettingsPersistence, and expose it on the audio/feedback settings panel.

### 6. Harvest rejected for full inventory produces zero feedback on any channel

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- **Impact:** When a player mines a block but their inventory is full, the host rejects the harvest and nothing happens — no sound, no haptic, no status text, no toast. The block stays, and the player cannot tell whether the game missed the input, the tool is wrong, or the inventory is full. This is confusing for everyone and a hard blocker for players who rely on explicit feedback.
- **Evidence:** MultiplayerSurvivalSync rejects with `SurvivalCommandFailureReason.InventoryFull` (lines 1874, 1893, 2000). SurvivalFeedbackBridge.OnCommandFeedback (lines 105–117) only handles `result.Accepted` and `HarvestFailureReason == BlockHarvestFailureReason.InsufficientTool`; every other rejection (including InventoryFull) falls through silently. The ruleset (voxel_survival_menus.md §10.4) specifies 'Inventory full. Dropped overflow nearby.'
- **Recommended fix:** Add an InventoryFull branch in SurvivalFeedbackBridge.OnCommandFeedback playing CraftFail/UiCancel plus a haptic tick, and surface the §10.4 message via the crafting/status label or the toast layer once it exists.

### 7. 'Reset Height' is a no-op under Floor tracking origin, and there is no seated-play height calibration

- **Severity:** Medium  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseHeightReset.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs`
- **Impact:** Seated players (including wheelchair users) cannot raise their in-world eye height: the rig uses Floor tracking origin, where XROrigin ignores CameraYOffset, so the comfort menu's only height control does nothing on Quest. The StandingEyeHeight setting (1.0–2.2 m) is persisted but has no UI slider and no effect in Floor mode. The body-locked Survival HUD is also generated at a fixed local height of 1.38 m, which sits at or above a seated player's eye level.
- **Evidence:** BlockiverseHeightReset.ResetHeight (lines 19–27) only sets `origin.CameraYOffset`. BlockiverseProjectBootstrapper.cs lines 1745 and 1815 set `origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor`; Unity's XROrigin moves the camera offset to 0 in Floor mode, so CameraYOffset writes are ignored. The comfort menu (EnsureXrRigComfortMenu lines 2127–2188) has no eye-height slider even though voxel_survival_menus.md §6.19 lists 'standing eye height' as a Comfort setting. EnsureXrRigSurvivalHud line 2781 fixes the HUD at localPosition (0, 1.38, 1.15) on the camera offset.
- **Recommended fix:** Implement a real seated-mode offset: add a 'Seated mode / height offset' setting in BlockiverseComfortSettings, apply it as an additional Y translation on the XROrigin CameraFloorOffsetObject (works regardless of tracking origin mode), wire 'Reset Height' to capture current head height and compute the offset against StandingEyeHeight, expose the eye-height slider in EnsureXrRigComfortMenu, and position the Survival HUD relative to the current head height (or give it a BlockiverseWorldSpacePanelPresenter that recenters like the menus do).

### 8. No text-size or UI-scale options; smallest generated labels are near the VR legibility floor

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseWorldSpacePanelPresenter.cs`, `docs/rulesets/voxel_survival_menus.md`
- **Impact:** Players with low vision cannot enlarge UI text or panels. Several generated labels (20–22 pt at panel scale 0.0013 viewed at 1.1–1.3 m) subtend roughly 1.0–1.3 degrees — at or below comfortable VR reading size on Quest — with no way to compensate. The project's own ruleset (§10.3 'Text size and UI scale should be configurable') requires this.
- **Evidence:** GameMenuScale = 0.0013 (BlockiverseProjectBootstrapper.cs line 94) with presenters configured at 1.1 m distance (e.g. line 4008). 20 pt labels (line 4513, creative tools status) and 22 pt body text (line 4626, controls mapping; line 4686, world details metadata) yield ~0.026–0.029 m line height at 1.1 m ≈ 1.35° including line spacing, with cap height below 1°. voxel_survival_menus.md §10.3 mandates configurable text size/UI scale; no such setting exists in BlockiverseComfortSettings, BlockiverseFeedbackSettings, or BlockiverseSettingsPersistence.
- **Recommended fix:** Add a 'UI Scale' setting (e.g. 0.8–1.5x) to BlockiverseComfortSettings, persist it in BlockiverseSettingsPersistence, expose a slider on the comfort panel (bootstrapper), and multiply it into BlockiverseWorldSpacePanelPresenter.panelScale (Recenter already applies panelScale on every show, so a runtime multiplier takes effect immediately). Raise minimum generated body text to ≥24 pt while at it.

### 9. Comfort menu omits the move-speed and eye-height controls its own spec requires

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs`, `docs/rulesets/voxel_survival_menus.md`
- **Impact:** ContinuousMoveSpeed (0.5–4.0 m/s) is a key comfort/accessibility variable — slower movement reduces vection sickness, faster reduces exposure time — but players have no UI to change it from the 1.8 default; it is only reachable by editing PlayerPrefs. Standing eye height likewise has no control. The menus ruleset §6.19 explicitly lists both under the Comfort section.
- **Evidence:** voxel_survival_menus.md §6.19 Comfort row: 'Locomotion mode (glide/teleport), move speed, smooth/snap turn, snap-turn degrees, standing eye height, vignette toggle/strength'. EnsureXrRigComfortMenu (BlockiverseProjectBootstrapper.cs lines 2127–2192) builds only glide/teleport toggles, smooth-turn toggle, snap-turn slider, vignette toggle+slider, and the height-reset button; BlockiverseComfortMenu.ConfigureControls (lines 45–61) has no move-speed or eye-height parameters. BlockiverseComfortSettings.ContinuousMoveSpeed (lines 32–36) and StandingEyeHeight (50–54) exist and are persisted (BlockiverseSettingsPersistence lines 70–77) but are UI-orphaned.
- **Recommended fix:** Add a 'Move Speed' slider (0.5–4.0, default 1.8) and a 'Standing Eye Height' slider (1.0–2.2) to EnsureXrRigComfortMenu using the existing EnsureSettingsSlider builder, extend BlockiverseComfortMenu.ConfigureControls and ApplyOtherControlsWithFeedback to bind them, and rerun the bootstrapper. BlockiverseInputRig.ApplyComfortSettingsToProviders already pushes move speed every change, so no rig work is needed.

### 10. Menu button is double-wired: it toggles the comfort panel and drives the pause-menu flow simultaneously

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`
- **Impact:** Every press of the left-controller menu button both routes the pause/escape logic (BlockiverseMenuController.OnMenuPressed) and toggles the comfort settings panel's presenter, so pausing in gameplay can pop the comfort panel over/behind the pause menu with overlapping audio cues — confusing, visually cluttered, and especially disorienting for players who rely on predictable UI behavior. It also means the comfort panel (which has no Close button) can appear when the player only wanted to pause.
- **Evidence:** EnsureXrRigComfortMenu adds a persistent listener: line 2207 `UnityEventTools.AddPersistentListener(inputRig.MenuPressed, presenter.ToggleVisible)` (only the comfort menu's own stale listeners are scrubbed first, lines 2199–2206). BlockiverseMenuController.Start line 199 adds `inputRig.MenuPressed.AddListener(OnMenuPressed)` at runtime, and the bootstrapper at lines 3919–3926 removes only the controller's stale persistent listener, leaving the comfort presenter's persistent listener in place. The comfort menu is not registered in screenPresenters, so ApplyRouterState never hides it.
- **Recommended fix:** Remove the MenuPressed → comfort-presenter persistent listener in EnsureXrRigComfortMenu (or scrub it in EnsureXrRigGameMenus the same way the controller listener is scrubbed) and make Settings → Comfort the canonical entry point, routing the comfort panel through the UiScreenRouter as its own screen so it gains consistent show/hide, a Close action, and modal behavior.

### 11. Comfort panel has no Close button and is not router-managed

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** Opening Settings → Comfort shows the panel via canvas.enabled only (no recenter, no router entry). The only dismissal is the Menu button, whose generic escape-hatch also pops the underlying Settings screen — so closing the comfort panel kicks the player out of Settings, and because Show() bypasses the presenter, the panel appears wherever it was last placed rather than recentering to the player's head.
- **Evidence:** BlockiverseMenuController.HandleAction lines 483–485: `case MenuActions.SettingsOpenComfort: comfortMenu?.Show();` with no router push and no presenter recenter (BlockiverseComfortMenu.Show, lines 71–78, just enables the canvas). EnsureXrRigComfortMenu (lines 2115–2188) generates title, toggles, sliders, and a Height Reset button but no Close button. OnMenuPressed (lines 133–138) pops the active screen as a generic escape hatch.
- **Recommended fix:** Add a Close button to the comfort panel in EnsureXrRigComfortMenu wired to hide it (or, better, register the comfort panel as a router screen with `settings.open_comfort` pushing it like Audio/Controls), and call the panel's presenter.Show() instead of canvas-enable so it recenters to the head when opened.

### 12. Hold-to-mine is the only mining input; spec-required toggle-to-mine alternative is missing

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs`, `docs/rulesets/voxel_survival_menus.md`, `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`
- **Impact:** Survival mining requires holding the right trigger continuously for the full break duration while keeping aim on the block. Players with limited grip strength, tremor, or fatigue conditions cannot sustain trigger holds; the project's own accessibility rules (§10.3 'Hold-to-mine and toggle-to-mine should both be supported') require an alternative that does not exist.
- **Evidence:** BlockiverseCreativeInputBridge.cs line 35: 'Hold-to-mine (§7.3): survival break is a timed hold on a fixed target', line 255: 'Survival mode: hold-to-mine — the press starts a timed hold'; BlockiverseInputRig.IsBreakHeld (line 95) is polled as a release safety net. voxel_survival_menus.md §10.3 requires both hold-to-mine and toggle-to-mine. No toggle setting exists in BlockiverseComfortSettings/BlockiverseFeedbackSettings.
- **Recommended fix:** Add a 'Toggle to mine' option (BlockiverseComfortSettings + persistence + comfort menu toggle in the bootstrapper). In BlockiverseCreativeInputBridge, when enabled, treat the first break press as starting the hold and cancel on a second press or on aim leaving the block, instead of requiring IsBreakHeld.

### 13. Art-generation script is stale versus the committed atlas: regenerating destroys crop-stage and fluid tiles

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `scripts/art/generate-art-assets.py`, `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`, `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png`
- **Impact:** The committed block atlas is 8x10 tiles (indices up to 75, including the crop growth-stage tiles that are the game's visual channel for ripeness, plus saplings, fluids, and storage blocks), but scripts/art/generate-art-assets.py builds an 8x7 atlas containing only tiles 0–49. Following the project's documented workflow ('never hand-author; regenerate instead') would silently wipe the growth-stage visuals, leaving all crop stages rendering background-gray or wrong tiles — eliminating the only indicator of crop ripeness.
- **Evidence:** generate-art-assets.py lines 14–15: `ATLAS_COLUMNS = 8`, `ATLAS_ROWS = 7`; its BLOCKS table ends at ('mend_bench', 49, ...). BlockVisualAtlas.cs line 12 declares `Rows = 10` and maps tiles 50–75 (GrainStalk_S1=53 … Emberflow=75, lines 72–101). The committed PNG is 128x160 px = 8x10 tiles (verified via PNG header).
- **Recommended fix:** Extend generate-art-assets.py: set ATLAS_ROWS = 10 and add generator entries for tiles 50–75 (tended_soil, all GrainStalk/Berrybush/Reedgrass/Sapling stages with progressively denser/taller patterns so stages differ by shape and not only color, lumen_lamp, spark_flare, containers, fluids), then regenerate and diff against the committed atlas.

### 14. Berry/crop ripeness and ore identification may rely on hue contrasts weak for red-green colorblindness; no colorblind options

- **Severity:** Low  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `scripts/art/generate-art-assets.py`, `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`
- **Impact:** The mature berrybush is signaled by red berry accents (203,56,91) on a green bush (50,112,54) — a classic deuteranopia/protanopia confusion pair — and several ores differ mainly by accent hue. There are no colorblind palettes or filters. Mitigating factors: growth stages use distinct atlas tiles (shape/density can differ, pixels not verified here), and harvesting feedback is text/audio based.
- **Evidence:** generate-art-assets.py line 60: `("berrybush", 41, (50, 112, 54), (203, 56, 91), "berries", 283)` — red accent on green base; tool/ore entries (raw_rosycopper (241,135,70) vs sunmetal_fleck (255,196,74)) also differentiate by warm hues. No colorblind setting exists anywhere in Scripts/.
- **Recommended fix:** When extending the art generator for the stage tiles (see atlas finding), encode growth stage primarily by silhouette/height/density rather than accent color, and lighten mature-stage accents for luminance contrast. A full colorblind filter is likely unnecessary if shape coding is done; document the approach in the art script.

### 15. Reduced Flash only attenuates lightning to 35% instead of offering full suppression

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxCuePlayer.cs`, `docs/rulesets/voxel_audio_vfx_ruleset.md`
- **Impact:** Photosensitive players enabling 'Reduced Flash' still get lightning flashes at 35% intensity. The ruleset frames the setting as protection for intense pulses; for photosensitivity, an off option is the safe choice (thunder audio still conveys the storm).
- **Evidence:** BlockiverseVfxCuePlayer.PlayCue lines 40–44: `if (cue == BlockiverseVfxCue.LightningFlash && feedbackSettings.ReducedFlash) intensity *= 0.35f;` — flash is reduced, never skipped.
- **Recommended fix:** When ReducedFlash is enabled, skip the LightningFlash cue entirely (return before pool.Play) or add a separate 'No Flash' tier; keep the ThunderNear/Far audio so storms remain perceivable.

### 16. No quick 180° turn-around option in snap turning

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** Players who need to reverse direction without smooth rotation must chain multiple snap turns (up to 12 presses at the 15° minimum). XRI's built-in turn-around gesture is explicitly disabled with no setting to enable it.
- **Evidence:** BlockiverseInputRig.ConfigureXriLocomotionProviders line 541: `snapTurnProvider.enableTurnAround = false;` with no comfort setting controlling it.
- **Recommended fix:** Add a 'Turn Around (stick down)' toggle to BlockiverseComfortSettings + the comfort menu, and set snapTurnProvider.enableTurnAround from it in ApplyComfortSettingsToProviders.

### 17. No hand-tracking input path; controllers are mandatory

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Packages/manifest.json`, `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`
- **Impact:** Players who cannot hold or operate Touch controllers (limb difference, grip impairment) have no alternative input: all bindings are `<XRController>` paths and no OpenXR hand-tracking feature/subsystem is configured. Worth recording as a known limitation for store metadata and the roadmap rather than an immediate defect.
- **Evidence:** All generated input actions bind `<XRController>{LeftHand}/...` / `{RightHand}/...` (BlockiverseProjectBootstrapper.cs lines 1060–1062 and BlockiverseInputRig pose paths lines 31–40). Grep for hand-tracking configuration across ProjectSettings/ and Packages/manifest.json returns nothing.
- **Recommended fix:** Document 'controllers required' in the wiki/store listing now. If hand tracking is ever added, the XRI ray interactors and the existing UI-first menu design would carry over; gameplay bindings would need pinch-gesture equivalents defined in the bootstrapper's input-action generation.

### 18. Crafting panel lacks per-recipe availability symbols specified by the accessibility rules

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`, `docs/rulesets/voxel_survival_menus.md`
- **Impact:** Recipes show ingredients and '[needs station]' as text (good — no color coding), but there is no ✓/✗/! availability marker per recipe as specified in §10.3, so players must attempt a craft (and parse the status line) to learn whether ingredients suffice. Minor friction, multi-channel failure feedback does exist (CraftFail audio + status text).
- **Evidence:** SurvivalCraftingPanel.FormatRecipe (lines 307–313) renders output, ingredients, and '[needs {station}]' but never checks inventory.CanConsume-style availability; §10.3 specifies '✓ available / ✗ missing / ! wrong station' symbols.
- **Recommended fix:** In FormatRecipe, prefix each recipe label with a symbol derived from ingredient availability against the bound inventory and EffectiveStationFor (text symbols keep it colorblind-safe), refreshing on inventory change events already routed through SurvivalHudController.

## What Looks Good (16)

- Teleport locomotion is a first-class comfort alternative to glide, selectable as a radio pair in the comfort menu with mutual exclusion and no dead 'both-off' state (Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs OnGlideToggled/OnTeleportToggled).
- Snap turn is the default with adjustable degrees (15–90, whole numbers) and smooth turn is opt-in — the correct comfort-first defaults (Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs, BlockiverseInputRig.ApplyComfortSettingsToProviders).
- A tunneling vignette infrastructure exists and is correctly eased on continuous move, smooth turn, and teleport while deliberately excluding snap turn to avoid flicker (BlockiverseProjectBootstrapper.EnsureXrRigTunnelingVignette, lines 2263–2270), with a runtime strength driver (Assets/Blockiverse/Scripts/VR/BlockiverseVignetteSettingsDriver.cs).
- All comfort and feedback settings persist across launches with debounced writes and corrupt-enum validation (Assets/Blockiverse/Scripts/VR/BlockiverseSettingsPersistence.cs, lines 64–69).
- Haptics are fully disableable and intensity-scalable, and every haptic path routes through BlockiverseFeedbackSettings.ResolveHapticIntensity (Assets/Blockiverse/Scripts/Gameplay/BlockiverseFeedbackSettings.cs lines 104–109; Assets/Blockiverse/Scripts/VR/BlockiverseInteractionHaptics.cs SendPattern).
- Audio offers five category sliders plus Mute All, all exposed in the generated audio settings panel and consumed by the cue player including live loop-volume refresh (Assets/Blockiverse/Scripts/UI/BlockiverseAudioSettingsPanel.cs; BlockiverseAudioCuePlayer.ResolveVolume/RefreshLoopVolumes).
- Reduced Flash and Reduced Particles settings exist, are persisted, exposed in UI, and actually consumed by the VFX player (Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxCuePlayer.cs lines 40–44).
- Menus are text-first: every action is a labeled text button, recipe gating uses text ('[needs Forge]') rather than color, selection state is announced in text ('Hotbar 3 / 8', 'No save selected') — satisfying §10.3's color-independence rule (SurvivalCraftingPanel.FormatRecipe, SurvivalInventoryPanel, BlockiverseLoadWorldPanel).
- UI palette has excellent contrast: near-white text (0.95,0.97,1.0) on near-black panels (0.06,0.07,0.09) ≈ 16:1; even dim text is ~8:1 (BlockiverseProjectBootstrapper.cs lines 137–162).
- Vitals are multi-encoded visually — numeric value, bar slider, and a state word ('Stable'/'Critical'/'Down') plus numeric hunger/thirst/stamina (Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs).
- Both controllers carry UI rays with press/scroll inputs, so menu interaction works from either hand (BlockiverseInputRig.EnsureRayInteractorInputs), and text entry uses the system keyboard (Assets/Blockiverse/Scripts/VR/BlockiverseSystemKeyboardField.cs).
- Menu panels recenter to the player's actual head position/height on every show, which inherently works for seated players (Assets/Blockiverse/Scripts/VR/BlockiverseWorldSpacePanelPresenter.Recenter).
- The menu button acts as a universal escape hatch so no screen can trap the player, with a deliberate exception preventing the death screen from being dismissed into a dead-walking state (Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.OnMenuPressed, lines 133–138).
- UI interactions are multi-channel by default: audio cue + haptic tick on menu open/close/select (BlockiverseComfortMenu.PlayFeedback, BlockiverseWorldSpacePanelPresenter.PlayFeedback).
- Weather is presented across three channels — audio loops, particles, and light flashes — so each impairment profile retains at least one channel (Assets/Blockiverse/Scripts/Gameplay/WeatherFeedbackController.cs).
- The controls reference screen and the first-launch popup share one canonical mapping string so they cannot drift (BlockiverseProjectBootstrapper.ControllerMappingText, lines 4380–4391).

## Could Not Review (6)

- On-device verification: actual legibility of 20–22 pt world-space text on Quest 3 panels, perceived vignette behavior at aperture 1.0, and comfort feel of glide defaults can only be confirmed in-headset.
- Exact XR Core Utils behavior of XROrigin.CameraYOffset under Floor tracking origin in the installed package version — the Reset Height no-op finding is based on standard XROrigin semantics and needs a device check.
- Pixel-level distinguishability of the committed atlas's crop-stage tiles (50–75) for colorblind users — the tiles exist in the PNG but their artwork is not reproducible from the committed generator script, so shape-vs-color coding could not be assessed.
- Runtime interplay of the Menu-button double wiring (pause flow + comfort panel persistent listener) — the wiring is verifiable statically but the visible result (overlap, z-order, audio doubling) needs a play test.
- Companion wiki (../Blockiverse-VR.wiki) and website accessibility documentation/store metadata — outside this repository checkout.
- PlayMode test coverage of comfort/settings flows was not exhaustively traced; tests were not executed per review constraints.

## Inspected (36)

- `Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseSettingsPersistence.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseVignetteSettingsDriver.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseHeightReset.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseLocomotionMode.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseInteractionHaptics.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseControllerHaptics.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseWorldSpacePanelPresenter.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseAudioSettingsPanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`
- `Assets/Blockiverse/Scripts/UI/MenuActions.cs`
- `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseFeedbackSettings.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseAudioCuePlayer.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxCuePlayer.cs`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`
- `Assets/Blockiverse/Scripts/Gameplay/WeatherFeedbackController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs (comfort menu, tunneling vignette, survival HUD, settings hub, audio panel, controls panel, startup overlay, input action generation, locomotion setup)`
- `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs`
- `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`
- `Assets/Blockiverse/Scripts/Survival/ResourceHarvestService.cs (grep)`
- `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs (grep)`
- `scripts/art/generate-art-assets.py`
- `scripts/audio/generate-audio.py (function list) and Assets/Blockiverse/Audio/ clip inventory`
- `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png (dimensions)`
- `docs/rulesets/voxel_audio_vfx_ruleset.md (§4, §15)`
- `docs/rulesets/voxel_survival_menus.md (§6.19, §6.20, §10.3, §10.4)`
