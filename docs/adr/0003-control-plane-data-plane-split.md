# ADR 0003: D3 — Control plane / data plane split in a single binary

- Status: Accepted
- Date: 2026-11-04

## Context

The data plane must scale horizontally while development should stay simple.

## Decision

One binary with a mode selected by `Gateway:Mode` (`All`, `ControlPlane`, `DataPlane`).

## Consequences

Production scales the data plane independently; development runs everything in one process.
