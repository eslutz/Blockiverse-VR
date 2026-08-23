# Known Issues & Support

> Maintained list of shipping limitations and the support channel disclosed in the store
> listing. Update before each release.

## Support

- **Support contact:** <support email or site>
- **Response expectation:** <e.g. best-effort within N business days>
- **Bug reports:** Include device model (Quest 3 / 3S), app version, and steps to reproduce.

## Known limitations (current build)

- Multiplayer is **local LAN only**; there is no cloud-hosted/online matchmaking yet
  (cloud private worlds are tracked in the execution plan's future-features section).
- Worlds are bounded (fixed dimensions), not infinite/streaming terrain.
- Voice communication uses Meta Quest party chat; there is no in-app voice.
- Gameplay, save, environment, vegetation, structures, multiplayer, and feedback behavior
  should match the canonical rulesets under `../rulesets/`.
- Sound effects in `Assets/Blockiverse/Audio` (Git LFS) are built from licensed third-party
  recordings by `python3 scripts/audio/build-audio-assets.py`; source and license for every
  cue are in `../audio/audio-asset-manifest.md`. The music bed and the classic block cues
  are original and regenerate with `python3 scripts/audio/generate-audio.py`.
- Quest headset acceptance for audio timing/output is still pending on the linked audio
  stories; do not mark the audio stories Done until device evidence is recorded.

## Resolved / not-an-issue

- <move items here as they are fixed, with the version that fixed them>

## Release notes

Per-release player-facing notes are drafted from `release-notes-template.md` and the
commit history since the previous release tag.
