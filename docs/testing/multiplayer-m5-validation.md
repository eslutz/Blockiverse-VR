# Historical Multiplayer Validation

Date recorded: 2026-05-28

Branch: `feature/m5-remaining-multiplayer`

Unity editor: 6000.3.16f1

Networking packages:

- `com.unity.netcode.gameobjects` 2.11.2
- `com.unity.transport` 2.7.3

## Automated Evidence

Command:

```sh
scripts/unity/run-tests.sh
```

Result files:

- `TestResults/Unity/EditMode.xml`: 121/121 passed, 0 failed, start `2026-05-28 13:37:59Z`, end `2026-05-28 13:38:05Z`.
- `TestResults/Unity/PlayMode.xml`: 46/46 passed, 0 failed, start `2026-05-28 13:38:25Z`, end `2026-05-28 13:38:39Z`.

M5 multiplayer coverage in the PlayMode suite:

- `ClientBlockMutationRequestsAreHostValidatedBroadcastAndLateJoinSynced` proves client edits are sent to the host, validated there, broadcast as ordered chunk deltas, rejected deterministically when invalid or stale, and included in late-join snapshots.
- `CompetingClientBlockMutationsRejectStaleRequestAndPreserveAuthoritativeWinner` proves two clients racing for the same block converge on the host-authoritative winner and the stale request is rejected as `ExpectedBlockMismatch`.
- `NetworkedSurvivalLiteActionsStayHostAuthoritativeAndPerPlayer` proves host-authoritative harvesting, per-player inventory snapshots, shared crate transfers, and host-validated crafting across two clients.
- `ActiveBlockEditingConvergesUnderSimulated100MsLatency` configures Unity Transport simulator parameters with `PacketDelayMs = 100` after connection and proves an active client block edit converges on host and client with no pending mutation left open.
- `ChunkDeltasConvergeUnderSimulatedPacketLoss` configures Unity Transport simulator parameters with `PacketDropInterval = 5` after connection and proves three ordered block deltas converge on the requesting client and observer client.

The latency and packet-loss checks use Unity Transport's `SimulatorUtility.Parameters` through the session `UnityTransport` driver. The older `UnityTransport.SetDebugSimulatorParameters` API is obsolete in this package, so the tests avoid it.

## Active Block-Editing Bandwidth

The current block-editing custom-message writer capacities are:

- Client mutation request: 128 bytes.
- Host mutation delta broadcast: 160 bytes.
- Host rejection/result response: 128 bytes.
- Late-join snapshot: 80 byte header plus 32 bytes per changed block.

For a two-player accepted client edit, the expected application payload is approximately 288 bytes: 128 bytes from client to host, then one 160 byte host delta to the remote client. At 10 accepted edits per second this is about 2.88 KB/s of application payload. At 20 accepted edits per second this is about 5.76 KB/s.

For host-authored edits, each remote client receives one 160 byte delta per edit. Each additional remote client adds about 160 bytes per accepted edit, or about 1.6 KB/s at 10 edits per second and 3.2 KB/s at 20 edits per second.

Rejected stale or invalid client edits use approximately 256 bytes of application payload in a two-player session: one 128 byte request plus one 128 byte result response. These estimates exclude Netcode for GameObjects headers, Unity Transport headers, reliable-delivery retransmits, connection management, player avatar updates, and Quest party-chat traffic.

## Scope And Residual Risk

This evidence is local editor multi-client validation on loopback transport from the earlier milestone plan. It remains useful as historical proof of host-authoritative command and delta behavior, but new validation should follow [Voxel Multiplayer and Networking Ruleset](../rulesets/voxel_multiplayer_networking_ruleset.md) and synchronize canonical world metadata, registry versions, structure state, vegetation state, and environment state.

Quest-device network captures, OVR Metrics captures, and headset multiplayer smoke evidence remain part of later device validation before store-candidate release work. Do not treat this document as Quest 3 or Quest 3S device proof.
