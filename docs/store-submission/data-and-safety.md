# Data Use & Safety Declarations

> Store-facing source for Meta "Data Use" / privacy questionnaire answers. Keep this in sync
> with the public privacy policy at `https://blockiversevr.com/privacy/`, the rulesets under
> `../rulesets/`, and the actual shipped behavior. Update whenever a new data flow or
> third-party integration is added.

## Data collection summary

| Data type | Collected? | Transmitted off device? | Purpose | Notes |
| --- | --- | --- | --- | --- |
| Account / platform identifiers | Via Meta only | Handled by Meta | Avatars, multiplayer connect | Provided by Meta platform SDK |
| User age category | Yes, category only | Requested from Meta Platform SDK | Mixed Ages platform feature gating | CH/TN/AD/UNKNOWN only; no birthdate; last known CH/TN/AD may be cached locally |
| World saves & inventory | Yes (local) | No | Game progression | Stored on device |
| Comfort / settings preferences | Yes (local) | No | User preferences | Stored on device |
| LAN session / IP data | Yes (transient) | Peer-to-peer on local network | Host/join a session | Not persisted after session |
| Canonical world state | Yes (local) | No | World persistence | Includes ruleset-defined environment, structure, vegetation, inventory, and save metadata |
| Diagnostic logs | Yes (local) | No (not auto-uploaded) | Debugging | Stored on device; development builds may include optional sanitized verbose gameplay trace files |
| Precise location | No | No | — | — |
| Advertising identifiers | No | No | — | No ads SDK |
| Third-party analytics | No | No | — | None integrated |

## Third-party services

- **Meta platform SDK** (User Age Group API, Horizon avatars, party chat, store services).
  Governed by Meta's privacy policy. No other third-party SDKs are integrated.

## Voice / communications

- No in-app voice chat. Voice uses Meta Quest party chat (out of app, governed by Meta).
- No in-app text chat between players.

## Child safety

- Mixed Ages builds request Meta's user age category once per online session and use
  `UNKNOWN`, offline, or failed responses without blocking the base game.
- Child accounts keep access to offline/LAN gameplay, but Blockiverse-owned Meta social,
  profile, and avatar lookup paths use fallback identity/avatar behavior unless current
  Meta policy review explicitly permits the feature.
- The app does not receive or store a birthdate, and it does not knowingly collect
  children's personal data beyond the Meta-provided category needed for platform policy
  handling.

## Security

- No personal data leaves the device except the minimum required by Meta platform features and
  transient LAN session data exchanged directly between players on the same local network.
- No signing keys, credentials, or secrets are stored in the app package or repository.

## Open declaration items (confirm at submission)

- [ ] Confirm the exact data categories Meta's current questionnaire requests.
- [ ] Confirm Horizon avatar data handling text matches Meta's required disclosures.
- [ ] Confirm age-rating answers align with these declarations.
- [ ] Confirm User Age Group API / Data Use Checkup answers match the Mixed Ages runtime behavior.
