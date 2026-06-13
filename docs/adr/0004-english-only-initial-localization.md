# ADR 0004: English-Only Initial Localization Scope

## Status

Accepted

## Decision

Ship the initial Quest release scope with English interface text only. Runtime menus use the
lightweight `BlockiverseLocalization` string-key lookup with English fallback strings, but the
project will not add Unity Localization, localized string-table assets, locale detection, or a
player-facing language setting for the initial release.

## Context

The current release target prioritizes Meta Quest 3/3S controller interaction, survival/creative
loops, LAN co-op, save/load stability, and store-candidate validation. The save schema already
reserves a future `LocalSessionSave.language` field, and generated UI text now flows through
stable keys where practical, so later localization can be added without rewriting menu code.

## Consequences

- Store and submission metadata must list English as the only supported interface language.
- Tests should assert localization keys or English fallback lookup results instead of hard-coded
  literal UI strings where the text belongs to generated menus.
- Future localization work must explicitly add per-locale tables, language selection, persistence
  through the reserved save field, and Meta store metadata updates before advertising more
  languages.
