# Store Candidate VRC Checklist

This checklist tracks in-repo readiness for Meta Quest review. It does not replace Meta's current VRC documentation or the Developer Dashboard validation process.

| Area | In-repo check | Evidence source | Status |
| --- | --- | --- | --- |
| Packaging | Android package id is `dev.ericslutz.blockiversevr`; app name is `Blockiverse VR`; manifest declares VR launch category. | `ProjectSettings/ProjectSettings.asset`, `Assets/Plugins/Android/AndroidManifest.xml`, Android branding library | Ready for candidate validation |
| Supported devices | Manifest declares Quest 3 and Quest 3S support. | `com.oculus.supportedDevices` manifest metadata | Ready for candidate validation |
| Permissions | No unnecessary force internet or SD card permission is enabled in Unity settings. | `ForceInternetPermission: 0`, `ForceSDCardPermission: 0` | Ready for candidate validation |
| Signing | Release signing material is not committed. | `scripts/ci/forbidden-files.sh` | In-repo ready; external signing remains out of scope |
| Performance | Candidate must be measured with OVR Metrics and should satisfy Meta's current rendering-rate VRC; internal target remains stable 72 FPS or better on Quest 3/3S. | `docs/testing/performance/README.md`, future OVR Metrics captures | Pending device validation |
| Comfort | Teleport and snap-turn comfort flows remain available for Quest controller play. | Unity tests, Quest smoke test notes | Pending final smoke validation |
| Input | Quest Touch controller input is the supported input path. | Input action assets, VR rig tests, manual Quest smoke | Pending final smoke validation |
| Privacy | Privacy policy draft and data-use inventory exist. | `privacy-policy.md`, `data-use.md` | Draft ready; public HTTPS URL still external |
| Content | Original assets and no protected third-party identity. | Art direction docs, prompt log, committed assets | Ready for candidate validation |
| Multiplayer claims | Store text says LAN co-op only and does not imply public matchmaking or cloud worlds. | `metadata.md`, `data-use.md` | Ready for candidate validation |
| Voice claims | Store text says no in-app voice and references Meta Quest party chat as external. | `metadata.md`, `privacy-policy.md` | Ready for candidate validation |
| Store assets | Screenshot/capture plan exists. | `screenshots.md` | Planned; final captures pending |
| Release notes | Draft release notes exist. | `release-notes.md` | Draft ready |
| Known issues | Known-issues draft exists. | `known-issues.md` | Draft ready |

## External Steps Not Performed

- Create or modify the Meta Developer Dashboard app.
- Upload builds or assets.
- Create release channels.
- Invite private testers.
- Configure production signing secrets.
- Submit for review.
