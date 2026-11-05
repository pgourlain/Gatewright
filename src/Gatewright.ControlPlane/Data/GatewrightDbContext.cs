// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Domain;
using Gatewright.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Gatewright.ControlPlane.Data;

/// <summary>The control plane database: draft configuration, revisions, publications and audit trail. Provider agnostic.</summary>
public sealed class GatewrightDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="GatewrightDbContext"/> class.</summary>
    /// <param name="options">The context options.</param>
    public GatewrightDbContext(DbContextOptions<GatewrightDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the environments.</summary>
    public DbSet<GatewayEnvironment> Environments => Set<GatewayEnvironment>();

    /// <summary>Gets the APIs.</summary>
    public DbSet<ApiEntity> Apis => Set<ApiEntity>();

    /// <summary>Gets the operations.</summary>
    public DbSet<OperationEntity> Operations => Set<OperationEntity>();

    /// <summary>Gets the public hostnames.</summary>
    public DbSet<HostnameEntity> Hostnames => Set<HostnameEntity>();

    /// <summary>Gets the products.</summary>
    public DbSet<ProductEntity> Products => Set<ProductEntity>();

    /// <summary>Gets the plans.</summary>
    public DbSet<PlanEntity> Plans => Set<PlanEntity>();

    /// <summary>Gets the consumers.</summary>
    public DbSet<ConsumerEntity> Consumers => Set<ConsumerEntity>();

    /// <summary>Gets the subscriptions.</summary>
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();

    /// <summary>Gets the subscription keys.</summary>
    public DbSet<SubscriptionKey> SubscriptionKeys => Set<SubscriptionKey>();

    /// <summary>Gets the shared policies.</summary>
    public DbSet<SharedPolicyEntity> SharedPolicies => Set<SharedPolicyEntity>();

    /// <summary>Gets the global and gateway policy sets.</summary>
    public DbSet<ScopePoliciesEntity> ScopePolicies => Set<ScopePoliciesEntity>();

    /// <summary>Gets the revisions.</summary>
    public DbSet<Revision> Revisions => Set<Revision>();

    /// <summary>Gets the publications.</summary>
    public DbSet<Publication> Publications => Set<Publication>();

    /// <summary>Gets the data plane nodes known to the control plane.</summary>
    public DbSet<NodeStatusEntity> Nodes => Set<NodeStatusEntity>();

    /// <summary>Gets the discovery state (a single row).</summary>
    public DbSet<DiscoveryStateEntity> DiscoveryStates => Set<DiscoveryStateEntity>();

    /// <summary>Gets the automatic OpenAPI import records.</summary>
    public DbSet<AutoImportRecord> AutoImports => Set<AutoImportRecord>();

    /// <summary>Gets the audit trail.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <inheritdoc/>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Stored as UTC ticks: sortable on every provider (SQLite cannot order DateTimeOffset text).
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<BackendDefinition>().HaveConversion<JsonConverter<BackendDefinition>>();
        configurationBuilder.Properties<PolicySet>().HaveConversion<JsonConverter<PolicySet>>();
        configurationBuilder.Properties<PolicyReference>().HaveConversion<JsonConverter<PolicyReference>>();
        configurationBuilder.Properties<CertificateSource>().HaveConversion<JsonConverter<CertificateSource>>();
        configurationBuilder.Properties<HstsDefinition>().HaveConversion<JsonConverter<HstsDefinition>>();
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<GatewayEnvironment>(b =>
        {
            b.HasIndex(e => e.Name).IsUnique();
            b.Property(e => e.Name).HasMaxLength(64);
        });

        modelBuilder.Entity<ApiEntity>(b =>
        {
            b.HasIndex(a => a.Name).IsUnique();
            b.Property(a => a.Name).HasMaxLength(128);
            b.HasMany(a => a.Operations).WithOne().HasForeignKey(o => o.ApiId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(a => a.Products).WithMany(p => p.Apis).UsingEntity("ProductApi");
        });

        modelBuilder.Entity<OperationEntity>(b =>
        {
            b.HasIndex(o => new { o.ApiId, o.Name }).IsUnique();
            b.Property(o => o.Name).HasMaxLength(128);
        });

        modelBuilder.Entity<ProductEntity>(b =>
        {
            b.HasIndex(p => p.Name).IsUnique();
            b.Property(p => p.Name).HasMaxLength(128);
            b.HasMany(p => p.Plans).WithOne().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlanEntity>(b => b.HasIndex(p => new { p.ProductId, p.Name }).IsUnique());

        modelBuilder.Entity<ConsumerEntity>(b => b.HasIndex(c => c.Name).IsUnique());

        modelBuilder.Entity<HostnameEntity>(b =>
        {
            b.HasIndex(h => h.Name).IsUnique();
            b.Property(h => h.Name).HasMaxLength(253);
        });

        modelBuilder.Entity<SubscriptionEntity>(b =>
        {
            b.HasIndex(s => s.Name).IsUnique();
            b.Ignore(s => s.ActiveKeys);
            b.HasOne<ConsumerEntity>().WithMany().HasForeignKey(s => s.ConsumerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProductEntity>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(s => s.Keys).WithOne().HasForeignKey(k => k.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubscriptionKey>(b => b.HasIndex(k => k.Hash).IsUnique());

        modelBuilder.Entity<SharedPolicyEntity>(b => b.HasIndex(p => p.Name).IsUnique());

        modelBuilder.Entity<Revision>(b => b.HasIndex(r => r.Number).IsUnique());

        modelBuilder.Entity<Publication>(b =>
        {
            b.HasIndex(p => new { p.EnvironmentId, p.Number }).IsUnique();
            b.HasOne<GatewayEnvironment>().WithMany().HasForeignKey(p => p.EnvironmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Revision>().WithMany().HasForeignKey(p => p.RevisionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NodeStatusEntity>(b =>
        {
            b.HasKey(n => n.NodeId);
            b.Property(n => n.NodeId).HasMaxLength(256);
            b.HasIndex(n => n.Environment);
        });

        modelBuilder.Entity<DiscoveryStateEntity>(b => b.HasKey(d => d.Id));

        modelBuilder.Entity<AutoImportRecord>(b =>
        {
            b.HasKey(r => r.ServiceRef);
            b.Property(r => r.ServiceRef).HasMaxLength(512);
        });

        modelBuilder.Entity<AuditEntry>(b => b.HasIndex(a => a.Timestamp));
    }

    private sealed class JsonConverter<T> : ValueConverter<T, string>
        where T : class
    {
        public JsonConverter()
            : base(v => GatewayJson.Serialize(v), v => GatewayJson.Deserialize<T>(v))
        {
        }
    }
}
