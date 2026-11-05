# ADR 0007: D7 — Policies are compiled when the configuration loads

- Status: Accepted
- Date: 2026-11-04

## Context

Per-request interpretation of policy configuration would hurt latency and allocations.

## Decision

Each policy configuration is validated and compiled into an `IPolicyInstance` once per snapshot; nothing is interpreted per request.

## Consequences

Fast hot path; invalid configuration is rejected before reaching the data plane.
