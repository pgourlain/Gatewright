# ADR 0013: Native AOT not pursued for V1

- Status: Accepted
- Date: 2026-11-05

## Context

Task 14.4 asked for an evaluation of Native AOT for the data plane: compatibility, memory and
startup gain, decided here rather than assumed.

Tried `dotnet publish src/Gatewright.Host -c Release -r osx-arm64 -p:PublishAot=true` for real
(not just reasoning about it) and found two separate blockers, in order of how early they bite:

1. **The AOT toolchain itself, today, fails this repository's own vulnerability gate.**
   `PublishAot=true` pulls in `Microsoft.DotNet.ILCompiler` 10.0.12 transitively, which NuGet
   audit flags with a high-severity advisory (GHSA-p3jp-9v83-76w9) — and `TreatWarningsAsErrors`
   plus the dependency-license/vulnerability policy (CLAUDE.md §7, task 0.10) turn that into a
   hard restore failure, same as a disallowed license would. Publishing AOT at all currently
   means carving out an exception to a policy adopted specifically to stop exactly this kind of
   thing from slipping through silently.
2. **Even past that, `Gatewright.Core` alone — no EF Core, no gRPC, no YARP yet — does not compile
   under AOT.** `GatewayJson`/`GatewayConfigSerializer` call `JsonSerializer.Serialize`/
   `Deserialize` with a plain `JsonSerializerOptions` (reflection-based `DefaultJsonTypeInfoResolver`,
   a non-generic `JsonStringEnumConverter`): 11 `IL2026`/`IL3050` errors, before the build ever
   reaches `Gatewright.Policies`, `Gatewright.DataPlane` or `Gatewright.ControlPlane` to find out
   what else is waiting there (YARP's own trim-compatibility, `Microsoft.IdentityModel`'s JWT/JWKS
   handling, `Grpc.Net.Client`, `StackExchange.Redis`, and EF Core / `JsonSchema.Net` — those two
   only matter for a single combined binary, see below, but are known historically AOT-unfriendly
   in their own right).

Fixing finding 2 for `Gatewright.Core` alone is mechanical (source-generated
`JsonSerializerContext`, `JsonStringEnumConverter<T>`) and would not be hard by itself. The deeper
problem is architectural, not a missing attribute:

- **D10** (one image for control plane and data plane, so the control plane can validate against
  exactly the schemas the data plane runs) means an AOT evaluation cannot stop at "is the data
  plane AOT-compatible" — the same compiled binary also carries the control plane's EF Core
  migrations and Admin API, which are a second, larger AOT-compatibility problem of their own.
  AOT-compiling only a conceptually-"data-plane" subset would mean a second binary flavor,
  contradicting the reason D10 exists.
- **D9's distribution model** has a customer write a plain POCO for their policy's config and get
  its JSON Schema for free via reflection (`JsonSchemaExporter`, task 9.1) — this is the whole
  point of the SDK ergonomics ("a customer installs Gatewright ... and adds their own policies
  through a NuGet SDK"). That reflection-based schema generation runs on *every customer's own
  arbitrary config type*, decided by them, not by this codebase. Making that AOT-safe would mean
  either asking every customer to hand-write source-generated serialization for their own policy
  config (a real ergonomics regression on the extensibility story this project is built around) or
  Gatewright generating that boilerplate for them — a non-trivial source-generator of its own.

So this is not "a few `[DynamicDependency]` attributes away." It is a genuine trade-off between
Native AOT and two decisions already taken deliberately (D9, D10).

## Decision

Do not pursue Native AOT for V1. Keep the standard JIT-compiled, `ReadyToRun`-eligible publish
(already the case via `dotnet publish -t:PublishContainer`, task 13.1) as the only supported
deployment. Startup time and memory are not currently a measured problem: `/healthz` answers
within the container's existing startup budget (phase 2/13 journal entries), and the gain AOT
would bring (lower baseline memory, faster cold start) matters most for scale-to-zero or very
short-lived instances, neither of which is this project's deployment model — the data plane is
long-running, horizontally scaled, kept warm (D1: stateless, unlimited horizontal scaling, but
scaling *out*, not *to zero*).

Revisit only if a concrete need appears that the current model does not already serve — for
example a customer wanting Gatewright as a short-lived sidecar or a serverless-style deployment
where cold start dominates — and treat it then as what it actually is: a new, reduced-scope
data-plane-only distribution shape (dropping D10's single-image assumption for that shape, and
requiring customers extending it to write AOT-safe policy config types themselves), not a flag
flipped on the existing one.

## Consequences

- No Native AOT work in V1; `docs/user` and the Helm chart make no AOT-related claims.
- The vulnerability-gate finding (point 1 above) is worth tracking independently of this
  decision: `Microsoft.DotNet.ILCompiler` 10.0.12's advisory may be fixed in a later SDK patch,
  which would remove that specific blocker without changing the architectural one.
- If AOT is revisited later for a reduced-scope data-plane-only shape, it needs its own ADR: it
  would reopen D10 for that shape and likely require a source-generated-config convention in
  `Gatewright.Sdk` for custom policies, which is new public API surface, not a packaging change.
