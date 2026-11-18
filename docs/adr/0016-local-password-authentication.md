# ADR 0016: Local username/password authentication for the admin portal and Admin API

- Status: Proposed (maintainer validation required)
- Date: 2026-11-18

## Context

CLAUDE.md §6 (Security) requires the Admin API and the portal to be protected by Entra ID (OIDC).
Some products (SigNoz, Grafana, Portainer…) also offer a built-in username/password login next to
OIDC, so a new installation can be tried and operated without an identity provider.

Today:

- The portal signs users in with OIDC (cookie + OpenID Connect) and forwards the user's access
  token to the Admin API (`TokenProvider.AccessToken`).
- The Admin API validates that token as a JWT from the configured OIDC issuer and maps roles
  (`Admin`, `Publisher`, `Reader`).
- The only way to try the portal without an identity provider is `Portal:Auth:Mode=Development`,
  which disables sign-in and makes every visitor an administrator.

A login form on the portal alone is not enough, because the Admin API (and the CLI / CI) need a
credential they can validate.

## Options

### A. The control plane is also a token issuer (recommended)

- New `LocalUser` entity in the control plane database (username, password hash, role, disabled
  flag, failed-attempt counter, last sign-in), with EF migrations for SQLite and PostgreSQL.
- `POST /auth/login` verifies the password and returns a short-lived JWT signed by a local key
  (published on a JWKS endpoint, rotated). Optional refresh token.
- The Admin API accepts two issuers: the configured OIDC one and the local one. Roles come from
  the same claim in both cases.
- The portal gets a login page and a second authentication scheme that obtains the local JWT and
  keeps it in `TokenProvider`, exactly like the OIDC access token today.
- The CLI and CI can use the same endpoint (or a long-lived token for a service account).

### B. Portal-only cookie login with a service identity towards the Admin API

- Users are stored and checked by the portal, which calls the Admin API with an internal
  credential and passes the user and roles along.
- Simpler, but the Admin API trusts the portal for identity, the CLI gets nothing, and a portal
  compromise yields full admin rights.

## Decision (proposed)

Option A, opt-in:

1. `ControlPlane:Auth:Local:Enabled` (default `false`). When disabled, behaviour is unchanged.
2. Passwords hashed with a memory-hard KDF (Argon2id, or PBKDF2 via ASP.NET Core `PasswordHasher`
   if no MIT/Apache-2.0 Argon2 library is acceptable). No plaintext secret in config or database.
3. First administrator created at bootstrap from a secret reference (Kubernetes Secret / Key
   Vault), never from a default password. The Helm chart exposes it as a value backed by a Secret.
4. Login protection: constant-time comparison, rate limit per username and per address, lockout
   with back-off after repeated failures, audit entry for every sign-in, failure, password change
   and account change.
5. Local signing key stored through the existing secret-reference mechanism, with rotation
   (current + previous key published on the JWKS).
6. Portal: login page shown only when local auth is enabled; OIDC and local can be enabled
   together. `Portal:Auth:Mode=Development` stays a try-out switch.

## Consequences

- CLAUDE.md §6 Security must be amended: the Admin API and portal are protected by OIDC **or**
  local accounts, OIDC remaining the recommended production setup. This needs maintainer approval.
- The control plane gains a sensitive surface (credential storage, token issuance) that Entra ID
  currently carries for us. It needs a dedicated threat model section in `docs/user`.
- New entity and migrations, new Admin API endpoints, new portal pages and Helm values.
- Dependency check: any Argon2 or JWT-issuing library must pass the license list (MIT, Apache-2.0,
  BSD…).
- Out of scope: self-service sign-up, password reset by email, MFA (candidates for later), and
  external developer accounts (see the developer portal, phase 16).
