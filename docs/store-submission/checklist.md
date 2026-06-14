# Store Submission Checklist

Working documents for each item live in this folder. Items that require real hardware,
a Meta account, or design assets are marked **(external)** and tracked as follow-ups.
No Meta Developer Dashboard action, release-channel upload, tester invite, Data Use Checkup
submission, or Submit for Review action is performed by this repository checklist.

- App metadata — `store-listing.md`
- Short description — `store-listing.md`
- Long description — `store-listing.md`
- Screenshots — `screenshots.md`; final assets are **(external)** capture on Quest 3
- Trailer or capture, if available — **(external)**
- Comfort rating notes — `store-listing.md`
- Privacy policy — `https://blockiversevr.com/privacy/` (`privacy-policy.md` is the local pointer)
- Data usage declarations — `data-and-safety.md`
- Age and child-safety review — `data-and-safety.md` + IARC questionnaire **(external)**
- User Age Group API implementation evidence - `../roadmap/meta_user_age_group_api_implementation_plan.md`,
  `../../Assets/Blockiverse/Scripts/MetaPlatform/`, and `../../Assets/Blockiverse/Tests/EditMode/MetaPlatform/`;
  dashboard self-certification, Data Use Checkup submission, and Quest account validation remain **(external)**
- VRC checklist — `vrc-checklist.md`
- Performance evidence — `../testing/performance/` (report per release) **(external capture)**
- Content checklist — `vrc-checklist.md`
- Store artwork — **(external)** icon, cover, hero image
- Support email or site — `known-issues-and-support.md`
- Known issues — `known-issues-and-support.md`
- Release notes — `release-notes-template.md`
- Ruleset/design consistency — `../rulesets/` and `../roadmap/blockiverse_vr_execution_plan.md`
- Signed release APK from `main` — `.github/workflows/beta-release.yml` builds and uploads
  the signed Beta APK; `.github/workflows/release-candidate.yml` promotes the selected Beta
  Meta build to RC, and `.github/workflows/production-release.yml` promotes the selected RC
  Meta build to `store` only after Store review is approved
