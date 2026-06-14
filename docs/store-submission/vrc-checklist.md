# Meta Quest VRC Checklist (Working Copy)

> Virtual Reality Check (VRC) tracking for store submission. This is an internal working copy —
> always reconcile against the current official Meta VRC guidelines, which take precedence and
> change over time. Mark each item Pass / Fail / N/A with evidence (build, report, or capture).

## Functional

- [ ] App launches to an interactive state without crashing.
- [ ] New worlds and menus use canonical ruleset IDs and labels rather than temporary validation vocabulary.
- [ ] App recovers correctly from headset sleep / proximity-sensor undock.
- [ ] App handles controller disconnect/reconnect gracefully.
- [ ] App exits cleanly and does not hang on quit.
- [ ] No placeholder/debug-only UI is visible in the release build (e.g. the performance
      overlay is disabled by default in release).

## Performance

- [ ] Sustains the target frame rate (>= 72 FPS) on Quest 3 and Quest 3S.
      Evidence: `docs/testing/performance/report-YYYY-MM-DD.md`.
- [ ] No extended hitches during normal chunk rebuilds or world generation.
- [ ] Stable performance in a two-player LAN session.

## Comfort

- [ ] Comfort rating in the listing matches in-app behavior.
- [ ] Teleport + snap turn available; smooth options are opt-in.
- [ ] Height reset / recenter available.

## Tracking & input

- [ ] Uses standard Quest Touch controller bindings.
- [ ] Menus open headset-relative and within reach.
- [ ] Haptics fire on key interactions (block break/place).

## Audio

- [ ] Audio plays on supported output without clipping.
- [ ] No copyrighted or third-party audio (all cues original/synthesized — see
      `Assets/Blockiverse/Audio`).

## Content & legal

- [ ] All art, characters, and audio are original (no third-party IP).
- [ ] Privacy policy published and linked (see `privacy-policy.md`).
- [ ] Data-use declarations match actual behavior (see `data-and-safety.md`).
- [ ] Age rating questionnaire (IARC) completed.

## Store presence

- [ ] Store listing copy finalized (see `store-listing.md`).
- [ ] Required artwork and screenshots uploaded.
- [ ] Support contact provided.

## Release Packaging

- [ ] Signed Beta APK built from `main` and uploaded to Meta `beta` using
      `.github/workflows/meta-release.yml` after production signing secrets are configured.
- [ ] RC release promotes the selected Beta GitHub release's Meta build ID to Meta `rc`
      using `.github/workflows/meta-release.yml`.
- [ ] Production release promotes the selected RC GitHub release's Meta build ID to Meta
      `store` using `.github/workflows/meta-release.yml` only after Store submission/review approval.
