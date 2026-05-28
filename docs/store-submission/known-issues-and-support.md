# Known Issues & Support

> Maintained list of shipping limitations and the support channel disclosed in the store
> listing. Update before each release.

## Support

- **Support contact:** <support email or site>
- **Response expectation:** <e.g. best-effort within N business days>
- **Bug reports:** Include device model (Quest 3 / 3S), app version, and steps to reproduce.

## Known limitations (current build)

- Multiplayer is **local LAN only**; there is no cloud-hosted/online matchmaking yet
  (cloud private worlds are a post-release roadmap item, M8).
- Worlds are bounded (fixed dimensions), not infinite/streaming terrain.
- Voice communication uses Meta Quest party chat; there is no in-app voice.
- Placeholder interaction/UI sounds are synthesized originals pending authored sound design.
  Generate them into `Assets/Blockiverse/Audio` (Git LFS) by running
  `python3 scripts/audio/generate-m6-audio.py`, then assign the clips on the audio prefab.

## Resolved / not-an-issue

- <move items here as they are fixed, with the version that fixed them>

## Release notes

Per-release player-facing notes are drafted from `release-notes-template.md` and the
`Unreleased` section of the top-level `CHANGELOG.md`.
