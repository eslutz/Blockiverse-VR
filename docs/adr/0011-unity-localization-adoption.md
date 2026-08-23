# ADR 0011: Unity Localization Replaces The Compiled String System

## Status

Accepted 2026-08-23. Supersedes ADR 0004 (English-only initial localization, 2026-06-12; retired — this decision replaces its compiled string-key system entirely, and English remains the only shipped locale until translation is scheduled).

## Context

Eric's direction (2026-08-22): replace the homegrown localization system properly, even at the
cost of redone work. English remains the only shipped language, but the system must be genuinely
ready to extend to any locale without another rewrite.

What the homegrown system was, concretely: a 238-entry `Dictionary<string,string>` compiled into
`Blockiverse.UI`; `Format` = positional `string.Format(InvariantCulture, …)` with no plural
affordance; and a binding model that reverse-looked-up keys from literal English strings,
first-wins over ten duplicate values — which had already silently bound all eleven "Close"
buttons in the shipped prefab to `ui.action.error.close` and left five `Keys` consts dead in the
generated UI.

## Decision

`com.unity.localization` **1.5.12** is the localization system. Its adoption was gated on a
spike whose findings are load-bearing and pinned as tests (`LocalizationSpikeEditModeTests`):

- Synchronous resolution works cold in EditMode and batchmode, **but only after
  `LocalizationSettings.InitializationOperation.WaitForCompletion()`**, and **only through the
  explicit-locale overload** — no locale-selection pass runs outside Play mode. The no-argument
  path's failure is pinned as a test, so a package version that changes this surfaces as a
  simplification signal.
- A missing table yields a diagnostic string; a missing key yields null. Entries are therefore
  resolved via `GetTable`/`GetEntry`, never by interpreting either shape.
- Positional `{0}` patterns format byte-identically to `string.Format` with Smart off; migrated
  entries stay Smart-off so legacy output is exact. Named Smart arguments are for new entries.

### Architecture

- **One String Table Collection, `UI`**, keys verbatim from the old `Keys` consts (232) plus six
  raw `ui.value.canonical.*` keys, plus post-migration additions. The dynamic `ui.value.*`
  namespaces stay unpopulated by design — `NormalizeKey`/`HumanizeIdentifier` remain the miss
  fallback, and tests pin their output.
- **`BlockiverseLocalization` is a shim**: same public surface (~200 call sites untouched),
  internals resolve overrides → table → fallback. It dies with the uGUI panels in UI-migration
  Phase 6. New UI Toolkit screens bind statically in UXML via the package's native
  `LocalizedString` binding and dynamically via `UiText` (named Smart args; quantities as
  numerics, identifiers pre-stringified invariant — port 8080 must never render "8,080").
- **The frozen snapshot** (`Tests/EditMode/Fixtures/localization-en-snapshot.json`, written by
  the migrator) is the migration-era record: the round-trip test proves snapshot ⊆ table
  byte-identically, and the reverse-lookup winner map is loaded from it rather than rebuilt,
  because a rebuild from alphabetical table enumeration would silently flip the 'Settings' and
  'Return to Title' collision winners on the next bootstrapper regeneration.
- **The 133 serialized prefab bindings are frozen** by a characterization test; the component
  re-refreshes on `SelectedLocaleChanged`, so live language switching needs no prefab change.

### Enforcement (each rule shown failing before being trusted)

Every `Keys` const resolves; every Smart entry parses; every entry carries a translator comment
— with the `Comment` class's constructor default `"Comment Text"` explicitly rejected, because
the first migration pass left all 241 comments saying exactly that and the first version of the
rule accepted it; UXML text is bound-or-allowlisted, with the allowlist a reviewed file.

### Deferred, with the seam named

- **~185 item/block display names** in `ItemRegistry`/`VoxelTypes` never entered the old system
  and do not enter this one yet. The seam is `DisplayNameForCanonicalId` →
  `ui.value.canonical.<id>` entries; they are script-generated when UI-migration Phase 4 builds
  the inventory/crafting screens whose call sites actually consume them.
- **Device validation** (Addressables content build on Quest, IL2CPP stripping) deferred by
  Eric's direction. The first device build must check logcat for Addressables errors and add
  `link.xml` if stripping bites.
- **Per-locale fonts** ride the `Fonts` asset-table slot when Zilla Slab/Barlow embed
  (`LocalizedFont` bindings are native to the package).

## Consequences

- `Assets/AddressableAssetsData/` exists and player builds gain an Addressables content-build
  step (`BuildPlayerContent()` before every `BuildPlayer`).
- `com.unity.nuget.newtonsoft-json 3.0.2` enters the dependency tree transitively.
- Translator workflow is the package's XLIFF/CSV/Google Sheets export — the reason a data table
  beats a compiled dictionary.
- The pseudo-locale (`en-XA` style expansion/accents) is the standing layout/tofu detector once
  UI Toolkit screens carry bindings; select it in a dev build and unlocalized text is instantly
  visible.
- A visible Language setting ships (Eric's call) listing English only; selection persists via
  the project settings store and carries the wiki documentation obligation.
