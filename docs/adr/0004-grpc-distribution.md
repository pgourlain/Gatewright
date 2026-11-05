# ADR 0004: D4 — Configuration distribution over a custom gRPC protocol

- Status: Accepted
- Date: 2026-11-04

## Context

xDS is powerful but heavy and hard to evolve from C#.

## Decision

The control plane streams versioned snapshots and deltas to data planes over a dedicated gRPC protocol (in-memory channel in mode `all`).

## Consequences

Easy to evolve; we own the protocol versioning and compatibility matrix (N-1 data plane supported).
