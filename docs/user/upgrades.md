# Upgrades

Gatewright follows [Semantic Versioning](https://semver.org/) (details in [compatibility](compatibility.md)). Read the `CHANGELOG.md` section of the target version first: breaking changes are prefixed `**BREAKING:**` with a migration note.

## Order

1. **Back up** the control plane database.
2. **Control plane first**, then the data planes. A data plane may run **one minor/major generation behind** the control plane (N-1), never ahead.
3. **Custom distribution?** Bump the `Gatewright.Sdk` package, rebuild your image, then roll it out in the same order.

With Helm:

```bash
helm upgrade gatewright deploy/helm/gatewright --namespace gatewright --reuse-values --set image.tag=<new>
```

The chart rolls the control plane and the data plane as separate Deployments; with `gateway.mode=all` both move together.

## Database migrations

The control plane applies its versioned EF Core migrations at startup (SQLite and PostgreSQL). Migrations are forward-only: to go back, restore the backup and redeploy the previous version. With several control plane replicas, upgrade one then the rest (rolling update does this), because an older replica may not understand a newer schema.

## Zero-downtime rolling update

Data plane pods drain on shutdown (`/readyz` fails first, then `Gateway:Shutdown:DrainDelaySeconds`), the chart ships a `preStop` hook and a `PodDisruptionBudget`. The scale test (`scripts/k8s-scale-test.sh`) exercises a rolling update under continuous load with zero request errors.

## Rolling back

- **Code**: `helm rollback gatewright`. Safe when the database schema did not change; otherwise restore the backup too.
- **Configuration**: independent of the code version. `gatewright rollback --env <e> --revision <n>`.

## Export format

`gatewright config export` documents carry `"format": "gatewright/v1"`. `config import` refuses any other format with an explicit message instead of silently dropping data. A future format will be introduced as `gatewright/v2` with a migration note, and both versions will be readable for at least one major version.
