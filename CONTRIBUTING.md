# Contributing to Gatewright

Thanks for your interest! This document explains how to build, test and submit changes.

## Prerequisites

- .NET 10 SDK
- Docker (integration tests use Testcontainers)
- Optional: `kind` and `helm` for Kubernetes tests

## Build and test

```bash
dotnet build Gatewright.slnx
dotnet test Gatewright.slnx
dotnet format Gatewright.slnx --verify-no-changes
./scripts/check-headers.sh
```

## Conventions

- **Language**: code, identifiers, XML comments and commit messages in English.
- **Commits**: [Conventional Commits](https://www.conventionalcommits.org/) (`feat(dataplane): ...`).
- **DCO**: every commit must be signed off (`git commit -s`), which adds a `Signed-off-by:` line. See <https://developercertificate.org/>.
- **License header**: every source file starts with
  ```
  // Copyright (c) Gatewright contributors.
  // SPDX-License-Identifier: MIT
  ```
  The compiler enforces it for C# (IDE0073); `scripts/check-headers.sh` checks the other file types.
- **Naming of tests**: `Method_Scenario_ExpectedResult`.
- **Dependencies**: only MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, MS-PL, 0BSD and Unlicense licenses are accepted. CI fails otherwise.
- **Public API**: `Gatewright.Abstractions` and `Gatewright.Sdk` follow strict SemVer and are tracked by PublicApiAnalyzers.
- Never disable a test, analyzer or warning to make a build pass.

## Changelog

Every change with a user-visible effect (public API, configuration, CLI, Admin API, chart, policies) adds a line under
`## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md), written for users, in English, with the task reference
(for example `- JWT policy with background JWKS refresh (#3.4)`). Purely internal changes (refactoring, tests) are not listed.
Breaking changes are prefixed with `**BREAKING:**` and come with a migration note. CI fails when a pull request touches
`src/**`, `deploy/**` or `templates/**` without touching the changelog, unless it carries the `skip-changelog` label.

## Pull requests

1. Fork and create a topic branch.
2. Keep changes small and focused; do not reformat unrelated code.
3. Add tests and update the docs and the changelog.
4. Fill in the pull request template checklist.

Architectural decisions that contradict an existing ADR need a new ADR in [docs/adr](docs/adr).

## Conduct

By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). Report vulnerabilities as described in [SECURITY.md](SECURITY.md).
