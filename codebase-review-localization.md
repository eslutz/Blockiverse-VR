# Codebase Review — Localization Expert

> Workflow run `wf_53b36881-009`, agent `ac024be630fb3ef14`. Raw expert output, pre-verification.

## Area Reviewed

Localization readiness review of the full Blockiverse VR codebase: package manifest, all 12 runtime assemblies (with emphasis on UI, Gameplay, VR, Survival, Voxel, Persistence), the generated XR rig prefab and Boot scene YAML, the BlockiverseProjectBootstrapper that bakes all UI, TMP font assets and settings, save/wire serialization culture-safety, and EditMode tests that pin UI strings. Overall assessment: the game has zero localization infrastructure — no Unity Localization package, no string tables, no language setting, no Application.systemLanguage usage — and every player-visible string is hardcoded English (roughly 40–60 distinct runtime strings across UI scripts, ~115 EnsureLabel/EnsureButtonControl literals baked into the generated prefab as 204 serialized m_text fields, plus ~100+ English block/item display names in the engine-free Voxel/Survival registries). The only bundled font is Latin-coverage Liberation Sans, so non-Latin text typed via the Quest system keyboard cannot render, and the world-name file sanitizer destroys non-ASCII names even in the displayed name. On the positive side, the architecture is unusually favorable for retrofitting: canonical snake_case IDs (not display names) are the persistence and wire vocabulary, failure reasons cross the network as enums and are turned into text only at the panel layer, saves use culture-invariant JsonUtility and round-trip "o" timestamps, and all scene text flows through one generator file. Effort assessment: adding localization NOW is a contained, mostly mechanical effort — introduce a string-table seam (Unity Localization package, or a lightweight engine-free table keyed by canonical id given the no-UnityEngine constraint on Voxel/Survival), route MenuActions/panel format strings/EnsureLabel through it, replace identifier-derived display text (enum ToString, snake_case ids, FailureReason names) with keyed lookups, add CJK-capable TMP fallback fonts, switch fixed-size truncating labels to auto-size/min-max, and fix the name sanitizer — roughly 2–4 engineer-weeks of infrastructure plus per-language translation/QA. Deferring is viable because the seam count is small and text is concentrated, but cost grows linearly with every new panel the bootstrapper gains, the English-string test assertions will all break at extraction time, and two of the findings (font coverage, world-name mangling) hurt non-English players today regardless of any translation plan.

## Findings (11)

### 1. No localization system exists: no package, no string tables, no language setting, no locale detection

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Packages/manifest.json`, `docs/rulesets/voxel_save_versioning_schema.md`, `docs/store-submission/store-listing.md`
- **Impact:** The game ships English-only with no seam to add a second language. The design docs already anticipate one (voxel_save_versioning_schema.md §18 reserves `language?: string` in LocalSessionSave) and the store listing has an unfilled placeholder, but nothing in code reads or stores a language. Every translated-release plan starts from zero infrastructure.
- **Evidence:** Packages/manifest.json contains no com.unity.localization (only Meta XR, XRI, Netcode, URP, etc.). Project-wide grep for `Localization|LocalizedString|LocaleIdentifier` over Assets/Blockiverse/Scripts and ProjectSettings returns zero hits; grep for `systemLanguage|CurrentCulture|SetCulture` returns zero hits. docs/rulesets/voxel_save_versioning_schema.md:876 defines `language?: string` in LocalSessionSave (unimplemented). docs/store-submission/store-listing.md:55: "Interface language(s): <English / others>".
- **Recommended fix:** Decide the localization strategy explicitly. If localizing: add com.unity.localization (or, given the engine-free assembly constraint, a small plain-C# string-table service in Core keyed by string ids with per-locale tables loaded by UI), add a language entry to the Settings screen, and persist it via the already-specified LocalSessionSave.language field. If staying English-only for 1.0, fill in the store-listing Languages section accordingly and record the decision in docs/adr/.

### 2. All player-facing UI text is hardcoded English: ~50 runtime strings across UI scripts plus 204 m_text fields baked into the generated XR rig prefab

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/MenuActions.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeHotbar.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`
- **Impact:** Localizing later requires touching every UI panel script, MenuActions, the session controller, and the 5K-line bootstrapper; there is no single extraction point. Until then the entire menu system, HUD, errors, and the controls-reference popup are English regardless of device language. This is the dominant cost driver for any future translation.
- **Evidence:** MenuActions.cs:82–146 hardcodes every menu button label ("Continue", "New World", "LAN Multiplayer", "Respawn at Bedroll", "Switch Survival/Creative", …). BlockiverseMenuController.cs:150/206–209 sets screen titles "You Died", "Paused", "Confirm?", "Settings", "OK"; line 469 builds the delete prompt $"Delete \"{worldName}\"? This cannot be undone.". BlockiverseWorldSessionController.cs:291,445,479–555,600,648 emits user-facing errors ("Failed to create the world.", "Save not found.", "Enter a world name first.", …). BlockiverseMultiplayerSessionMenu.cs:82–276 contains ~20 connection status sentences. BlockiverseCreativeToolsPanel.cs:123–394 contains ~25 status strings. SurvivalHealthPanel.cs:135–137 returns "Down"/"Critical"/"Stable". Bootstrapper makes 115 EnsureLabel/EnsureButtonControl calls with English literals (e.g. lines 2652 "Survive, craft, and shape the world.", 2722 "Controller Map", 4164 "New World", 4185 row labels, 4382–4392 ControllerMappingText), producing 204 m_text fields in BlockiverseXRRig.prefab (verified by grep; values include "Load World", "World Details", "Reduced Flash", "Meadow Turf").
- **Recommended fix:** Introduce a string-id-based lookup before the UI surface grows further: give every literal a key (e.g. menu.title.continue, error.save_not_found), move English text into a default table, and have MenuActions, the panels, and EnsureLabel resolve keys through it. Because scenes/prefabs are bootstrapper-generated, baked text can instead become a runtime localizer component (set text from key on Awake), keeping the bootstrapper writing keys, not copy.

### 3. Only Latin-coverage Liberation Sans fonts are bundled; CJK/Arabic/Thai/Indic text typed via the Quest system keyboard cannot render

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/TextMesh Pro/Resources/TMP Settings.asset`, `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset`, `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset`, `Assets/TextMesh Pro/Fonts/LiberationSans.ttf`, `Assets/Blockiverse/Scripts/VR/BlockiverseSystemKeyboardField.cs`
- **Impact:** A player whose Quest is set to Japanese/Korean/Chinese/Arabic etc. can open the native system keyboard (BlockiverseSystemKeyboardField) and type a world name or seed in their script; TMP has no glyphs for it, so the input field, Load World rows, World Details, and the rename field show missing-glyph boxes. It also means no future localized UI can ship for those languages without new font assets, and RTL scripts additionally need shaping TMP does not provide natively.
- **Evidence:** The only font assets in the project are the stock TMP essentials: LiberationSans SDF.asset has m_AtlasPopulationMode: 0 (static), m_SourceFontFile: {fileID: 0}, and exactly 250 m_Unicode entries (ASCII + Latin-1). Its m_FallbackFontAssetTable (line 7715) points only to LiberationSans SDF - Fallback.asset, which is dynamic (m_AtlasPopulationMode: 1) but sources the same LiberationSans.ttf — Liberation Sans covers Latin/Greek/Cyrillic only, no CJK/Arabic/Thai/Devanagari. TMP Settings.asset has m_fallbackFontAssets: [] (no global fallback) and m_ClearDynamicDataOnBuild: 1. BlockiverseSystemKeyboardField.cs:51 opens TouchScreenKeyboard.Open(..., TouchScreenKeyboardType.Default) and streams keyboard.text straight into TMP_InputField (lines 59–71), so any script the OS keyboard emits reaches TMP.
- **Recommended fix:** Add a CJK-capable fallback (e.g. Noto Sans CJK subsets or Noto Sans dynamic font assets) to TMP Settings' global fallback list so player-typed names render even pre-localization; budget atlas memory for Quest (use dynamic atlases with multi-atlas enabled). Alternatively, until fonts are added, restrict the world-name input's content to the supported character set and message it, rather than rendering boxes. Verify keyboard behavior per-language on device.

### 4. World-name sanitizer flattens every non-ASCII character to underscore in the player-visible name, not just the directory name

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- **Impact:** Any world name containing characters outside [a-zA-Z0-9 _-.()] is silently corrupted in the UI and the save manifest: "Café" becomes "Caf_", a Japanese or Cyrillic name becomes a row of underscores, and multiple such worlds collide into "___", "___ (2)", "___ (3)". This affects non-English players today (the Quest system keyboard lets them type these names) and destroys user-entered data with no warning.
- **Evidence:** BlockiverseWorldSessionController.cs:828–848: SanitizeFileName replaces any char failing IsSafeFileNameChar (ASCII letters/digits/space/_/-/./()) with '_'. AllocateSavePath (lines 804–818) builds candidateName from the sanitized baseName and returns it as the worldName. CreateNewWorld line 278 then assigns `(currentSavePath, currentWorldName) = AllocateSavePath(config.Name.Trim())` — so the sanitized string becomes the manifest WorldName shown in Load World, World Details, and rename. The comment at 824–827 explains the sanitizer exists because Path.GetInvalidFileNameChars() is empty on Android, but the allowlist is far stricter than filesystem safety requires.
- **Recommended fix:** Decouple display name from directory name: store the player's original (trimmed) name in the manifest and UI, and derive the directory from the sanitized name plus a uniqueness suffix or short hash (the manifest already carries the authoritative name; SummaryKey already keys on name+seed+createdUtc). At minimum, widen the allowlist to permit all letters/digits via char.IsLetterOrDigit (which is Unicode-aware) while still excluding path separators and reserved characters.

### 5. Raw canonical IDs and C# enum identifiers are rendered directly as UI text (untranslatable by construction, and rough even in English)

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseNewWorldPanel.cs`, `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseCatalogBrowserPanel.cs`, `Assets/Blockiverse/Scripts/Survival/CraftingRecipe.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldDetailsPanel.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs`
- **Impact:** Players see machine identifiers: the New World screen displays "survival", "survival_terrain", "flat_builder", "pinewild" as the selector values; the creative catalog category label shows enum names like "DeepRock"; station names are derived by splitting PascalCase enum identifiers; and station/crafting failures render enum member names ("Cannot deposit: OutputBlocked", "Cannot craft …: MissingIngredient"). None of these can be localized without changing the mechanism, because the displayed text IS the identifier.
- **Evidence:** BlockiverseNewWorldPanel.cs:40–47/128: ValueGetters return Config.GameMode/WorldPreset/StartingBiome (canonical strings from NewWorldConfig.cs:12–19, e.g. "survival_terrain") straight into cycleValueLabels[idx].text. BlockiverseCatalogBrowserPanel.cs:172: categoryLabel.text = Categories[categoryIndex].ToString() (CreativeCatalogCategory members include DeepRock). CraftingRecipe.cs:21–33: CraftingStationNames.DisplayName builds UI text by inserting spaces into the enum identifier ("ClayKiln" → "Clay Kiln"). BlockiverseStationPanel.cs:170/184: $"Cannot deposit: {result.FailureReason}" prints enum member names. SurvivalCraftingPanel.cs:142/164 same pattern with CraftingFailureReason. BlockiverseWorldDetailsPanel.cs:48–49 Capitalize(save.GameMode) re-cases the canonical id for display. BlockiverseCreativeToolsPanel.cs:123 shows the WeatherState enum raw.
- **Recommended fix:** Add explicit display-name mappings for every identifier that reaches a label: a per-enum switch or dictionary returning a string-table key (failure reasons, stations, categories, weather states, game mode/difficulty/world size/preset/biome options). This is also an immediate English polish win — "Cannot deposit: output blocked" and "Survival Terrain" instead of identifier text.

### 6. Pervasive English sentence-fragment composition: concatenated/interpolated UI strings with fixed word order and no pluralization support

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseLoadWorldPanel.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldDetailsPanel.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs`
- **Impact:** Even after string extraction, these patterns break under translation: word order is fixed by code-side concatenation, counts use the English "x6" suffix convention, and label:value pairs are fused into single format strings. Languages with different word order, declension, or plural rules (Russian, Polish, German, Japanese counters) cannot be expressed; translators never see complete sentences.
- **Evidence:** SurvivalCraftingPanel.cs:309–328: FormatRecipe = $"{FormatStack(recipe.Output)} - {FormatIngredients(recipe)}" plus appended " [needs {station}]"; FormatStack = $"{definition.Name} x{stack.Count}". SurvivalInventoryPanel.cs:94/108/158: $"x{stack.Count}", $"Hotbar {n} / {total}", "{Name} x{Count}". SurvivalCratePanel.cs:74/100: $"Deposited {FormatStack(held)}" / $"Withdrew {FormatStack(stack)}". SurvivalHealthPanel.cs:105: $"{baseState} · Hunger {hunger} · Thirst {thirst} · Stamina {stamina}". BlockiverseLoadWorldPanel.cs:86: $"{Name}  ·  Day {DayCount}". BlockiverseWorldDetailsPanel.cs:51–54 fuses three lines of "Label: value" pairs into one format string. BlockiverseMultiplayerSessionMenu.cs:260/270 appends sentences: $"{reconnectMessage} Last disconnect: {reason}".
- **Recommended fix:** When extracting strings, convert every composed message into a single parameterized template per message (e.g. craft.success = "Crafted {item} ×{count}") so translators control word order, and adopt a plural-aware formatter (Unity Localization Smart Strings or ICU-style select/plural) for count-bearing messages. Keep FormatStack-style helpers but have them resolve through templates instead of string interpolation.

### 7. Fixed-size world-space panels with Truncate overflow and no auto-sizing — zero tolerance for translated-text expansion

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** Every generated label has a hard pixel size and silently truncates overflowing text; buttons are fixed 220×54 (down to 150×48 / 164×54) with 26pt labels. German/French/Russian translations typically run 20–35% longer than English, so localized labels like "Switch Survival/Creative" or "Respawn at World Spawn" would clip with no visual indication. Layout is fully code-defined in the bootstrapper, so fixing it later means revisiting all 115 call sites.
- **Evidence:** EnsureLabel (lines 3573–3604): labelRect.sizeDelta = size (fixed), tmp.fontSize = fontSize (fixed), tmp.overflowMode = TextOverflowModes.Truncate, no enableAutoSizing. EnsureButtonControl (3425–3481): fixed size with default 220×54 and label insets of 8/4 px; call sites use even smaller fixed sizes (3987–3991: Host/Join/Stop at 164×54; 4521–4525: 150×48). No LayoutGroup/ContentSizeFitter usage for these panels.
- **Recommended fix:** When (or before) localizing, enable TMP auto-sizing with sane min/max (e.g. fontSizeMin 18) on button and row labels in EnsureLabel/EnsureButtonControl — a two-line change that propagates to every generated panel on the next bootstrap run — and spot-check the worst offenders (pause menu, controls popup) with pseudo-localized 30%-longer strings.

### 8. Block/item display names are constructor literals inside engine-free assemblies, and ItemRegistry keys a lookup on the English display name

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs`, `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `Assets/Blockiverse/Scripts/Survival/ItemDefinition.cs`
- **Impact:** The ~100+ English names ("Meadow Turf", "Fired Brick Block", …) live in Voxel and Survival, which by project invariant cannot reference UnityEngine — and therefore cannot use the Unity Localization package directly. Worse, ItemRegistry maintains definitionsByName keyed by the display name, so naively replacing Name with translated text would change registry semantics (duplicate detection) and any future name-based lookups. Localization must instead happen at the UI layer keyed by canonical id, which no current code anticipates.
- **Evidence:** VoxelTypes.cs:254 etc.: registry.Register(new BlockDefinition(MeadowTurf, "meadow_turf", "Meadow Turf", …)) — English name as the third positional literal for every block. ItemRegistry.cs:30–69: every item registered with an English name literal; ItemRegistry.cs:11/227/234: readonly Dictionary<string, ItemDefinition> definitionsByName = new(StringComparer.OrdinalIgnoreCase) keyed by definition.Name. UI reads definition.Name directly for display (SurvivalCraftingPanel.cs:327, BlockiverseCatalogBrowserPanel.cs:167, CreativeHotbar.cs:165) and BlockiverseCatalogBrowserPanel.cs:187 string-matches search input against Name with OrdinalIgnoreCase (which does not case-fold most non-ASCII).
- **Recommended fix:** Treat in-registry Name as the English source/development name and add a UI-side display-name resolver keyed by CanonicalId/ItemId (a plain dictionary loaded from the string table works within the engine-free constraint if placed behind an interface). Switch catalog search to compare against the resolved display name with CultureInfo-aware IndexOf/CompareInfo once names can be non-ASCII. Keep definitionsByName keyed on the immutable English name or drop it in favor of canonical-id keys.

### 9. Registry-hash ID ordering uses the culture-sensitive default string comparer

- **Severity:** Low  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- **Impact:** ComputeBlockRegistryHash/ComputeItemRegistryHash sort canonical IDs with LINQ OrderBy's default comparer, which is culture-sensitive. If collation ever orders two IDs differently across device locales (locale-tailored collation of sequences, or Mono/ICU behavior differences on Quest vs editor), the concatenated string and its MD5 differ, and WorldSaveService.cs:391–393 would reject a perfectly valid save with a registry-mismatch failure. The current all-lowercase-ASCII-with-underscores ID set makes divergence unlikely today, but the comparer is the wrong tool for a stable wire/persistence fingerprint.
- **Evidence:** WorldSaveService.cs:989: string.Join("|", registry.All.Select(d => d.CanonicalId).OrderBy(id => id)); line 995: same for item IDs. OrderBy with no comparer uses Comparer<string>.Default (culture-sensitive CompareTo), unlike the deliberate StringComparer.OrdinalIgnoreCase used elsewhere (e.g. SaveListModel.cs:123). The hashes are persisted in the manifest and compared on load (lines 391–393).
- **Recommended fix:** Pass StringComparer.Ordinal to both OrderBy calls. Existing saves keep matching as long as ordinal order equals the order the current locale produced, which holds for the present ID set — verify by comparing the ordinal-sorted join against the current output before shipping.

### 10. Dates and numbers are displayed without regional formatting (invariant dates, English decimal conventions); no current parsing risk

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldDetailsPanel.cs`, `Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs`, `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- **Impact:** World Details shows Created/Last Played as invariant "yyyy-MM-dd" regardless of the player's region — safe but not locale-appropriate (US users expect MM/DD/YYYY, Germans DD.MM.YYYY). The dev performance overlay formats numbers with the current culture including :n0 group separators, fine for a dev tool. Importantly, the inverse problem is absent: no float.Parse/ToString crosses the save or wire boundary as locale-formatted text, so there is no German-locale decimal-comma corruption path. The seed field's ulong.TryParse (NewWorldConfig.cs:86) uses current culture implicitly, but integer digit parsing is locale-stable in practice; non-numeric input deterministically falls through to the FNV-1a hash.
- **Evidence:** BlockiverseWorldDetailsPanel.cs:61: utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) for display. PerformanceStatsOverlay.cs:86–89: $"…{Sampler.AverageFps:0.0}…{stats.TriangleCount:n0}…" with current culture. NewWorldConfig.cs:86: ulong.TryParse(trimmed, out ulong numeric) without NumberStyles/IFormatProvider. Counter-evidence of safety: WorldSaveService.cs:238/911 uses DateTime.UtcNow.ToString("o") (round-trip, invariant); all JSON goes through UnityEngine.JsonUtility (lines 374, 1190–1192) which is culture-invariant; BlockiverseWorldSessionController.cs:791–800 parses timestamps with CultureInfo.InvariantCulture; project-wide grep finds no float.Parse/double.Parse anywhere.
- **Recommended fix:** For localized display, format save dates with the user's culture (e.g. utc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) while keeping invariant "o" for storage — the split is already correctly in place. Optionally pass NumberStyles.None + CultureInfo.InvariantCulture to the seed TryParse to make cross-device seed-text determinism explicit.

### 11. EditMode tests pin exact English UI strings, coupling the test suite to the unlocalized copy

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Tests/EditMode/ActionMenuEditModeTests.cs`, `Assets/Blockiverse/Tests/EditMode/SurvivalUiEditModeTests.cs`
- **Impact:** Any string extraction or copy change breaks these assertions, and they institutionalize English literals as the contract. When localization lands, these tests must be rewritten to assert string keys or table lookups; budgeting for that avoids surprise churn during extraction.
- **Evidence:** ActionMenuEditModeTests.cs:35–39 asserts title.text == "Paused" and labels equal "Resume", "Switch Survival/Creative", "Creative Tools", "Return to Title"; line 112–114 asserts confirm labels "Delete"/"Keep". SurvivalUiEditModeTests.cs:84/106 asserts statusLabel.text == "Crafted Work Plank x6".
- **Recommended fix:** When introducing the string table, change these assertions to compare against the table lookup for the expected key (or assert the key resolution path) rather than literal English, so tests survive translation and copy edits.

## What Looks Good (6)

- Canonical snake_case string IDs are cleanly separated from display names at the data layer: BlockDefinition carries both canonicalId ("meadow_turf") and Name ("Meadow Turf") as distinct fields (Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs:95–121, 254), and saves/wire use only the canonical IDs — exactly the foundation a display-name localization layer needs.
- Failure reasons cross the host/client boundary as enums (CraftingFailureReason, BlockHarvestFailureReason, RepairFailureReason in Assets/Blockiverse/Scripts/Survival/CraftingService.cs, ResourceHarvestService.cs, MendBenchRepair.cs) and binary Netcode payloads, not pre-rendered English sentences — so the host's language can never leak into a client's UI, and message text can be localized client-side without protocol changes.
- Save serialization is culture-safe by construction: UnityEngine.JsonUtility (culture-invariant) for all save JSON, DateTime round-trip "o" timestamps on write (WorldSaveService.cs:238, 911), and CultureInfo.InvariantCulture + DateTimeStyles on parse (BlockiverseWorldSessionController.cs:791–800). No float.Parse/ToString of gameplay numbers crosses any persistence or wire boundary as text.
- Menu architecture separates action ids from labels (MenuActions screen/action id constants vs. MenuAction label strings, Assets/Blockiverse/Scripts/UI/MenuActions.cs), and save-list sorting/filtering deliberately uses StringComparer.OrdinalIgnoreCase (Assets/Blockiverse/Scripts/UI/SaveListModel.cs:123–125) — string extraction would be mechanical, not structural.
- All UI text flows through one generator (BlockiverseProjectBootstrapper EnsureLabel/EnsureButtonControl) and a small set of panel scripts, with legacy UnityEngine.UI.Text actively migrated to TMP (bootstrapper lines 3594–3597); a localization seam added at those two choke points covers nearly the whole game.
- The native system keyboard path (Assets/Blockiverse/Scripts/VR/BlockiverseSystemKeyboardField.cs) uses TouchScreenKeyboard with proper commit/cancel semantics, so locale-appropriate OS keyboards and IMEs already work at the input layer — only rendering (fonts) and storage (name sanitizer) block non-Latin input today.

## Could Not Review (5)

- On-device behavior: how the Quest system keyboard presents per-language IMEs and exactly how TMP renders missing glyphs on hardware (boxes vs. invisible) — static review only; the font-coverage facts are verified but the visual outcome needs a device test.
- Mono/IL2CPP collation behavior on Quest for the default string comparer in the registry-hash OrderBy — the divergence risk across device locales cannot be conclusively proven or excluded statically.
- The companion wiki (../Blockiverse-VR.wiki) and website (../Blockiverse-VR.website) repos referenced by CLAUDE.md — outside this repository checkout; their language/store-metadata state was not inspected beyond docs/store-submission/store-listing.md in this repo.
- Meta XR SDK / Meta Avatars internal UI surfaces (permission dialogs, avatar editor hand-offs) — third-party package behavior; whether they follow device language is not determinable from this codebase.
- Audio: scripts/audio/generate-audio.py output was assumed to be non-speech SFX/music (no VO assets found); if any future audio contains speech, localized asset needs were not assessed.

## Inspected (47)

- `Packages/manifest.json`
- `Assets/Blockiverse/Scripts/UI/MenuActions.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseNewWorldPanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseLoadWorldPanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseWorldDetailsPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SaveListModel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseCatalogBrowserPanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseSystemKeyboardField.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseSettingsPersistence.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeHotbar.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeCatalog.cs`
- `Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs (string/format sweep)`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs (string/format sweep)`
- `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`
- `Assets/Blockiverse/Scripts/Survival/ItemDefinition.cs`
- `Assets/Blockiverse/Scripts/Survival/CraftingRecipe.cs`
- `Assets/Blockiverse/Scripts/Survival/CraftingService.cs`
- `Assets/Blockiverse/Scripts/Survival/ResourceHarvestService.cs`
- `Assets/Blockiverse/Scripts/Survival/MendBenchRepair.cs`
- `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs`
- `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs (UI text, EnsureLabel, EnsureButtonControl, TMP setup, ControllerMappingText)`
- `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab (204 m_text fields)`
- `Assets/Blockiverse/Scenes/Boot.unity (m_text scan)`
- `Assets/TextMesh Pro/Resources/TMP Settings.asset`
- `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset`
- `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset`
- `Assets/TextMesh Pro/Fonts/LiberationSans.ttf (presence/coverage)`
- `Assets/Blockiverse/Tests/EditMode/ActionMenuEditModeTests.cs`
- `Assets/Blockiverse/Tests/EditMode/SurvivalUiEditModeTests.cs`
- `ProjectSettings/ProjectSettings.asset (productName)`
- `docs/store-submission/store-listing.md`
- `docs/rulesets/voxel_save_versioning_schema.md (§18 LocalSessionSave)`
