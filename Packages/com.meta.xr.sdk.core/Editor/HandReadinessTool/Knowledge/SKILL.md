---
name: hand-readiness
description: Audits a Unity project for hand-readiness — finds controller-based interactions that need to migrate to hand tracking, suggests ISDK replacements, and produces a prioritized migration plan. Powers the Hand Readiness Tool (Editor) and runs anywhere the user asks about hand-readiness, controller-to-hands conversion, OVRInput replacement, ISDK adoption, or preparing a VR project for hands-only input.
---

# Hand Readiness

## Context

The target device is a hands-only Meta VR headset where hand tracking is the primary input method, not a fallback. Most existing Quest projects were built controller-first, so the work isn't just "replace button presses with gestures" — it's rethinking interaction design so hands feel natural instead of like a worse controller. A systematic scan matters: find every controller dependency, understand the gameplay intent behind it, and map it to the right ISDK pattern.

## Analysis Workflow

### Step 1: Scan the Project

Build a complete picture of how the project handles input today. Controller dependencies hide in surprising places — not just obvious `OVRInput.Get()` calls, but also XR Interaction Toolkit components, Unity's legacy Input system, and custom grab systems. Cast a wide net:

```bash
# Find controller input usage
grep -r "OVRInput\.\(Get\|GetDown\|GetUp\)" --include="*.cs" .
grep -r "OVRInput\.Button\|OVRInput\.Axis" --include="*.cs" .

# Find XR Interaction Toolkit usage
grep -r "XRGrabInteractable\|XRRayInteractor\|XRDirectInteractor" --include="*.cs" .

# Find legacy Input system usage
grep -r "Input\.GetButton\|Input\.GetAxis\|Input\.GetKey" --include="*.cs" .

# Find interaction patterns
grep -r "Grabbable\|IGrabbable\|OnGrab\|OnRelease" --include="*.cs" .
grep -r "Raycast\|RaycastHit\|Physics\.Raycast" --include="*.cs" .
```

### Step 2: Categorize Findings

For each finding, categorize it:

| Category | What to Look For | Impact |
|----------|------------------|--------|
| **Controller Input** | `OVRInput.Get()`, button/axis references | Must replace with hand gestures or ISDK components |
| **Grab Systems** | Trigger-based grab, distance grab | Convert to `HandGrabInteractable` with pinch detection |
| **UI Interaction** | Ray-based UI, pointer clicks | Convert to poke interactions (`PokeInteractable`) |
| **Movement** | Thumbstick locomotion, snap turn | Redesign for hand-based or gaze-based navigation |
| **Object Manipulation** | Thumbstick rotation, button-based scaling | Use direct hand rotation/scaling with two-hand support |

### Step 3: Suggest Adaptations

For each controller-dependent system, suggest a specific ISDK-based replacement. Reference the hand-tracking patterns in `references/hand-tracking-patterns.md` for implementation details.

### Step 4: Prioritize

Rank suggestions by impact and effort:

- **High Priority** — core gameplay interactions that use controller APIs and must be replaced (e.g. OVRInput-based grab, trigger-based shooting).
- **Medium Priority** — secondary interactions on controller APIs (e.g. thumbstick scrolling, ray-based UI navigation).
- **Low Priority** — enhancements for code that already uses hand tracking but could be improved (hover feedback, audio cues, two-hand support). Nice-to-haves, not required for hand-readiness.

**Important.** If the project already uses ISDK hand tracking (`HandGrabInteractable`, `PokeInteractable`, etc.) and has no controller-dependent code, report that it is already hand-ready. Surface enhancements only as Low-priority items — don't treat them as required changes. A project that already works with hands should not receive a long list of improvement suggestions.

## Output Format

Provide your analysis as a structured report:

1. **Project Summary** — what type of project this is and its core mechanics.
2. **Readiness Score** — rough percentage of interactions that already work with hands.
3. **Required Changes** — prioritized list with:
   - what was found (specific files / classes),
   - what needs to change,
   - which ISDK pattern to use (reference `hand-tracking-patterns.md`),
   - estimated complexity (Low / Medium / High).
4. **Quick Wins** — changes that are easy and high-impact.
5. **Migration Risks** — potential issues to watch for.

## Key References

For detailed implementation patterns, read:
- `references/hand-tracking-patterns.md` — 7 ISDK interaction patterns with component references.
- `references/migration-guide.md` — step-by-step controller-to-hands migration checklist.

## Important Notes

- Always maintain controller support as a fallback during migration.
- Consider accessibility — some users may prefer or need controller input.
- Performance matters — hand tracking adds CPU overhead; suggest efficient implementations.
- Test with both left and right hands; don't assume right-hand dominance.
- Hand tracking works best with interactions within arm's reach.
