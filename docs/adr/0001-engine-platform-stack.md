# ADR 0001: Engine And Platform Stack

## Status

Accepted; amended 2026-08-23 to add UI Toolkit and the dependency preference order.

## Decision

Use Unity 6 Personal, C#, URP, OpenXR, Unity OpenXR: Meta, Meta XR Core SDK, Unity Input System, Unity UI Toolkit for runtime UI (adopted by [ADR 0010](0010-ui-toolkit-runtime-ui.md), which governs the migration from uGUI), and Netcode for GameObjects.

### Dependency preference order

When a capability is needed, prefer sources in this order:

1. **Native Unity APIs** — engine features and Unity-maintained packages.
2. **Native Meta APIs** — Meta XR SDKs and platform services, for Quest capabilities Unity does not provide.
3. **Well-regarded, actively maintained third-party libraries and packages** — only when neither first-party source covers the need, and subject to the existing policy of verifying current stable versions from official upstream sources before adoption.
4. **Our own custom code** — the last resort, when nothing above fits; and the inverse applies when first-party support arrives for something we hand-rolled (as with the compiled localization tables replaced by `com.unity.localization` in [ADR 0011](0011-unity-localization-adoption.md)).

A lower tier is justified only by a concrete gap in the tiers above it — missing capability, a measured platform defect, or an incompatibility — and the justification belongs in the ruleset or ADR that adopts it.

## Context

The project targets Meta Quest 3 and Quest 3S, uses C#, and needs mature XR, Android, testing, profiling, and CI/CD support.

The preference order exists because this repository is maintained by one owner with agent assistance: first-party dependencies track engine and platform upgrades on someone else's payroll, while every hand-rolled system is a permanent local maintenance cost. The project has already paid that cost once — the custom localization table system was built and then replaced wholesale by Unity's own package.

## Consequences

Unity becomes the primary build and test environment. Core gameplay logic should remain in pure C# assemblies where practical so voxel storage, generation, commands, inventory, crafting, save/load, and networking validation can be tested without VR hardware.

New dependencies and new custom subsystems should name which preference tier they sit in and why the tiers above did not fit.
