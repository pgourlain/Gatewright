# ADR 0009: D9 — Extensibility through the distribution model

- Status: Accepted
- Date: 2026-11-04

## Context

Dynamic DLL loading is fragile (versioning, isolation, security).

## Decision

V1 extensibility is built-in policies plus .NET plugins compiled into a customer image via NuGet (SDK). CEL expressions come in V2, WASM later.

## Consequences

Robust and debuggable; customers own the build and image.
