# ADR 0002: Runtime Diagnostics Logging

## Status

Accepted

## Decision

Use a small local-only `BlockiverseLog` facade for runtime and editor diagnostics. The facade lives in Core so every assembly can use it without taking a new dependency. Development information logs are enabled only in the Unity Editor and development builds unless tests override that behavior. Warnings and errors always write to the local Unity/player log.

Do not add remote analytics, crash upload, log upload, or a third-party logging service for the Alpha logging foundation.

## Context

Alpha validation needs useful local diagnostics before the full M6 performance instrumentation backlog is implemented. Renderer, save/load, migration, and authored asset bootstrap failures should leave clear local evidence in Unity logs and Quest player logs without exposing private local machine paths or player-sensitive data.

## Consequences

Systems should log sanitized summaries instead of raw state dumps. Logs must not include full save paths, device identifiers, player identifiers, join codes, voice or chat content, secrets, or inventory contents beyond counts.

Quest validation captures use local `hzdb log` commands. Profiler markers, in-game stats panels, OVR Metrics captures, and formal performance reports remain separate M6 work.
