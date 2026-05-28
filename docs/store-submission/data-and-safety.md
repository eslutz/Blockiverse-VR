# Data Use & Safety Declarations

> Source of truth for the Meta store "Data Use" / privacy questionnaire answers. Keep this in
> sync with `privacy-policy.md` and the actual shipped behavior. Update whenever a new data
> flow or third-party integration is added.

## Data collection summary

| Data type | Collected? | Transmitted off device? | Purpose | Notes |
| --- | --- | --- | --- | --- |
| Account / platform identifiers | Via Meta only | Handled by Meta | Avatars, multiplayer connect | Provided by Meta platform SDK |
| World saves & inventory | Yes (local) | No | Game progression | Stored on device |
| Comfort / settings preferences | Yes (local) | No | User preferences | Stored on device |
| LAN session / IP data | Yes (transient) | Peer-to-peer on local network | Host/join a session | Not persisted after session |
| Diagnostic logs | Yes (local) | No (not auto-uploaded) | Debugging | Stored on device |
| Precise location | No | No | — | — |
| Advertising identifiers | No | No | — | No ads SDK |
| Third-party analytics | No | No | — | None integrated |

## Third-party services

- **Meta platform SDK** (Horizon avatars, party chat, store services). Governed by Meta's
  privacy policy. No other third-party SDKs are integrated.

## Voice / communications

- No in-app voice chat. Voice uses Meta Quest party chat (out of app, governed by Meta).
- No in-app text chat between players.

## Child safety

- App is not directed at users below the minimum age required by Meta's platform policies.
- No knowing collection of children's personal data.

## Security

- No personal data leaves the device except the minimum required by Meta platform features and
  transient LAN session data exchanged directly between players on the same local network.
- No signing keys, credentials, or secrets are stored in the app package or repository.

## Open declaration items (confirm at submission)

- [ ] Confirm the exact data categories Meta's current questionnaire requests.
- [ ] Confirm Horizon avatar data handling text matches Meta's required disclosures.
- [ ] Confirm age-rating answers align with these declarations.
