# ADR 0006: D6 — Phased policy pipeline with scope inheritance

- Status: Accepted
- Date: 2026-11-04

## Context

A familiar model reduces the learning curve and enables per-customer differentiation.

## Decision

Policies run in phases (`Inbound`, `Backend`, `Outbound`, `OnError`) and inherit through scopes `global → gateway → product → API → operation` with `inherit`, `override` and `remove`.

## Consequences

Predictable behavior similar to Azure API Management; effective policy chains must be resolvable and displayable.
