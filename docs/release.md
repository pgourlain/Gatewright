# Release procedure

Releases are cut by a maintainer and built by `.github/workflows/release.yml` when a `v*` tag is pushed. Versions follow [Semantic Versioning](https://semver.org/) and [compatibility](user/compatibility.md); the version of every package and image comes from the tag (MinVer).

## One-time setup

| What | Where | Notes |
|---|---|---|
| NuGet Trusted Publishing policy | nuget.org → *Trusted Publishing* | Repository owner/name, workflow file `release.yml`, environment `nuget` |
| Repository variable `NUGET_USER` | GitHub → Settings → Variables | The nuget.org profile name used by `NuGet/login`. Alternative: secret `NUGET_API_KEY` (scoped to `Gatewright.*`, push-only) |
| Environment `nuget` | GitHub → Settings → Environments | Add required reviewers so a tag alone cannot publish to NuGet.org |
| GHCR packages public | GitHub → Packages | `gatewright-host`, `gatewright-portal`, `charts/gatewright` |
| Signing key | local git config | `git config user.signingkey …` and `tag.gpgSign true` (GPG, SSH or gitsign) |
| Branch protection on `main` | GitHub | Required checks: CI, Licenses, DCO, Changelog |

## Prereleases (`-preview.N`, `-rc.N`)

Before 1.0 and for risky changes, tag a prerelease straight from `main`. The changelog is **not** rewritten: the release notes of a prerelease are the `[Unreleased]` section.

```bash
git switch main && git pull --ff-only
git tag -s v1.0.0-rc.1 -m "v1.0.0-rc.1"
git push origin v1.0.0-rc.1
```

The release is marked *pre-release* on GitHub, images get only the exact tag (no `latest`, no `X.Y`).

## Stable release

1. **Check main is green** (CI, Licenses, k8s e2e) and that every user-visible change has a `CHANGELOG.md` line. Breaking changes are prefixed `**BREAKING:**` with a migration note.
2. **Prepare the changelog** on a release branch:
   ```bash
   git switch -c release/1.0.0
   ./scripts/prepare-release.sh 1.0.0      # [Unreleased] -> [1.0.0] - <today>, new empty [Unreleased], comparison links
   git diff CHANGELOG.md                    # read it
   ./scripts/extract-changelog.sh 1.0.0     # this exact text becomes the GitHub Release body
   git commit -s -am "chore(release): 1.0.0"
   ```
   Open a pull request, merge it (DCO and Conventional Commits apply).
3. **Tag the merge commit** with a signed tag and push it:
   ```bash
   git switch main && git pull --ff-only
   git tag -s v1.0.0 -m "v1.0.0"
   git verify-tag v1.0.0
   git push origin v1.0.0
   ```
4. **Watch the workflow** (*Actions → Release*). Stages: `verify` (build, tests, format, license check, helm lint) → `packages`, `images`, `chart` in parallel → `nuget` (approval if required) → `release`.

## What the pipeline produces

| Artifact | Where |
|---|---|
| NuGet packages (`Gatewright.*`, `Gatewright.Templates`, tool `Gatewright.Cli`) + symbols | nuget.org and the release assets |
| Images `gatewright-host`, `gatewright-portal`, multi-arch (`linux/amd64`, `linux/arm64`), tags `X.Y.Z`, `X.Y`, `X`, `latest` (stable only), OCI labels (license, source, version, revision) | `ghcr.io/<owner>/` |
| SLSA build provenance attestation per image | attached to the image in GHCR |
| SBOM (CycloneDX JSON) per image | release assets |
| Helm chart `gatewright` (`version` and `appVersion` = release) | `oci://ghcr.io/<owner>/charts` |
| `THIRD-PARTY-NOTICES.md` regenerated from the resolved dependencies | release assets and inside the packages |
| Release body | the matching `CHANGELOG.md` section, extracted by `scripts/extract-changelog.sh` |

The workflow refuses a tag that is not a SemVer version, or whose changelog section is empty.

## Post-release checks

```bash
v=1.0.0
dotnet new install Gatewright.Templates::$v && dotnet new gatewright-host -n Smoke && (cd Smoke && dotnet build)
docker pull ghcr.io/<owner>/gatewright-host:$v && docker inspect ghcr.io/<owner>/gatewright-host:$v --format '{{json .Config.Labels}}'
gh attestation verify oci://ghcr.io/<owner>/gatewright-host:$v --owner <owner>
helm install smoke oci://ghcr.io/<owner>/charts/gatewright --version $v --dry-run
```

- The GitHub Release lists packages, chart, SBOMs and notices, and its body equals the changelog section.
- `dotnet tool install -g Gatewright.Cli --version $v` works.
- Announce (GitHub Discussions / release notes link).

## When something fails

- **Before `nuget`**: fix, delete the tag locally and remotely (`git push --delete origin v1.0.0`), delete any GHCR tag created, re-tag. Nothing is public on NuGet yet.
- **After `nuget`**: NuGet versions cannot be deleted, only unlisted. Do not reuse the version: release a patch (`v1.0.1`).
- Container images and chart tags are never overwritten for a stable version; publish a patch.

## Rehearsal (`v0.0.1-rc.1`)

To test the pipeline end to end, push the repository to a scratch GitHub repository (fork or private), leave `NUGET_USER`/`NUGET_API_KEY` unset (the `nuget` job only runs for the `pgourlain` owner, so a scratch repository skips it), and push `v0.0.1-rc.1`. Expect: GitHub prerelease with packages, chart, SBOMs and notices; images and chart in the scratch owner's GHCR.
