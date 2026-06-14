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
- Privacy policy — `privacy-policy.md` (publish at a public URL)
- Data usage declarations — `data-and-safety.md`
- Age and child-safety review — `data-and-safety.md` + IARC questionnaire **(external)**
- VRC checklist — `vrc-checklist.md`
- Performance evidence — `../testing/performance/` (report per release) **(external capture)**
- Content checklist — `vrc-checklist.md`
- Store artwork — **(external)** icon, cover, hero image
- Support email or site — `known-issues-and-support.md`
- Known issues — `known-issues-and-support.md`
- Release notes — `release-notes-template.md`
- Ruleset/design consistency — `../rulesets/` and `../roadmap/blockiverse_vr_execution_plan.md`
- Signed release APK from `main` — `.github/workflows/meta-release.yml` builds and uploads
  the signed Beta APK; the same workflow promotes a selected Beta GitHub release to RC
  and promotes a selected RC GitHub release to `store` only after the `meta-production`
  environment approval gate is approved
