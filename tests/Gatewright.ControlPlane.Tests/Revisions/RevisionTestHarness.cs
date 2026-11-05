// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Validation;
using Microsoft.Extensions.Time.Testing;

namespace Gatewright.ControlPlane.Tests.Revisions;

/// <summary>A database plus the revision services wired against it.</summary>
internal sealed class RevisionTestHarness : IDisposable
{
    private readonly TestDatabase _database = new();

    public RevisionTestHarness()
    {
        Db = _database.CreateContext();
        var registry = new PolicyRegistry([]);
        var translator = new SnapshotTranslator(new GatewayConfigValidator(registry), new PolicyCompiler(registry, new NoServices()));
        Service = new RevisionService(Db, new ConfigExporter(Db), translator, Time);
    }

    public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    public GatewrightDbContext Db { get; }

    public RevisionService Service { get; }

    public ApiEntity AddApi(string name, string basePath = "", string backend = "http://backend")
    {
        var api = new ApiEntity
        {
            Name = name,
            BasePath = basePath.Length == 0 ? "/" + name : basePath,
            Backend = new BackendDefinition { Destinations = [backend] },
        };
        Db.Apis.Add(api);
        Db.SaveChanges();
        return api;
    }

    public void Dispose()
    {
        Db.Dispose();
        _database.Dispose();
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
