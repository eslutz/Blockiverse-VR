# Meta XR Simulator And MCP Validation

This document records the local M3 validation tooling for Blockiverse VR. The setup is global on Eric's machine so any Codex workspace can use the same Horizon Debug Bridge, Unity MCP relay, and Meta XR Simulator validation flow.

## Installed Tooling

- Meta XR Simulator app: `/Applications/MetaXRSimulator.app`
- Horizon Debug Bridge npm package: `@meta-quest/hzdb@1.2.1`
- Verified `hzdb` CLI version: `hzdb 1.2.1.2.140`
- `hzdb` install prefix: `/Users/ericslutz/.nvm/versions/node/v22.12.0`
- Meta XR Core SDK package: `com.meta.xr.sdk.core@201.0.0`
- Meta XR Interaction SDK package: `com.meta.xr.sdk.interaction.ovr@201.0.0`
- Unity MCP relay: `/Users/ericslutz/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64`

The stable validation package set does not commit `com.unity.ai.assistant` or `com.meta.xr.unity-mcp.extension`. Those editor tooling packages previously produced non-gameplay Unity warnings in batchmode validation: the Meta MCP extension referenced Interaction SDK assemblies when Interaction SDK was absent, and Unity AI Assistant bundled a duplicate `System.Runtime.CompilerServices.Unsafe.dll`. Keep Unity MCP/AI Assistant packages isolated to a local tooling profile or temporary branch when editor MCP work requires them, then re-run clean package validation before treating simulator/headset logs as stable signal.

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

The committed package set includes Meta XR Interaction SDK because the Meta tooling and simulator validation paths reference its `Oculus.Interaction` assemblies. As of May 26, 2026, the official Meta package registry reports `com.meta.xr.sdk.core@201.0.0` and `com.meta.xr.sdk.interaction.ovr@201.0.0` as the current versions, and Interaction OVR depends on `com.meta.xr.sdk.interaction@201.0.0` plus Core `201.0.0`.

If Unity MCP is needed for a local editor automation session, install or restore Unity AI Assistant and the Meta XR Unity MCP Extension outside the clean validation baseline. The Unity MCP package stores a relay payload under `Packages/com.unity.ai.assistant/RelayApp~`. On macOS Apple Silicon the payload is a zip named `relay_mac_arm64`. Unity normally unpacks it into `~/.unity/relay/relay_mac_arm64.app` when the relay service starts. If batchmode only creates `~/.unity/relay/relay.json`, unpack it manually, adjusting the package-cache hash to the locally resolved AI Assistant package:

```sh
ditto -x -k Library/PackageCache/com.unity.ai.assistant@<package-cache-hash>/RelayApp~/relay_mac_arm64 /Users/ericslutz/.unity/relay
/bin/chmod +x /Users/ericslutz/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64
```

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

## M3 Survival-Lite Smoke Script

Use this script after each integration PR that changes gameplay, persistence, or VR input:

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

Record the worktree branch, linked issue, Unity test commands, APK build command if run, simulator screenshots/log paths, and residual risk in the PR and linked GitHub issue.

## Sources

- [Unity MCP overview](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.9/manual/unity-mcp-overview.html)
- [Unity MCP setup](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.9/manual/unity-mcp-get-started.html)
- [Meta AI tooling overview](https://developers.meta.com/horizon/documentation/unity/ts-ai-tooling-overview/)
- [Horizon Debug Bridge MCP](https://developers.meta.com/horizon/documentation/unity/ts-mqdh-mcp/)
- [Meta XR Unity MCP Extension](https://developers.meta.com/horizon/documentation/unity/unity-mcp-extension/)
- [Meta AI solutions](https://developers.meta.com/horizon/documentation/unity/ai-solutions/)
- [Meta XR Simulator overview](https://developers.meta.com/horizon/documentation/unity/xrsim-intro/)
- [Meta XR Simulator setup](https://developers.meta.com/horizon/documentation/unity/xrsim-getting-started/)
- [Meta XR Simulator session capture](https://developers.meta.com/horizon/documentation/unity/xrsim-session-capture/)
- [Meta XR Simulator logs](https://developers.meta.com/horizon/documentation/unity/xrsim-logs/)
