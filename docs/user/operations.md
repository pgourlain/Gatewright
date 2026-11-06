# Operations

## Health and readiness

| Endpoint | Meaning |
|---|---|
| `GET /healthz` | Liveness: the process runs |
| `GET /readyz` | `200` once a valid configuration is applied and shutdown has not started; `503` otherwise |

A data plane pod stays unready until its first snapshot is applied. After that, losing the control plane **never** stops traffic: the pod keeps its last known good configuration (D5).

## Scaling

- **Data plane**: stateless. Scale with `dataPlane.replicaCount` or the HPA (`dataPlane.autoscaling.*`, CPU or requests per second). Measured on kind: scale-out 1→50 and scale-in 50→1 with no request errors, configuration propagation to 50 pods in about 2 s (`scripts/k8s-scale-test.sh`).
- **Control plane**: one replica with SQLite; several with PostgreSQL (leader election on a Kubernetes `Lease` runs discovery on a single replica).
- **Redis**: optional; carries batched shared rate limits, strict quotas and key-revocation pub/sub. With Redis down, `failOpen` / `failClosed` per rate limit decides (D8); everything else keeps working.

## Rolling updates and shutdown

On `SIGTERM` the pod fails `/readyz` immediately and waits `Gateway:Shutdown:DrainDelaySeconds` before stopping Kestrel, so load balancers stop sending new requests. The chart adds a `preStop` hook and a `PodDisruptionBudget`. Keep `terminationGracePeriodSeconds` above the drain delay plus your longest request.

## Monitoring

See [observability](observability.md): OTLP traces/metrics/logs, `gatewright.*` metrics, sampled access logs, usage analytics and ready-made Grafana dashboards and alert rules under `deploy/observability/`. Alert at least on: `/readyz` failing on more than one pod, `gatewright.config.version_applied` lagging behind the published version, 5xx ratio, `gatewright.rejections{cause="rate_limited"}` spikes, `gatewright.redis.*` errors.

## Backup and restore

All state that matters is in the control plane database: back up PostgreSQL (or the SQLite volume). Data planes hold nothing to back up. `gatewright config export` gives a portable, human-reviewable copy of one environment.

## Secrets and rotation

| Secret | Setting | Rotation |
|---|---|---|
| Data plane ↔ control plane token | `Distribution:Token` | Roll both deployments; reconnection is automatic |
| API key pepper | `Gateway:ApiKeyPepper` | Changing it invalidates every stored key hash: plan a re-issue |
| Diagnostics token | `Gateway:DiagnosticsToken` | Restart the pods |
| Subscription keys | portal / Admin API | Rotate or revoke; revocations reach all pods within seconds |
| TLS certificates | mounted files or a referenced Kubernetes Secret | Hot reloaded, no restart |

## Troubleshooting

| Symptom | Check |
|---|---|
| `404` for a published route | `gatewright nodes`: has the pod applied the version? `GET /gw/config` (diagnostics token) shows the live configuration |
| `401`/`403` from a policy | Send `x-gatewright-trace: <Gateway:DiagnosticsToken>` and read `x-gatewright-trace-result` for the per-policy outcome |
| `413` / `431` | A request exceeded `Gateway:Limits`; see [security](security.md#request-limits) |
| Pod never ready | Nothing published yet, or the control plane address/token is wrong: check the pod log for the distribution client |
| Rate limit not shared across pods | `shared: true` needs `Redis:ConnectionString` on every data plane |
