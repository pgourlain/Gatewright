# ADR 0011: D11 — TLS may be terminated upstream or by Gatewright

- Status: Accepted
- Date: 2026-11-04

## Context

Deployments on Azure often terminate public TLS in Front Door or Application Gateway.

## Decision

Gatewright supports upstream termination with trusted forwarded headers, and terminates TLS itself with SNI and hot certificate reload.

## Consequences

Both topologies are supported; trusted proxy configuration becomes security relevant.
