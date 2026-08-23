# Meta XR Simulator And MCP Validation

This document records local validation tooling for Blockiverse VR. The setup is global on Eric's machine so any Codex workspace can use the same Horizon Debug Bridge, Unity MCP relay, and Meta XR Simulator validation flow.

The smoke script below is historical evidence from the earlier temporary validation world. New simulator checks should use the canonical rulesets in `../rulesets/`, especially `survival_terrain`, `flat_builder`, `void_builder`, canonical registry IDs, and the unified save schema.

## Installed Tooling

Updated 2026-08-19.

- Unity CLI: `~/.unity/bin/unity` (`1.0.0-beta.5`, experimental; `source ~/.unity/env` to add to PATH).
  Reference: https://docs.unity.com/en-us/unity-cli/unity-cli-reference
- Meta XR Simulator app: `/Applications/MetaXRSimulator.app`
- Horizon Debug Bridge npm package: `@meta-quest/hzdb@1.2.1`
- Unity editor: `6000.5.8f1`
- Meta XR Core SDK package: `com.meta.xr.sdk.core@205.0.0`, embedded at
  `Packages/com.meta.xr.sdk.core` with a local compile fix (see Unity Package Notes)
- Meta XR Interaction SDK packages: `com.meta.xr.sdk.interaction@205.0.0`,
  `com.meta.xr.sdk.interaction.ovr@205.0.0` (registry)
- Meta XR Platform SDK package: `com.meta.xr.sdk.platform@205.0.0` (registry)
- Meta XR Operator MCP proxy: `/Users/ericslutz/meta-xr-operator/meta-xr-operator-mcp-proxy`
- Unity MCP relay: `/Users/ericslutz/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64`

The stable validation package set does not commit local editor automation packages such as `com.besty.unity-skills`, `com.unity.ai.assistant`, or `com.meta.xr.unity-mcp.extension`. Those editor tooling packages previously produced non-gameplay Unity warnings in batchmode validation: the Meta MCP extension referenced Interaction SDK assemblies when Interaction SDK was absent, and Unity AI Assistant bundled a duplicate `System.Runtime.CompilerServices.Unsafe.dll`. Keep Unity MCP/AI Assistant packages isolated to a local tooling profile or temporary branch when editor MCP work requires them, then re-run clean package validation before treating simulator/headset logs as stable signal.

## Global Codex MCP Servers

The global Codex MCP config is `/Users/ericslutz/.codex/config.toml`. A pre-edit backup from the initial M3 setup is `/Users/ericslutz/.codex/config.toml.bak-m3-wave0`.

```toml
[mcp_servers.meta-horizon-mcp]
command = "hzdb"
args = ["mcp", "server"]
startup_timeout_sec = 120

[mcp_servers.unity-mcp]
command = "/Users/ericslutz/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64"
args = ["--mcp"]
startup_timeout_sec = 120
```

Codex loads global MCP server definitions when a Codex process or session starts. Restart or reload Codex after changing this config if the tools are not visible in a current session.

## Verification Commands

Run these commands from any project checkout:

```sh
test -d /Applications/MetaXRSimulator.app
node --version
which hzdb
hzdb --version
npm list -g --depth=0 @meta-quest/hzdb --prefix /Users/ericslutz/.nvm/versions/node/v22.12.0
test -x /Users/ericslutz/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64
/Users/ericslutz/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64 --version
/Users/ericslutz/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64 --mcp
```

Expected results:

- `hzdb --version` prints `hzdb 1.2.1.2.140`.
- `npm list` prints `@meta-quest/hzdb@1.2.1`.
- The Unity relay prints `Unity AI Relay` version `1.0.11`.
- `--mcp` starts the Unity MCP server and exits cleanly when stdin closes.

Use `hzdb mcp server` to smoke-test Horizon Debug Bridge MCP startup. If run outside an MCP client, it starts the stdio server and then exits when the initialize request never arrives; this is expected for a terminal-only startup check.

## Unity Package Notes

Updated 2026-08-13: the project moved to the Meta XR `205.0.0` family (required by
Meta XR Operator, which ships in Core `205+`). Core `205.0.0` does not compile on
Unity `6000.5.x` out of the box: `Editor/BuildingBlocks/BlockData/MultiplayerBlocks/NGO/SceneListenerNGO.cs`
reads `createGameObjectHierarchyEvent.instanceId` / `changeGameObjectStructure.instanceId`,
which Unity 6000.5 marks obsolete-as-error (CS0619). The fix is to use `.entityId`
on both lines. Because a package-cache patch is wiped whenever UPM re-resolves, the
patched Core package is **embedded** at `Packages/com.meta.xr.sdk.core` so the fix
survives locally and in CI. Re-check each new Meta `205.x` release and drop the
embed once Meta ships the fix upstream. Interaction, Interaction OVR, and Platform
stay on registry `205.0.0`; Meta Avatars remains `40.0.1`.

Historical (superseded): the project previously pinned Core/Platform/Interaction OVR
at `81.0.1` because, as of June 14, 2026, `203.0.0` had invalid preprocessor placement
in `RuntimeOptimizerPlugin.cs`, `201.0.0` crashed Linux GameCI via the `OVRProjectConfig`
static initializer, and `83.0.0`–`85.0.0` failed GameCI compilation in
`Editor/MetaXRSimulator/Installer.cs`. Quest CI still runs the Android editor image with
`-buildTarget Android` so Meta editor assemblies compile with Quest target symbols.

If Unity MCP is needed for a local editor automation session, install or restore Unity AI Assistant and the Meta XR Unity MCP Extension outside the clean validation baseline. The Unity MCP package stores a relay payload under `Packages/com.unity.ai.assistant/RelayApp~`. On macOS Apple Silicon the payload is a zip named `relay_mac_arm64`. Unity normally unpacks it into `~/.unity/relay/relay_mac_arm64.app` when the relay service starts. If batchmode only creates `~/.unity/relay/relay.json`, unpack it manually, adjusting the package-cache hash to the locally resolved AI Assistant package:

```sh
ditto -x -k Library/PackageCache/com.unity.ai.assistant@<package-cache-hash>/RelayApp~/relay_mac_arm64 /Users/ericslutz/.unity/relay
/bin/chmod +x /Users/ericslutz/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64
```

## Unity CLI And Editor MCP Channels

Added 2026-08-19.

**Unity CLI** (`unity`) is Unity's standalone successor to the Hub CLI. The commands that matter here:

- `unity pipeline list` — lists every running editor instance with project path, PID and `Running`,
  **even without the Pipeline package**. Run it before `scripts/unity/run-tests.sh` or any batchmode
  command: a GUI editor holding the project makes a second instance fail with
  "Multiple Unity instances cannot open the same project". (`unity status` only shows editors that
  have the Pipeline package, so it is not a substitute.)
- `unity test . --mode EditMode|PlayMode --output <nunit.xml> [--filter <pattern>] [--timeout <s>]` —
  resolves the editor from `ProjectVersion.txt`, spawns batchmode, writes NUnit XML. Exit `6` =
  tests failed or editor error, `7` = Unity service unreachable (retry). Still subject to the instance
  lock. `scripts/unity/run-tests.sh` remains the required acceptance gate; use `unity test` for
  targeted runs.
- `unity build . --target Android --execute-method <Editor.Method> -o <path>` — CI-style build with
  Android signing flags (`--android-keystore-base64`, `--android-keystore-password`,
  `--android-key-alias`, `--android-export-type apk|aab`). **Refuses a dirty worktree** unless
  `--allow-dirty-build`. `scripts/unity/build-development-apk.sh` remains the documented path.
- `unity run . -- <raw editor args>` — generic batchmode with forwarded args.
- `unity command` / `unity list` / `unity mcp` (stdio MCP server) / `unity job` — drive a **connected**
  editor. Requires the Unity Pipeline package (`0.5.0-exp.1`, experimental) in the project, which this
  repo does NOT have. `unity pipeline install` edits `Packages/manifest.json`; per CLAUDE.md treat it
  like MCP for Unity / Unity Skills — local-only and only with explicit approval.
- `unity doctor` / `unity env` / `unity editors -i` / `unity license` — diagnostics. `unity skill install
  claude-code` writes an agent skill to `~/.claude/skills/unity-cli/` (persistent config; ask first).

**Three distinct editor-control channels exist — do not conflate them:**

1. **Meta XR SDK editor MCPBridge** (`meta-xr-unity-runtime`): ships in the embedded Core SDK at
   `Packages/com.meta.xr.sdk.core/Editor/MCPBridge/`. HTTP MCP at
   `http://127.0.0.1:<McpBridge_Port>/mcpbridge/` (currently `48736`) with a Bearer token shared with the
   Agent Bridge. Runs whenever the GUI editor is open and exposes TestRunner (`ListTests`, `RunTests` →
   runId + `WaitForTestRun`/`GetTestResults`, `RunAllTests`, `CancelTestRun`), Compilation,
   UIVerification, InteractionTesting, BuildingBlocks and CodeAnalysis tools. **This is how to compile
   and run tests while a GUI editor holds the project lock** — batchmode cannot. Discovery file:
   `~/.unity/mcp/connections/bridge-<id>-<editorPid>.json`.
2. **Unity CLI** `unity mcp` / `unity command` — needs the Pipeline package (above).
3. **MCP for Unity / Unity Skills** — optional local tooling per CLAUDE.md.

**Registration gotcha (2026-08-19):** `meta-xr-unity-runtime` and `meta-xr-operator` are registered in
`~/.claude.json` under the project key for the *parent* directory `.../Side Projects/Blockiverse`, not
`.../Blockiverse/Blockiverse-VR`. Claude Code scopes MCP servers by session cwd, so a session started in
the repo root sees neither (`claude mcp list` is empty). Re-add them for this cwd from a terminal
(`claude mcp add ...` — the AI Tools panel's own buttons fail because Unity's PATH lacks `claude`), or
start the session from the parent directory. The Bearer token is per editor session.

## Meta XR Operator (Runtime Agent Validation)

Added 2026-08-13. Meta XR Operator (experimental, Core SDK `205+`) runs an MCP
server inside the app during Play mode, giving agents runtime access to session
state, frame/composition-layer info, composited eye captures, head/controller
poses, and input injection. Docs: https://developers.meta.com/horizon/documentation/unity/meta-xr-operator/

Setup on this machine (already done; recorded for recovery):

1. One-time: `Meta > Meta XR Operator > Activate` (persists in EditorPrefs).
2. Per editor session: `Meta > Meta XR Simulator > Activate`.
3. Enter Play mode. On macOS the editor sets `XR_API_LAYER_PATH` /
   `XR_ENABLE_API_LAYERS` automatically at Play entry when activated.
4. Connect through the proxy `~/meta-xr-operator/meta-xr-operator-mcp-proxy`
   (stdio MCP; registered with `claude mcp add meta-xr-operator` in the local
   project config). The editor-side Agent Bridge is registered separately as
   `meta-xr-unity-runtime`.

Hard-learned constraints:

- **Do not open or probe raw connections to `http://localhost:8720/sse` while the
  layer is starting.** Aborted/early SSE connections double-faulted
  `libXrApiLayer_METAX_operator` and crashed the entire editor twice on
  2026-08-13. Wait for `[MetaXROperator] Registered ... tool` lines in the
  project `Logs/Editor.log`, then connect via the proxy only.
- The server exists only during Play mode; it dies with the XR session. If the
  simulator frontend disconnects, restart the simulator app before re-entering
  Play.
- The AI Tools panel's "Run Command" registration buttons fail because Unity's
  process PATH lacks the `claude` CLI; run the `claude mcp add ...` commands from
  a terminal instead.
- Simulator-injected controller input via `openxr_set_controller_input` reaches
  the OpenXR layer but has not been observed to trigger app-level input actions
  (e.g. the pause menu). Use simulator keyboard/mouse or on-device input for
  menu-flow validation.

- **Controllers only track in the simulator if the Touch Plus/Pro interaction profiles are
  enabled for the Standalone target.** The simulator presents Touch Plus/Pro controllers and the
  runtime binds `/interaction_profiles/meta/touch_controller_plus` (or `facebook/touch_controller_pro`);
  with only the legacy Oculus Touch profile enabled, Unity's `<XRController>{Hand}/...` actions stay
  unbound — Operator reports tracked poses while the rig's controllers never move. The bootstrapper
  enables the three Touch profiles for Standalone since 2026-08-19; check with
  `openxr_get_active_interaction_profile` and by reading a controller transform in Play mode.
- **Do not leave `adb forward tcp:8720 tcp:8720` active while targeting the editor.** The forward
  binds `127.0.0.1:8720` on the host and the proxy then reaches the (absent) device server instead of
  the editor's; the proxy reports `Server unavailable / offline`. Remove it with
  `adb forward --remove tcp:8720` before editor sessions, add it for on-device sessions.
- Operator MCP tools are not mounted in every agent session. The proxy is a stdio MCP server that
  takes the backend URL as its first argument (`meta-xr-operator-mcp-proxy http://127.0.0.1:8720`),
  so a small JSON-RPC-over-stdio client works when the registered tools are missing. Bundled tools
  include `openxr_get_controller_pose` with `pose_type: grip|aim` and `base_space`, which is how the
  grip→aim offset for the pointer ray was measured (recorded in the commit history, 2026-08-19).
- On-device Operator sessions need a Development build, `adb shell setprop
  debug.oculus.experimentalEnabled 1`, the forward above, a worn headset (session must reach
  `FOCUSED`), and — for captures — `adb shell setprop debug.meta_xr_operator.request_capture_permission 1`.
  `openxr_set_head_pose` is unavailable on a headset. Details: `.claude/skills/meta-xr-operator-unity-meta-quest`.

Validation evidence 2026-08-13: with Unity 6000.5.8f1 + Meta XR 205.0.0 +
Composition Layers 2.5.0 and the world-space menu baseline, Operator inspection in
the simulator showed the session reaching `FOCUSED`, a single healthy projection
layer at frame end (splash layer released, no rogue quad layers), and composited
captures rendering world, UI, and hands — the July "black screen after splash"
symptom did not reproduce. On-device validation remains outstanding.

## Meta XR Simulator Validation Flow

Use Computer Use for editor-facing validation. Unity MCP may also be used from a local tooling profile when available, but it should not be required for the clean simulator/headset validation signal.

1. Open the Unity project in the target worktree.
2. Activate the simulator from `Meta > Meta XR Simulator > Activate`.
3. Enter Play mode in the `Boot` scene.
4. Drive keyboard/mouse input or forwarded controllers.
5. Capture screenshots for PR evidence.
6. Collect simulator logs from `~/Library/Application Support/MetaXR/MetaXrSimulator/logs` when that directory exists.
7. If session capture is stable locally, capture the session for debugging only. Do not commit `.vrs` or other recording artifacts.

MQDH and physical Touch controller forwarding are only needed when keyboard/mouse input is insufficient for a validation case.

## Historical Temporary-World Smoke Script

Use this script only when validating legacy migration behavior from old temporary saves or fixtures:

1. Start a Survival Lite session from `Boot`.
2. Verify spawn is safe and generated terrain is visible.
3. Collect Timber and Slate.
4. Craft a Workbench from 4 Timber.
5. Craft a Storage Crate from 6 Timber and 2 Slate.
6. Place the Storage Crate and move at least one item stack into and out of it.
7. Craft or acquire a Recovery Wrap from 2 Leafmass and 1 Torchbud.
8. Enter a hazard volume and verify health decreases.
9. Use the Recovery Wrap and verify health increases by 25, capped at max health.
10. Force death, verify respawn at the generated safe spawn, and confirm vitals reset to the intended state.
11. Save and reload. Verify game mode, inventory, hotbar, vitals, and crate contents persist.
12. Re-run the relevant Creative Mode smoke check to confirm M2 placement, breaking, hotbar selection, undo, and save/load did not regress.

For new gameplay, record the worktree branch, linked issue, affected rulesets, Unity test commands, APK build command if run, simulator screenshots/log paths, and residual risk in the PR and linked GitHub issue.

## Sources

- [Unity MCP overview](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.9/manual/unity-mcp-overview.html)
- [Unity MCP setup](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.9/manual/unity-mcp-get-started.html)
- [Meta AI tooling overview](https://developers.meta.com/horizon/documentation/unity/ts-ai-tooling-overview/)
- [Horizon Debug Bridge MCP](https://developers.meta.com/horizon/documentation/unity/ts-mqdh-mcp/)
- [Meta XR Unity MCP Extension](https://developers.meta.com/horizon/documentation/unity/unity-mcp-extension/)
- [Meta AI solutions](https://developers.meta.com/horizon/documentation/unity/ai-solutions/)
- [Unity CLI reference](https://docs.unity.com/en-us/unity-cli/unity-cli-reference)
- [Meta XR Operator](https://developers.meta.com/horizon/documentation/unity/meta-xr-operator/)
- [Meta XR Simulator overview](https://developers.meta.com/horizon/documentation/unity/xrsim-intro/)
- [Meta XR Simulator setup](https://developers.meta.com/horizon/documentation/unity/xrsim-getting-started/)
- [Meta XR Simulator session capture](https://developers.meta.com/horizon/documentation/unity/xrsim-session-capture/)
- [Meta XR Simulator logs](https://developers.meta.com/horizon/documentation/unity/xrsim-logs/)
