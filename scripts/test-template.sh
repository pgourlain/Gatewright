#!/usr/bin/env bash
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
#
# Packs Gatewright locally, instantiates the 'gatewright-host' template from those packages, builds it and runs its tests.
set -euo pipefail

cd "$(dirname "$0")/.."
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
feed="$work/feed"

dotnet pack Gatewright.slnx -c Release -o "$feed" -p:ContinuousIntegrationBuild=false > "$work/pack.log"
version="$(ls "$feed"/Gatewright.Sdk.*.nupkg | head -n1 | sed -E 's/.*Gatewright\.Sdk\.(.*)\.nupkg/\1/')"
echo "Packed Gatewright $version"

dotnet new install "$PWD/templates/gatewright-host" --force > /dev/null
trap 'dotnet new uninstall "$PWD/templates/gatewright-host" > /dev/null 2>&1 || true; rm -rf "$work"' EXIT

project="$work/Contoso.Edge"
dotnet new gatewright-host -n Contoso.Edge -o "$project" --gatewrightVersion "$version" > /dev/null
cat > "$project/nuget.config" <<NUGET
<configuration>
  <packageSources>
    <clear />
    <add key="gatewright-local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
NUGET

cd "$project"
dotnet build > "$work/build.log" || { tail -30 "$work/build.log"; exit 1; }
dotnet test tests/Contoso.Edge.Tests
echo "Template OK"
