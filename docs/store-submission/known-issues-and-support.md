# Known Issues & Support

> Maintained list of shipping limitations and the support channel disclosed in the store
> listing. Update before each release.

## Support

- **Support contact:** <support email or site>
- **Response expectation:** <e.g. best-effort within N business days>
- **Bug reports:** Include device model (Quest 3 / 3S), app version, and steps to reproduce.

## Known limitations (current build)

- Multiplayer is **local LAN only**; there is no cloud-hosted/online matchmaking yet
  (cloud private worlds are a later roadmap item).
- Worlds are bounded (fixed dimensions), not infinite/streaming terrain.
- Voice communication uses Meta Quest party chat; there is no in-app voice.
- Gameplay, save, environment, vegetation, structures, multiplayer, and feedback behavior
  should match the canonical rulesets under `../rulesets/`.
- Interaction, movement, inventory, and crafting sounds are generated original cues in
  `Assets/Blockiverse/Audio` (Git LFS). Regenerate the current cue set by running
  `python3 scripts/audio/generate-audio.py`.
- Quest headset acceptance for audio timing/output is still pending on the linked audio
  stories; do not mark the audio stories Done until device evidence is recorded.

## Resolved / not-an-issue

- <move items here as they are fixed, with the version that fixed them>

## Release notes

Per-release player-facing notes are drafted from `release-notes-template.md` and the
`Unreleased` section of the top-level `CHANGELOG.md`.
