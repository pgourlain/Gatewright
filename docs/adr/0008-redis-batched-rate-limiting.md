# ADR 0008: D8 — Rate limiting with local buckets and Redis token batches

- Status: Accepted
- Date: 2026-11-04

## Context

Calling Redis for each request does not scale and makes Redis a hot spot.

## Decision

A local token bucket reserves batches of tokens from Redis through Lua scripts; strict Lua counters are used only for billable quotas.

## Consequences

Fewer than one Redis call per 20 requests under nominal load; a documented tolerance on the global limit; configurable fail-open or fail-closed.
