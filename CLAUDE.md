# CLAUDE.md

This file is the single source of truth for agent instructions in this repository.
`AGENTS.md` intentionally points here.

Canonical game design lives in [docs/rulesets/](docs/rulesets/) and the roadmap in
[docs/roadmap/blockiverse_vr_execution_plan.md](docs/roadmap/blockiverse_vr_execution_plan.md).
Architecture decisions go in [docs/adr/](docs/adr/) — reserved for cross-cutting
architecture; feature-level design decisions belong in the ruleset that owns the system,
which is also why ADR numbers are permanent (gaps from retired ADRs are never reused) —
and the testing contract is
[docs/testing/README.md](docs/testing/README.md).

Current project handoff state lives in [MEMORIES.md](MEMORIES.md).

## Agent Workflow Policy

- The project owner is Eric Slutz; the GitHub username for assignment and review is `eslutz`.
- Eric must provide final approval for complex, high-risk, product-facing, or PR-backed work before merge.
- Eric is currently the only human on the project. Do not configure required approving reviews or required CODEOWNERS review unless another human reviewer is added; otherwise Eric cannot approve his own PR.
- Keep `main` protected with a repository ruleset requiring status checks, linear history, conversation resolution, and force-push protection.
- Use trunk-based development. Do not create a long-lived `develop` branch or long-lived release branches.
- Use short-lived `feature/*`, `fix/*`, `chore/*`, `spike/*`, and `hotfix/*` branches.
- Prefer pull requests into `main` after CI passes. Direct pushes to `main` should be rare and explicit.
- Link pull requests to an issue when active issue tracking exists. Otherwise link the relevant execution-plan section, ruleset, or ADR.
- Use GitHub issues and the `Blockiverse VR Roadmap` project only for active workflow state: bugs, blockers, review work, multi-PR initiatives, and durable follow-ups. The roadmap and rulesets remain the canonical product sources.
- When work begins on an existing issue, assign it to `eslutz` unless Eric explicitly says otherwise.
- Keep PR descriptions useful: scope, linked issue or source doc, validation commands, manual validation, risk notes, and known follow-ups.
- Do not merge a PR, close a PR-backed issue, or move it to Done until Eric has approved the work or explicitly asked for completion.
- Before adding or changing GitHub Actions, packages, SDKs, CLIs, Unity packages, build images, or other third-party dependencies, verify the current stable version from official upstream sources. Prefer latest stable majors unless the repo has a documented compatibility constraint.
- Update documentation when behavior, workflow, architecture, project policy, release process, store submission, or user-visible scope changes.

### Memory And Handoff Policy

- Read [MEMORIES.md](MEMORIES.md) before substantial work. Treat it as the current handoff for decisions, validation status, local tooling state, dirty-worktree constraints, and deferred external gates.
- Keep [MEMORIES.md](MEMORIES.md) concise. Update it when project state, architecture decisions, validation status, package/tooling setup, release gates, or source/generated artifact rules materially change.
- Do not turn [MEMORIES.md](MEMORIES.md) into a changelog. Remove stale paths, obsolete blockers, and superseded decisions when refreshing it.
- Keep long-form testing instructions in [docs/testing/README.md](docs/testing/README.md), not in the memory file.

### Release Policy

- Production releases are cut from `main`.
- Release versioning follows [ADR 0005](docs/adr/0005-release-versioning.md), with `ProjectSettings/BlockiverseVersion.txt` as the SemVer base version source.
- Pull requests use `.github/workflows/quest-ci.yml` for validation only. PR workflows must not receive Meta credentials or publish to Meta release channels.
- Meta channel CD is split across:
  - `.github/workflows/quest-alpha.yml`, which builds a release-signed Quest APK from `main` pushes or manual trusted refs and uploads it to Meta `alpha`;
  - `.github/workflows/quest-promote.yml`, which manually promotes a selected tested Meta build ID through `alpha -> beta`, `beta -> rc`, or `rc -> store` without rebuilding.
- Promotion to `beta`, `rc`, and `store` must preserve the exact tested Meta build artifact. Store promotion requires the `meta-store` environment approval gate.

### Project Guardrails

- Treat Meta Quest 3 and Meta Quest 3S as the primary target platforms.
- Initial multiplayer uses Meta Quest party chat for voice. Do not add in-app voice chat unless the rulesets and roadmap explicitly change.
- Use original names and original assets; do not copy protected third-party identity.
- Gameplay code, UI labels, registries, save data, and tests should use canonical IDs from the rulesets. Legacy IDs must be handled through explicit migration code or marked as historical validation artifacts.
- Never commit secrets, keystores, signing credentials, API keys, `.env` files, Unity `Library`, `Temp`, `Logs`, local generated folders, device logs, screenshots, recordings, Perfetto traces, APKs, or other generated validation artifacts unless a tracked artifact is explicitly required.
- Keystores and production signing material stay outside the repo and in GitHub Actions secrets.
- Current licensing state is source-available / All Rights Reserved, with one carve-out: the compiled dedicated server binary and container image are distributable under the grant in `LICENSE.md` and the terms in `SERVER-EULA.md`. Source is never covered by that grant. Keep `LICENSE.md`, `NOTICE.md`, `SERVER-EULA.md`, and related docs aligned with that posture.

### Tooling Policy

- Prefer reproducible command-line tooling over GUI-only actions when command output is useful validation evidence.
- Use MCP for Unity as the default live Unity Editor bridge when the Editor is open and connected. Treat it as local developer tooling, not a committed project dependency; if needed, install `com.coplaydev.unity-mcp` locally from `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`, configure it from `Window > MCP For Unity > Local Setup Window`, and start the local server at `http://127.0.0.1:8080/mcp`.
- Use Meta XR Operator (experimental, Meta XR Core SDK 205+) for runtime in-VR validation: session/frame/composition-layer inspection, composited eye captures, and pose control of the app running in Play mode with the Meta XR Simulator. Activate once via `Meta > Meta XR Operator > Activate`, activate the simulator per editor session, and connect only through the registered `meta-xr-operator` MCP proxy — never probe `localhost:8720/sse` directly during startup (it has crashed the editor). Full runbook: `docs/testing/meta-xr-simulator-and-mcp.md`.
- Use Unity Skills for module-specific REST workflows, advisory docs, XR diagnostics, batch/workflow operations, console/debug triage, package-aware guidance, and targeted Unity Test Runner jobs. Treat it as local developer tooling, not a committed project dependency; if needed, install `com.besty.unity-skills` locally from `https://github.com/Besty0728/Unity-Skills.git?path=/SkillsForUnity`, start it from `Window > UnitySkills > Start Server`, and verify `http://localhost:8090/health`.
- Before using Unity Skills, read `/Users/ericslutz/.agents/skills/unity-skills/SKILL.md`, then the relevant module `SKILL.md` from `/Users/ericslutz/.agents/skills/unity-skills/skills/`. Honor `currentMode`, approval grants, and forbidden-skill behavior. Do not add skills to the Allowlist unless Eric explicitly asks.
- Before using MCP for Unity, inspect the active instance and project root through MCP resources. If multiple Unity Editors are open, route to this project before mutating scenes, assets, scripts, packages, or tests.
- Tool split: prefer MCP for Unity for general live Editor inspection and automation; prefer Unity Skills when a task needs its REST modules, advisory guidance, XR/test diagnostics, or batch/workflow semantics. Both are investigation and automation aids, not substitutes for committed scripts or test evidence.
- A local package-cache `package.json.meta` GUID conflict can appear when both Unity Skills and MCP for Unity are installed in a developer checkout. Do not commit package manifest or lockfile changes for those tools unless Eric explicitly requests a dependency update. Treat the conflict as local-only only if Unity compiles, both local servers work, and the committed package manifests remain clean.
- Several worktrees or agent sessions can run Unity at the same time; the licence is not exclusive across project paths (measured — see "Sharing the Unity License" below). Announce a long run as a courtesy, never kill another worktree's run, and get agreement before the destructive licensing recovery, which takes the licence from every worktree at once.
- A batchmode run can dirty files you never touched. Diff the whole tree afterwards, not just the paths you expected to change, and revert anything outside your scope rather than letting it ride along in a generated-artifact diff. Two effects are known and neither is yours to commit: package-managed defines moving between build targets in `ProjectSettings.asset` (documented in [MEMORIES.md](MEMORIES.md) — the active target changes during a PlayMode run, and the owning package rewrites its define), and `Assets/UniversalRenderPipelineGlobalSettings.asset` losing the 13 entries of `m_RuntimeSettings.m_List`. **The URP one is intermittent, and not seeing it proves nothing.** It has fired on two worktrees and stayed clean across many more runs on three others, including a worktree that had never touched the asset and a second run on a worktree where it had just fired. Check every time regardless — a clean tree is the common case, not evidence that this is fixed. **That URP deletion must never be committed**, and it is the highest-consequence of the three rather than the most obscure. Those 13 entries are the runtime resource pointers URP carries into a player build; resolving them against the asset's own `references:` type map gives `UniversalRenderPipelineRuntimeXRResources` (XR runtime resources), `VrsRenderPipelineRuntimeResources` (variable rate shading, which this project pins for foveated rendering), and `ShaderStrippingSetting` (shader stripping — the mechanism behind the "renders in the editor, black on device" trap [MEMORIES.md](MEMORIES.md) already warns about), among ten others. The risk is not that the file is dirty; it is that a silent commit is a device-only rendering regression, editor-clean and very hard to diagnose after the fact. No one has yet built a player from an emptied list to confirm breakage — the established part is what the entries are, which is enough.

Revert it unconditionally rather than reasoning case by case: the committed asset legitimately carries all 13 (last written deliberately in `29cb1190`), and no project code touches that asset at all — `grep -rn --include='*.cs' -E "UniversalRenderPipelineGlobalSettings|RenderPipelineGraphicsSettings" Assets/Blockiverse` is empty, so nothing in this repo can be the author. It is **not** the same mechanism as the define churn: mtimes across a full gate put the URP write mid-EditMode, minutes before the PlayMode build-target switch that explains the defines. The trigger is not established. That the same worktree fired once and then stayed clean rules out anything fixed about the project or its path and points at something that varied between runs — a cold versus warm `Library`, or an asset reimport in the first run and not the second, are the obvious candidates. An EditMode-only run on a cold `Library` is the cheap experiment for narrowing it. It is easy to commit by accident because a bootstrapper rerun legitimately rewrites URP assets, so the deletion can look like part of a regeneration diff.
- Use the committed local scripts as the repeatable Unity validation source of truth. `scripts/unity/run-tests.sh` remains the required EditMode and PlayMode validation command.
- Unity CLI (`unity`, installed at `~/.unity/bin`; experimental) is available as local developer tooling. Run `unity pipeline list` before any batchmode command to confirm no Unity Editor already has the project open — a second instance fails to launch. `unity test` and `unity build` may be used for targeted runs and CI-style builds, but the committed scripts above remain the acceptance gate. Do not install the Unity Pipeline package (`unity pipeline install`, which edits `Packages/manifest.json`) without explicit approval; treat it like MCP for Unity and Unity Skills — local-only, never committed.
- Use the globally installed Horizon Debug Bridge CLI, `hzdb`, for Meta Quest device work instead of enabling the hzdb MCP server in the base Codex config.
- Verify Quest-device tooling before device work with `hzdb --version` and `hzdb device list`.
- Use `adb` directly only when `hzdb` does not expose the needed operation or when comparing behavior against lower-level Android tooling; document why the fallback was needed.
- Use GitHub CLI for best-effort GitHub Project updates and cleanup because connector tools may not expose all project mutations.

### Unity Licensing Recovery

> **Destructive to other worktrees.** The Unity licensing client is per-user, not
> per-project — one `Unity.Licensing.Client --namedPipe Unity-LicenseClient-<user>`
> serves every editor on the machine. Killing it takes the license away from any
> Unity run in *any* worktree, not just the stuck one. Run the pre-flight check
> below first, and if another worktree's run is live, coordinate with it instead of
> killing anything.

**Pre-flight — is anything else using Unity right now?**

```sh
ps -eo command= | grep "^/Applications/Unity/Hub/Editor/.*MacOS/Unity "
```

Read this by **grouping on `-projectPath`, not by counting lines**. One run
routinely shows two or three: the editor itself launches import workers from the
same binary (`-adb2 ... -name AssetImportWorkerHW0`), and those match too. That is
wanted — a busy worker still means a run is in progress there — but three lines sharing
a `-projectPath` is one run, not three. To attribute a line whose path is ambiguous,
`lsof -d cwd -p <pid>` gives the owning working directory.

Note what this pre-flight is and is not for. Another worktree's live run does **not**
block yours — start it. This check exists because the recovery below kills the shared
per-user licensing client, which would take the licence out from under that run too.

Proceed with **the recovery** only when the projects listed are just this one (or there
are none); otherwise coordinate with the other run's owner first. This constraint is on
the recovery, not on starting an ordinary run. Do not use `pgrep -f`/`pkill -f` with a
bare `Unity` pattern for this check — see "Matching Unity Processes Safely" below.

If Unity batchmode logs `ResponseCode: 505`, `Unsupported protocol version '1.18.1'`,
or waits on `LicenseClient-ericslutz-6000.5`, and the pre-flight shows no other
project's run, reset the local Unity/Hub process state.

**Select processes by PID, never by `pkill -f <pattern>`.** An agent submits these
blocks through a command wrapper, so the invoking shell's own argument list contains
whatever pattern you write — `pkill -f 'Unity.Licensing.Client|…'` can therefore match
and kill the very shell running the recovery, before the verification or the retry
happens. Deriving PIDs from an install-path-anchored `ps` cannot match a shell.

```sh
# 1. Stuck editors, by PID. Confirm every hit is this project (see pre-flight) first.
ps -eo pid=,command= | grep "/Applications/Unity/Hub/Editor/.*MacOS/Unity "

# 2. Terminate them and WAIT. An editor left alive keeps the project lock, and the
#    retry below then fails to launch even though the licensing reset succeeded.
#    SIGTERM first so Unity releases the lock cleanly; escalate only if it will not go.
kill <editor-pids>
while ps -eo command= | grep -q "^/Applications/Unity/Hub/Editor/.*MacOS/Unity "; do sleep 2; done

# 3. Hub, then the licensing client — again by PID.
osascript -e 'tell application "Unity Hub" to quit'
ps -eo pid=,command= | grep "/Applications/Unity/.*Unity.Licensing.Client" | awk '{print $1}' | xargs -r kill

# 4. Verify: this must print nothing before retrying.
ps -eo command= | grep "^/Applications/Unity"

# 5. Retry.
scripts/unity/run-tests.sh
```

Step 4 anchors on `^/Applications/Unity`, so it catches editors, the licensing client,
the package-manager server and shader compilers while matching no shell or `grep` of
its own. Do not leave stuck Unity batchmode processes running.

### Sharing the Unity License

**There is no licence token, and you do not need anyone's permission to run Unity in
your own worktree. Just run it.** Announce a long run as a courtesy so others know the
machine is loaded; do not wait for a reply.

This reverses earlier guidance in this file, so here is the measurement rather than an
assertion. On 2026-08-22, two batchmode editors ran concurrently on different project
paths, both fully licensed:

```txt
pid=97104 elapsed=05:27 EDITOR worktrees/emitter-shadow-edges-fb81e3   (Android APK build)
pid=99355 elapsed=01:25 EDITOR worktrees/blockiverse-self-hosted-server-734ac2
```

The second editor's log shows `Successfully connected to LicensingClient` and
`Successfully resolved entitlement details` while the first was mid-build — no `505`,
no `Unsupported protocol version`, no wait. Reproduce it the same way before believing
any future claim of exclusivity: start a run while another worktree's is live and read
the `[Licensing::` lines.

Three things ARE real, and they are the only reasons to coordinate:

1. **Two editors cannot open the same project path.** The second fails to launch. This
   is a genuine lock, but it is per-project, so it constrains *your own* worktree — a
   stale editor of your own is the usual cause — not anyone else's.
2. **Concurrent runs are heavy** (editor, import workers, shader compilers, ILPP) and
   will slow each other down. That is a reason to mention a long run, and a reason to
   avoid stacking four of them. It is not a reason to block.
3. **The licensing *client* is a single per-user process** (`Unity-LicenseClient-<user>`).
   A shared service is not an exclusive lock — runs coexist through it fine — but it
   does mean the recovery above is destructive to every worktree at once. **That is the
   one action requiring agreement before you take it.**

**Never kill another worktree's run.** This is the rule that actually protects people,
and it is now the whole of the etiquette. A poller once destroyed an in-flight APK
build here by claiming a momentary gap. If a run genuinely must be stopped, ask its
owner; if the owner's session is gone and a stale editor is blocking *its own* project
path, only that project's next user needs to clear it.

If you want to know who is running what — before the recovery, or to explain why the
machine is slow — `ps` answers it directly and is the authority for *who holds what
right now*. Use the anchored forms below rather than `pgrep -f`. You no longer need to
infer permission from it, which was the source of every past failure here: absence of
a process could never distinguish *free* from *between two of someone else's runs*, and
sessions kept trying to make that inference carry weight it could not bear.

Historical note, kept because the reasoning recurs and the failure mode is general:
this section previously specified a named hand-off queue, a "never poll for a gap"
rule, and a 10-minute silent-holder takeover. All of it existed to ration an exclusive
resource that turned out not to be exclusive. Two separate sessions independently
asserted the exclusivity and then retracted it, and the queue survived both retractions.

**Why it survived is the part worth keeping.** Nothing could contradict it. Every
session that respected the queue observed exactly what the queue predicted — waited,
got the licence, ran fine — so compliance was mistaken for confirmation. A rule that is
never violated produces no evidence about whether it is true, and an unviolated protocol
and a green test suite are the same epistemic object: absence of contradiction, read as
proof.

The way out is the same in both cases — **measure the constraint itself, not compliance
with it.** Break the thing deliberately and check that the alarm sounds. Here that meant
starting a second editor on another project path and reading the `[Licensing::` lines
instead of politely waiting one more time. It is the same move as gutting an
implementation to prove its tests can actually fail.

So: before you write a rule that serialises work, or trust a suite that has never gone
red, go and falsify it once.

### Matching Unity Processes Safely

Process checks around Unity have broken three separate agent sessions on this
machine, always the same way: a `-f` pattern matches the *observer* as well as the
observed.

- `pgrep -afil 'Unity|Licensing|UnityPackageManager'` matched **31 processes** on a
  machine running exactly 2 editors. `PATH` contains `~/.unity/bin`, so every shell
  this repo's tooling spawns matches on its environment alone, as do unrelated MCP
  servers and the checking command itself. Its documented acceptance ("should return
  no processes") is therefore unsatisfiable and reads as a stuck process that is not
  there.
- `pgrep -f "MacOS/Unity -batchmode"` matches the shell running it, because that
  literal is in its own command line. A guard built on it reports a Unity run that
  does not exist.
- `pkill -f` with a broad pattern kills other worktrees' runs. This has destroyed an
  in-flight build. It can also kill **the shell running the recovery**: an agent submits
  a block through a command wrapper, so the invoking shell's argument list contains the
  pattern being searched for. This survived in the recovery block above through several
  revisions of this very section — the fix is to select by PID from an anchored `ps`.
- A **wait loop** built on a self-matching pattern (`while pgrep -f "...Unity..."; do
  sleep; done`) is the worst of the family, because it fails differently: it is not a
  one-shot false positive but a mutual deadlock that *grows*. Each waiter's own
  command line satisfies the condition it is waiting on, so every additional waiter
  makes the wait strictly harder for all the others. Thirty such loops once ended up
  deadlocked against each other here. It presents as "the run is slow" rather than as
  an error, so it is found by wondering why so many background tasks are alive, not
  by reading a failure.

Two safe forms. Match on the process *name*, which no shell command line can satisfy:

```sh
# Is anything running? (editors and their import workers; workers exit with their editor)
pgrep -x Unity

# How many runs? Workers share the binary, so the name alone cannot tell them apart —
# read the command line to drop them.
for p in $(pgrep -x Unity); do
  ps -p "$p" -o command= | grep -q -- "-adb2\|AssetImportWorker\|-srvPort" || echo "editor $p"
done
```

Or anchor on the install path, which additionally shows you *whose* run it is:

```sh
ps -eo command= | grep "^/Applications/Unity/Hub/Editor/.*MacOS/Unity "
```

Read that by grouping on `-projectPath`, not by counting lines — one run shows two or
three. Three things break naive grouping, and this repo trips all of them:

- **The checkout path contains a space** (`.../Code/Side Projects/...`), so the obvious
  `sed -E 's/.*(-projectPath [^ ]*).*/\1/'` truncates at `Side` and silently groups
  unrelated worktrees together. Take everything after the flag up to the next one:
  `sed -E 's/.*-projectPath (.*)$/\1/; s/ -[a-zA-Z].*$//'`.
- **A relative `-projectPath .`** — this repo's own bootstrapper invocation — shows as
  `.` and is missed by any grouping on directory names.
- **Import workers** identify their parent with `-parentPid`, not by repeating the
  project.

`lsof -a -p <pid> -d cwd -Fn` resolves all three: it gives the real working directory
regardless of how the argument was written.

Kill by PID after identifying the specific process, not by pattern.

## Commands

Unity 6000.5.8f1 (Apple Silicon path is the default; override with `UNITY_EDITOR`).

```sh
# Required validation — runs EditMode then PlayMode, NUnit XML to TestResults/Unity/
scripts/unity/run-tests.sh

# Single test / one platform — the script takes --platform, --filter, --results-name,
# and --results-dir. Use it rather than invoking Unity directly: it passes -nographics
# only for EditMode, because a PlayMode run without a graphics device segfaults inside
# EnterPlayMode with a native stack that reads like a code bug. (Set
# UNITY_PLAYMODE_NOGRAPHICS=1 to opt in deliberately.)
scripts/unity/run-tests.sh --platform EditMode \
  --filter "Blockiverse.Tests.EditMode.SomeClass.SomeTest" --results-name Single

# Builds (entry points in Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs)
scripts/unity/build-development-apk.sh            # dev APK; runs the bootstrapper first
# Release-signed APKs are built by .github/workflows/quest-alpha.yml only.

# Linux dedicated server (generates the server scene, then builds it)
scripts/unity/build-linux-server.sh
# Needs the linux-server editor module. The build refuses to run without an explicit version:
# a server advertises it in the approval payload and clients on another version are refused.

# After any gate that touches assembly definitions: assert the test assemblies were DISCOVERED.
# A mis-resolved asmdef reference drops a whole assembly silently and the run still reports green.
scripts/unity/check-test-suites.py

# Generated original assets (never hand-author; regenerate instead)
python3 scripts/art/generate-art-assets.py        # block/item/UI/VFX textures + atlas
python3 scripts/audio/generate-audio.py           # music bed + classic block cues ONLY

# Sound effects are built from licensed third-party source recordings staged
# outside the repo (see docs/audio/audio-asset-manifest.md for the full pipeline).
python3 scripts/audio/make-audio-manifest.py      # regenerate the cue -> source map
python3 scripts/audio/build-audio-assets.py --check   # verify sources resolve first
python3 scripts/audio/build-audio-assets.py       # build the shipping cues
python3 scripts/audio/validate-audio-assets.py    # gate: format/level/licence of what shipped
python3 scripts/audio/make-audio-docs.py          # refresh the provenance table
```

`generate-audio.py` still synthesizes the whole original cue set, but only writes
the music tracks and `classic_block_*` into `Assets/` — the other cue names now
hold licensed production audio. Use `--dump-legacy <dir>` (outside `Assets/`) to
render the full original set for comparison.

`.github/workflows/quest-ci.yml` validates pull requests with repository checks, Unity Personal activation through GameCI, Unity tests, and an Android smoke APK. `.github/workflows/quest-alpha.yml` builds the release-signed APK that goes to Meta `alpha`. `.github/workflows/quest-promote.yml` promotes already-uploaded Meta build IDs to `beta`, `rc`, and eventually `store` without rebuilding.

## Architecture

VR voxel sandbox for Meta Quest 3/3S. Unity 6, URP, OpenXR + Meta XR SDK 205 (core embedded with a local `entityId` compile fix — see docs/testing/meta-xr-simulator-and-mcp.md), XRI, Netcode for GameObjects 2.13.1. LAN host-authoritative co-op. No scene switching: `Assets/Blockiverse/Scenes/Boot.unity` is the whole game.

### Dedicated server

A headless Linux server build ships alongside the Quest client; see
[ADR 0007](docs/adr/0007-self-hosted-dedicated-server.md) and [docs/server/](docs/server/).
`NetworkSessionMode.Server` is authoritative with no local player. Two rules that are easy to
break and expensive to debug:

- Presentation assemblies are kept out of the server with `excludePlatforms`, **never** with
  `defineConstraints: ["!UNITY_SERVER"]` — that define is set for Editor scripts too, which stops
  `Blockiverse.Editor` compiling and makes `-executeMethod` unrunnable, including the build itself.
- `Blockiverse.MetaAvatars` ships in the server build despite being useless to it: its components
  are on the shared network player prefab and one is a `NetworkBehaviour`, so excluding it would
  change the spawn contract between server and client.

### Assembly layering (Assets/Blockiverse/Scripts/)

Bottom → top; an assembly may only reference those below it:

- **Core** (logging facade `BlockiverseLog`, canonical paths/constants in `BlockiverseProject`) and **Networking** (thin LAN session over NetworkManager/UnityTransport — no gameplay knowledge)
- **Voxel** — the data model: `VoxelWorld` (flat `BlockId[]`, `BlockChanged` event, changed-block delta set), `BlockRegistry`, `BlockMutationAuthority` (the single validation gate for world edits), `ChunkDeltaLog`, `DeterministicHash`
- **Survival.Health** (vitals/hazards; note its rootNamespace is `Blockiverse.Survival`) and **WorldGen** (terrain presets, seed-pure `SurvivalBiomeResolver`, structures/vegetation, Markov `WeatherService`, `WorldConstants`: ChunkSize 16, WorldMaxY 127, SeaLevel 64, 20 ticks/s, 24000-tick day)
- **Survival** — items/inventory/crafting/stations/harvest/farming; `ItemRegistry`, `ContainerInventoryStore`
- **Persistence** (`WorldSaveService` — see save format below) and **MetaAvatars** (Meta Avatars streaming over Netcode at 15 Hz)
- **Gameplay** — the integration hub: `CreativeWorldManager` (central world owner for both modes; `Awake()` generates a default world), `MultiplayerChunkAuthoritySync` (block mutations + late-join world distribution), `MultiplayerSurvivalSync` (the entire survival economy command channel), rendering/lighting (`VoxelWorldRenderer`, `ChunkMeshBuilder`, `VoxelSkyLightMap`, `WorldTimeClock`)
- **VR** (XR rig, input, comfort) → **UI** (menu router/panels + `BlockiverseWorldSessionController`) → **Editor** (the bootstrapper; editor-only)

EditMode tests live per-area under `Assets/Blockiverse/Tests/EditMode/`, PlayMode (incl. real Netcode host/client sessions) under `Tests/PlayMode/`.

### Cross-cutting invariants

- **No `InternalsVisibleTo` anywhere.** `internal` members are invisible across assemblies — cross-assembly APIs must be `public`. This has shipped compile breaks before; the asmdef boundary, not the namespace, is what matters (Survival.Health shares the `Blockiverse.Survival` namespace but not the assembly).
- **Engine-free simulation core.** Voxel, Survival, Survival.Health, and WorldGen have no `UnityEngine` dependency. This is what allows world generation on background threads (`Task.Run` in late-join sync) and plain NUnit EditMode tests. Do not introduce `UnityEngine` into these assemblies.
- **Host authority.** The host owns chunk generation, mutation validation/commit (`ChunkAuthorityBoundary` flags), delta broadcast, late-join sync, all survival economy resolution (inventories, crafting, stations, drop rolls, shared crate), and multiplayer saves. Clients only send requests and mirror snapshots. Exception: each peer simulates its own vitals locally.
- **Determinism.** Everything seed-derived goes through `DeterministicHash.Hash/UnitRoll` (distinct salts per system) or the seed-pure biome resolver; simulation advances only on `WorldTimeClock` ticks; weather RNG state and tick counts travel in sync snapshots so late-joiners stay in lockstep. Wall-clock randomness is allowed only where host-authoritative (harvest drop rolls). Never put live sim state on background threads — only pure generation.
- **Canonical string IDs** (from the rulesets, e.g. `meadow_turf`) are the persistence and wire vocabulary; saves store canonical strings and registry hashes. Int `BlockId` values are in-memory only. New code, UI labels, and saves must use canonical IDs.
- **Scenes and prefabs are generated, not hand-edited.** `BlockiverseProjectBootstrapper.Run()` (menu: Blockiverse → Bootstrap Unity Quest Project, 4K lines, idempotent) produces the Boot scene, XR rig prefab, network prefabs, player settings, materials, and input actions. To change scene/prefab wiring, change the bootstrapper and rerun it.

### Runtime flow

Boot scene carries the XR rig (with all world-space menus and both controllers' input), a world root (`CreativeWorldManager` + renderer + interaction), and the full network/survival stack — so single-player survival and LAN host/join work without scene loads. `BlockiverseMenuController` only routes screens and emits `ActionRequested(actionId)` (constants in `MenuActions`); `BlockiverseWorldSessionController` implements the session verbs (new world from seed text, save/load/continue). Menu flows are specified in `docs/rulesets/voxel_survival_menus.md`.

### Save format

`<name>.vxlworld/` directory (schema v4): `manifest.json` (pretty-printed, registry hashes), `dimensions/main/` (dimension, environment, containers, `regions/r.<rx>.<rz>.vxlr`), `players/local_player.json`. Regions store **only changed blocks** (delta vs. terrain regenerated from seed) as 16-block sections with string palettes. All writes are atomic (`.tmp` → move/replace; regions dir swap keeps a `.bak` recovery window). Legacy v1–v3 saves fail fast — no migrations pre-release. Single-player saves live under `Application.persistentDataPath/Saves`; the multiplayer host world is `multiplayer-world.vxlworld`.

## Documentation currency

Two companion projects must stay current alongside this repo:

- **Wiki** (`../Blockiverse-VR.wiki`) — the primary source for all user-facing documentation (gameplay mechanics, controls, crafting/survival rules, save format, multiplayer setup, store descriptions, release notes, known issues). The wiki is what players and store reviewers read; it must reflect the shipped state of the game, not aspirational plans.
- **Website** (`../Blockiverse-VR.website`) — the public-facing project site; keep store metadata, feature lists, screenshots, and versioning consistent with what is actually in the game.

**When to update:** any change that affects a user-observable behaviour, a publicly documented feature, or a store-submitted artefact warrants a corresponding wiki and/or website update in the same PR or immediately following commit. This includes (but is not limited to):

- New or changed gameplay mechanics, survival rules, crafting recipes, or block/item behaviour
- Save-format version bumps or migration behaviour changes
- Multiplayer session flow changes (hosting, joining, disconnect handling)
- VR comfort, control binding, or locomotion changes
- New store-ready features, screenshots, or release notes
- Changes to the privacy policy or data-use declarations

Changes that are purely internal (refactors, test additions, CI fixes, performance work with no observable behaviour change) do not require wiki or website updates, but use judgement — if a performance fix removes a known limitation that appears in the known-issues page, update it.
