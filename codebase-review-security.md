# Codebase Review — Security Expert

> Workflow run `wf_53b36881-009`, agent `a866704bcc80c034c`. Raw expert output, pre-verification.

## Area Reviewed

Application/network security of the LAN host-authoritative co-op stack: every custom named-message and RPC handler in MultiplayerSurvivalSync (~3K lines), MultiplayerChunkAuthoritySync, MultiplayerWorldPersistence, MetaAvatarStreamRelay/MetaAvatarStreamMessage, and the Networking assembly (BlockiverseNetworkSession/Config); the FastBufferReader deserialization paths (traced into the NGO 2.11.2 package source in Library/PackageCache); host/client trust boundaries (PlayerHello GUID reconnect identity, server-authoritative inventory/tool/station resolution, BlockMutationAuthority bounds checks); local storage (PlayerPrefs, WorldSaveService save format, path-traversal guards); Android/Quest permissions (AndroidManifest, ProjectSettings); dependency versions (Packages/manifest.json); and a repo-wide secret scan (CI workflows, build scripts, ProjectSettings). Overall the design is genuinely server-authoritative and shows good defensive instincts in many places (client-supplied item stacks are ignored, save paths are trust-rooted, avatar sender ids are re-stamped, mutations are bounds-checked, payloads are size-clamped in some paths, no hardcoded secrets). The principal weakness is the raw deserialization of attacker-controlled strings through NGO's FastBufferReader, which has an integer-overflow bounds bypass that a single malicious LAN packet can use to force a multi-gigabyte allocation on the host. Secondary issues are the entirely client-asserted reconnect-identity GUID carried in cleartext, open join with no approval/cap, and the absence of any per-client command rate limiting.

## Findings (7)

### 1. Attacker-controlled string length in named-message handlers triggers multi-GB host allocation (deserialization DoS)

- **Severity:** High  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Library/PackageCache/com.unity.netcode.gameobjects@beeaefb722f7/Runtime/Serialization/FastBufferReader.cs`
- **Impact:** Any unauthenticated LAN peer can crash or severely stall the host (and thus drop the entire co-op session for all players) by sending a single ~16-byte malformed command. Because the host owns all world generation, mutation validation and the survival economy, killing the host process ends the game for everyone.
- **Evidence:** Host-side handlers read attacker-controlled strings with no length sanity check: HandleCommandRequestMessage reads `reader.ReadValueSafe(out string outputItemId)` (MultiplayerSurvivalSync.cs:2153), ReadItemStack reads `reader.ReadValueSafe(out string itemId)` (line 3048) for crate/station commands, and HandlePlayerHelloMessage reads `reader.ReadValueSafe(out string guid)` (line 2698). In NGO 2.11.2, FastBufferReader.ReadValueSafe(out string) (FastBufferReader.cs:603-641) reads the length with the NON-range-checked `ReadLength(out int length)` (line 618 -> 664-668, casts uint->int with no bound), then bounds-checks via `TryBeginReadInternal(length * sizeof(char))` (line 620). With a declared length of 0x40000000 the `length * 2` multiply overflows signed int to a negative value, so TryBeginReadInternal (line 439: `Position + bytes > Length`) returns true and the guard is bypassed; line 624 then executes `s = "".PadRight(length)`, attempting a ~2 GB string allocation from a tiny packet. NGO's HandleMessage wraps handlers in try/catch (NetworkMessageManager.cs:420-427) so an eventual OutOfMemoryException is caught, but the oversized allocation attempt itself is the DoS on the 8 GB shared-memory Quest.
- **Recommended fix:** Do not feed attacker-controlled buffers to ReadValueSafe(out string) unbounded. Before each string read, validate a maximum length against the remaining buffer (e.g. read a uint length, reject if it exceeds a small per-field cap like 64 and/or `reader.Length - reader.Position`), or read canonical ids as a fixed-cap byte span (mirroring the 64 KiB clamp already used in MetaAvatarStreamMessage.NetworkSerialize). Apply the same guard to every ReadValueSafe(out string) reachable from a client message (outputItemId, itemId in ReadItemStack, guid, and the host->client activeOutputItemId at line 2378).

### 2. Reconnect-identity GUID is fully client-asserted and sent in cleartext, enabling inventory theft

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Prefabs/Networking/BlockiverseNetworkManager.prefab`
- **Impact:** A malicious LAN peer can steal a disconnected teammate's stashed inventory. Because joins are unauthenticated and traffic is unencrypted, an attacker can sniff a victim's persistent GUID off the wire and later present it as their own to receive the victim's stashed items.
- **Evidence:** HandlePlayerHelloMessage (MultiplayerSurvivalSync.cs:2693-2709) trusts the client-supplied GUID with zero verification: it sets `playerGuidsByClientId[senderClientId] = guid` and, if a stash exists, `inventoriesByClientId[senderClientId] = stashed; SendInventorySnapshot(senderClientId)`. The departing player's inventory is stashed under that GUID in HandleClientDisconnected (lines 2650-2654). SendPlayerHello (lines 2671-2689) writes the raw GUID with `writer.WriteValueSafe(ResolveLocalPlayerGuid())` and the prefab sets `m_UseEncryption: 0`, so the GUID is observable to any LAN sniffer. There is no binding between the GUID and the transport-level client identity, and no check that the GUID is not already in use by another connected client.
- **Recommended fix:** Bind reconnect identity to something the host can attest rather than a self-declared cleartext token: e.g. only honor a stash hand-back if the requesting connection presents the GUID over an approved/encrypted channel, reject a GUID that is currently claimed by another connected client, and ideally derive identity from NGO connection-approval payload validated at join time. At minimum enable transport encryption so the GUID is not sniffable.

### 3. Connection approval disabled with no join gate, player cap, or encryption

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Prefabs/Networking/BlockiverseNetworkManager.prefab`, `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkSession.cs`
- **Impact:** Any device that can reach UDP 7777 on the host can join an in-progress game with no password or approval, and there is no cap on concurrent clients. An attacker can open unlimited connections (each spawns a player NetworkObject, allocates a host inventory, and triggers a full late-join world snapshot regeneration), exhausting host CPU/memory.
- **Evidence:** BlockiverseProjectBootstrapper.cs:1250 sets `networkManager.NetworkConfig.ConnectionApproval = false;` and no ConnectionApprovalCallback is assigned anywhere (grep across Assets/Blockiverse returns none). The prefab sets `m_UseEncryption: 0`. BlockiverseNetworkSession.StartHost/StartClient (lines 69-104) call SetConnectionData with no SetServerSecrets/DTLS and never register approval. There is therefore no authentication, no max-connection enforcement, and no integrity protection on the LAN session. Each new client triggers HandleClientConnected -> SendLateJoinSnapshot + SendEnvironmentSnapshot (MultiplayerChunkAuthoritySync.cs:214-231) and a fresh inventory.
- **Recommended fix:** Enable ConnectionApproval with a callback that (a) enforces a maximum player count, (b) optionally validates a host-set session password/token carried in the connection payload, and (c) rate-limits new connections per source. Enable UnityTransport encryption (SetServerSecrets / DTLS) for the session so payloads and the reconnect GUID are not exposed on the LAN.

### 4. No per-client rate limiting on the host command/mutation channels (flood + broadcast amplification)

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- **Impact:** A connected client can flood the host with survival commands or (in creative worlds) raw block mutations as fast as it can send. Each accepted operation makes the host broadcast inventory/crate/station/delta snapshots to all clients, so one malicious peer can amplify its traffic to grief or stall the whole session.
- **Evidence:** HandleCommandRequestMessage (MultiplayerSurvivalSync.cs:2094-2186) dispatches every client command with no throttling; the only bound is the per-client ProcessedRequestWindow (lines 2947-2966) which merely de-duplicates by requestId, and a flooder simply increments requestId each call. Accepted crate transfers call BroadcastSharedCrateSnapshot (line 1905) and accepted station commands call BroadcastStationSnapshot (line 2025); HandleMutationRequestMessage commits then BroadcastDelta to all remote clients (MultiplayerChunkAuthoritySync.cs:290-296, SendToRemoteClients 860-873). No token-bucket or per-tick cap exists on any path.
- **Recommended fix:** Add a per-client rate limiter (e.g. token bucket per connection per command kind) in the host dispatchers, dropping or disconnecting clients that exceed a sane cadence, and coalesce snapshot broadcasts so a burst of commands cannot fan out one snapshot per command to every peer.

### 5. Host re-broadcasts arbitrary client avatar-stream bytes into the Meta Avatar SDK deserializer on every peer

- **Severity:** Low  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs`, `Assets/Blockiverse/Scripts/MetaAvatars/BlockiverseMetaAvatarPresenter.cs`, `Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs`
- **Impact:** A malicious client can submit crafted avatar-stream bytes that the host relays unmodified to all other clients, where they are fed directly into OvrAvatarEntity.ApplyStreamData. If the Meta Avatar SDK does not fully validate stream input, this could crash or misbehave on remote peers.
- **Evidence:** SubmitAvatarStreamServerRpc re-stamps SenderClientId (good) but performs no payload inspection and immediately calls ReceiveAvatarStreamClientRpc (MetaAvatarStreamRelay.cs:65-71); the client handler passes the raw bytes to remotePresenter.ApplyRemoteStream (lines 74-81) -> BlockiverseMetaAvatarPresenter.ApplyRemoteStream (line 99-108) -> MetaHorizonAvatarProvider.ApplyStreamData -> `avatarEntity.ApplyStreamData(streamData)` (MetaHorizonAvatarProvider.cs:116). The payload is size-clamped to 64 KiB (MetaAvatarStreamMessage.cs:9/27-29) but its contents are never validated before reaching the third-party SDK.
- **Recommended fix:** Treat relayed avatar bytes as untrusted: wrap ApplyStreamData in a try/catch so a malformed payload from one peer cannot crash others, and if the Meta SDK exposes any stream-validation entry point, gate inbound streams on it. Confirm on-device how OvrAvatarEntity.ApplyStreamData behaves on corrupt input.

### 6. Unbounded growth of stashedInventoriesByGuid across reconnect churn

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- **Impact:** A client repeatedly connecting and disconnecting with a fresh GUID each time leaves an orphaned Inventory in the host's stash map that is never reclaimed, slowly exhausting host memory over a long session.
- **Evidence:** HandleClientDisconnected stashes `stashedInventoriesByGuid[guid] = inventory` (MultiplayerSurvivalSync.cs:2650-2654) whenever a disconnecting client had sent a hello and had an inventory. Entries are only removed on a matching reclaim (`stashedInventoriesByGuid.Remove(guid, ...)`, line 2704). There is no TTL, size cap, or eviction, so unique-GUID reconnect cycles accumulate Inventory instances indefinitely. The connect path even guarantees an inventory exists via GetInventory(clientId) in HandleClientConnected (line 2589).
- **Recommended fix:** Bound the stash (cap count and/or expire entries after a timeout), and clear it on session stop alongside the other per-session maps cleared in ClearSessionState (lines 2598-2606).

### 7. Test-only scenes enabled in EditorBuildSettings and a stray scene committed at Assets root

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `ProjectSettings/EditorBuildSettings.asset`, `Assets/Blockiverse/Scenes/MultiplayerTest.unity`, `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity`
- **Impact:** If an APK is ever produced via Unity's default Build Settings rather than the project's build scripts, the MultiplayerTest scene would be packaged into the shipped app, increasing attack/QA surface. The orphan InitTestScene at the Assets root is repo hygiene debt.
- **Evidence:** EditorBuildSettings.asset lists `Assets/Blockiverse/Scenes/Boot.unity` (enabled) and `Assets/Blockiverse/Scenes/MultiplayerTest.unity` (enabled:1) in m_Scenes. The documented build entry points (BlockiverseBuildSmoke.cs:29 and :60) override `scenes = new[] { BlockiverseProject.BootScenePath }`, so the official APKs ship only Boot, but the editor build list still includes the test scene, and a stray InitTestScene...unity is committed at the Assets root.
- **Recommended fix:** Remove MultiplayerTest from the enabled EditorBuildSettings scene list (or mark it disabled) and delete the orphaned InitTestScene file so no build path can include test scenes.

## What Looks Good (12)

- Server-authoritative tool/inventory resolution: ResolveAuthoritativeTool ignores client-supplied item stacks and reads the equipped tool from the host-owned inventory slot (MultiplayerSurvivalSync.cs:1768-1774), and every ProcessHost* path validates against host state, so clients cannot forge tools, spoof inventory contents, or request arbitrary item grants.
- Survival worlds reject raw client block mutations through the creative channel, forcing edits through the validated survival command path (MultiplayerChunkAuthoritySync.cs:279-289, GameModeForbidsDirectMutation).
- BlockMutationAuthority bounds-checks every mutation position and rejects unknown block ids (Voxel/ChunkAuthority.cs:267-278), preventing out-of-bounds writes from creative clients.
- Avatar stream relay re-stamps SenderClientId server-side, preventing identity spoofing of avatar frames (MetaAvatarStreamRelay.cs:68-70).
- MetaAvatarStreamMessage clamps payload length to 64 KiB on both read and write, bounding that allocation path (MetaAvatarStreamMessage.cs:27-42) - the model the string reads should follow.
- Save path handling is trust-rooted: TryResolveTrustedSavePath rejects relative paths and any path escaping persistentDataPath or the temp root, defeating path traversal (MultiplayerWorldPersistence.cs:465-499), and multiplayer saves are host-authority-gated (TryEnsureHostSaveAuthority).
- Save loading uses Unity JsonUtility (no polymorphic type resolution / no deserialization RCE) and validates inventory bounds, palette indices, and stack counts (WorldSaveService.cs:601-611, 1134-1175).
- No hardcoded secrets: keystore credentials come exclusively from environment variables / GitHub Actions secrets (BlockiverseBuildSmoke.cs:79-91, .github/workflows/release-apk.yml:30-51) and scripts/ci/forbidden-files.sh blocks committing .jks/.keystore/.p12/.env files; ProjectSettings carries no keystore password.
- AndroidManifest is minimal and sets android:allowBackup="false", preventing adb-backup extraction of saves/PlayerPrefs; no over-broad dangerous permissions are requested.
- Meta Platform access token is forwarded to OvrAvatarEntitlement.SetAccessToken and never logged (MetaHorizonAvatarProvider.cs:246).
- WorldEditService enforces fill/replace/copy volume caps (WorldEditService.cs:41-101), and the 'infinite' world-size option is mapped to bounded 256x256 dimensions (BlockiverseWorldSessionController.cs:334), avoiding allocation blowups.
- NGO 2.11.2 wraps named-message/RPC handler invocation in try/catch (NetworkMessageManager.cs:420-427), so ordinary truncated-message OverflowExceptions are caught rather than crashing the host.

## Could Not Review (4)

- On-device (Quest 3) behavior of the oversized-string allocation: whether IL2CPP/GC throws a catchable OutOfMemoryException versus aborting the process or tripping the Android low-memory killer. This determines whether the deserialization DoS is High or Critical and requires runtime testing.
- Whether the Meta Avatar SDK (OvrAvatarEntity.ApplyStreamData) internally validates relayed stream bytes; the SDK source was not in scope, so the avatar-payload trust finding needs on-device confirmation with malformed input.
- Actual transport-layer maximum message/fragment sizes negotiated by com.unity.transport 2.7.3 at runtime were not exercised; the analysis assumes a small malicious packet is accepted and reaches the handler, which is consistent with the named-message code path but not runtime-verified.
- Dynamic runtime wiring of the network/survival components in Boot.unity (serialized references actually assigned at play time) was not exhaustively traced through the ~5K-line bootstrapper; review focused on the message-handling logic rather than full scene-graph wiring.

## Inspected (26)

- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`
- `Assets/Blockiverse/Scripts/Gameplay/WorldEditService.cs`
- `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs`
- `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamMessage.cs`
- `Assets/Blockiverse/Scripts/MetaAvatars/BlockiverseMetaAvatarPresenter.cs`
- `Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs`
- `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkSession.cs`
- `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkConfig.cs`
- `Assets/Blockiverse/Scripts/Voxel/ChunkAuthority.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseSettingsPersistence.cs`
- `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs (network setup ~1233-1254)`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs`
- `Assets/Plugins/Android/AndroidManifest.xml`
- `ProjectSettings/ProjectSettings.asset (permissions/keystore)`
- `ProjectSettings/EditorBuildSettings.asset`
- `Assets/Blockiverse/Prefabs/Networking/BlockiverseNetworkManager.prefab`
- `Packages/manifest.json`
- `.github/workflows/release-apk.yml`
- `scripts/unity/build-release-apk.sh`
- `scripts/ci/forbidden-files.sh`
- `Library/PackageCache/com.unity.netcode.gameobjects@beeaefb722f7/Runtime/Serialization/FastBufferReader.cs`
- `Library/PackageCache/com.unity.netcode.gameobjects@beeaefb722f7/Runtime/Messaging/NetworkMessageManager.cs`
- `Library/PackageCache/com.unity.netcode.gameobjects@beeaefb722f7/Runtime/Messaging/Messages/NamedMessage.cs`
