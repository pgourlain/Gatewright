// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Gatewright.ControlPlane.AdminApi;

public static partial class AdminEndpoints
{
    private static void MapCatalog(RouteGroupBuilder group)
    {
        MapApis(group);
        MapProducts(group);
        MapConsumers(group);
        MapHostnames(group);
        MapSharedPolicies(group);
    }

    private static IQueryable<ApiEntity> ApisWithChildren(GatewrightDbContext db) =>
        db.Apis.Include(a => a.Operations).Include(a => a.Products).AsSplitQuery();

    private static ApiResponse ToResponse(ApiEntity api) => new(
        api.Name,
        api.DisplayName,
        api.BasePath,
        api.Version,
        api.Backend,
        api.Policies,
        [.. api.Products.Select(p => p.Name).Order(StringComparer.Ordinal)],
        [.. api.Operations.OrderBy(o => o.Name, StringComparer.Ordinal).Select(o => new OperationResponse(o.Name, o.Method, o.Path, o.Policies))]);

    private static void MapApis(RouteGroupBuilder group)
    {
        group.MapGet("/apis", async (GatewrightDbContext db, CancellationToken ct) =>
            (await ApisWithChildren(db).AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct)).Select(ToResponse))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPost("/apis/import", async (
            HttpRequest request,
            ApiImportService importer,
            ChangeRecorder recorder,
            CancellationToken ct,
            string? name,
            string? basePath,
            string? service,
            bool? removeMissing,
            bool? dryRun) =>
        {
            using var reader = new StreamReader(request.Body);
            var document = await reader.ReadToEndAsync(ct);
            string[] destination = [.. request.Query["destination"].Where(v => v is not null).Select(v => v!)];
            string[] product = [.. request.Query["product"].Where(v => v is not null).Select(v => v!)];
            BackendDefinition? backend = null;
            if (service is not null || destination.Length > 0)
            {
                backend = new BackendDefinition { Service = service, Destinations = [.. destination] };
            }

            var report = await importer.ImportAsync(
                document,
                new Gatewright.Core.OpenApi.OpenApiImportOptions { Name = name, BasePath = basePath, Backend = backend, Products = product.Length > 0 ? product : null },
                removeMissing ?? true,
                dryRun ?? false,
                ct);
            if (report.Errors.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = [.. report.Errors] });
            }

            if (report.Applied)
            {
                await recorder.CommitAsync("api.import", "api", report.Api!, ct);
            }

            return Results.Ok(report);
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapGet("/apis/{name}", async (string name, GatewrightDbContext db, CancellationToken ct) =>
            await ApisWithChildren(db).AsNoTracking().FirstOrDefaultAsync(a => a.Name == name, ct) is { } api
                ? Results.Ok(ToResponse(api))
                : RequestValidation.NotFound("API", name))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPost("/apis", async (ApiRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var errors = ValidateApi(request);
            if (errors.Count > 0)
            {
                return RequestValidation.Problem(errors);
            }

            if (await db.Apis.AnyAsync(a => a.Name == request.Name, ct))
            {
                return RequestValidation.Conflict($"API '{request.Name}' already exists.");
            }

            var products = await ResolveProductsAsync(db, request.Products, ct);
            if (products is null)
            {
                return RequestValidation.Problem("products", "One or more products do not exist.");
            }

            var api = new ApiEntity
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                BasePath = request.BasePath,
                Version = request.Version,
                Backend = request.Backend,
                Policies = request.Policies ?? PolicySet.Empty,
            };
            api.Products.AddRange(products);
            db.Apis.Add(api);
            await recorder.CommitAsync("api.create", "api", api.Name, ct);
            return Results.Created($"{Prefix}/apis/{api.Name}", ToResponse(api));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapPut("/apis/{name}", async (string name, ApiRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var errors = ValidateApi(request with { Name = name });
            if (errors.Count > 0)
            {
                return RequestValidation.Problem(errors);
            }

            var api = await ApisWithChildren(db).FirstOrDefaultAsync(a => a.Name == name, ct);
            if (api is null)
            {
                return RequestValidation.NotFound("API", name);
            }

            var products = await ResolveProductsAsync(db, request.Products, ct);
            if (products is null)
            {
                return RequestValidation.Problem("products", "One or more products do not exist.");
            }

            api.DisplayName = request.DisplayName;
            api.BasePath = request.BasePath;
            api.Version = request.Version;
            api.Backend = request.Backend;
            api.Policies = request.Policies ?? PolicySet.Empty;
            api.Products.Clear();
            api.Products.AddRange(products);
            await recorder.CommitAsync("api.update", "api", name, ct);
            return Results.Ok(ToResponse(api));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapDelete("/apis/{name}", async (string name, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var api = await db.Apis.FirstOrDefaultAsync(a => a.Name == name, ct);
            if (api is null)
            {
                return RequestValidation.NotFound("API", name);
            }

            db.Apis.Remove(api);
            await recorder.CommitAsync("api.delete", "api", name, ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapGet("/apis/{api}/operations", async (string api, GatewrightDbContext db, CancellationToken ct) =>
            await ApisWithChildren(db).AsNoTracking().FirstOrDefaultAsync(a => a.Name == api, ct) is { } found
                ? Results.Ok(ToResponse(found).Operations)
                : RequestValidation.NotFound("API", api))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPut("/apis/{api}/operations/{name}", async (string api, string name, OperationRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            RequestValidation.CheckName("name", name, errors);
            if (string.IsNullOrEmpty(request.Path) || request.Path[0] != '/')
            {
                errors["path"] = ["Path must start with '/'."];
            }

            if (!HttpMethodNames.Contains(request.Method ?? ""))
            {
                errors["method"] = ["Not a valid HTTP method."];
            }

            if (errors.Count > 0)
            {
                return RequestValidation.Problem(errors);
            }

            var owner = await db.Apis.Include(a => a.Operations).FirstOrDefaultAsync(a => a.Name == api, ct);
            if (owner is null)
            {
                return RequestValidation.NotFound("API", api);
            }

            var operation = owner.Operations.FirstOrDefault(o => o.Name == name);
            var created = operation is null;
            if (operation is null)
            {
                operation = new OperationEntity { ApiId = owner.Id, Name = name };
                db.Operations.Add(operation);
            }

            operation.Method = request.Method!.ToUpperInvariant();
            operation.Path = request.Path;
            operation.Policies = request.Policies ?? PolicySet.Empty;
            await recorder.CommitAsync(created ? "operation.create" : "operation.update", "operation", $"{api}/{name}", ct);
            var body = new OperationResponse(operation.Name, operation.Method, operation.Path, operation.Policies);
            return created ? Results.Created($"{Prefix}/apis/{api}/operations/{name}", body) : Results.Ok(body);
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapDelete("/apis/{api}/operations/{name}", async (string api, string name, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var operation = await db.Operations.FirstOrDefaultAsync(o => o.Name == name && db.Apis.Any(a => a.Id == o.ApiId && a.Name == api), ct);
            if (operation is null)
            {
                return RequestValidation.NotFound("Operation", $"{api}/{name}");
            }

            db.Operations.Remove(operation);
            await recorder.CommitAsync("operation.delete", "operation", $"{api}/{name}", ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminRoles.AdminPolicy);
    }

    private static readonly HashSet<string> HttpMethodNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE",
    };

    private static Dictionary<string, string[]> ValidateApi(ApiRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        RequestValidation.CheckName("name", request.Name, errors);
        if (string.IsNullOrEmpty(request.BasePath) || request.BasePath[0] != '/')
        {
            errors["basePath"] = ["Base path must start with '/'."];
        }

        return errors;
    }

    private static async Task<List<ProductEntity>?> ResolveProductsAsync(GatewrightDbContext db, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        var distinct = names.Distinct(StringComparer.Ordinal).ToList();
        var products = await db.Products.Where(p => distinct.Contains(p.Name)).ToListAsync(ct);
        return products.Count == distinct.Count ? products : null;
    }

    private static ProductResponse ToResponse(ProductEntity product) => new(
        product.Name,
        product.RequiresSubscription,
        product.Policies,
        [.. product.Apis.Select(a => a.Name).Order(StringComparer.Ordinal)],
        [.. product.Plans.Select(p => p.Name).Order(StringComparer.Ordinal)]);

    private static void MapProducts(RouteGroupBuilder group)
    {
        group.MapGet("/products", async (GatewrightDbContext db, CancellationToken ct) =>
            (await db.Products.AsNoTracking().Include(p => p.Apis).Include(p => p.Plans).AsSplitQuery().OrderBy(p => p.Name).ToListAsync(ct)).Select(ToResponse))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/products/{name}", async (string name, GatewrightDbContext db, CancellationToken ct) =>
            await db.Products.AsNoTracking().Include(p => p.Apis).Include(p => p.Plans).AsSplitQuery().FirstOrDefaultAsync(p => p.Name == name, ct) is { } product
                ? Results.Ok(ToResponse(product))
                : RequestValidation.NotFound("Product", name))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPost("/products", async (ProductRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            RequestValidation.CheckName("name", request.Name, errors);
            if (errors.Count > 0)
            {
                return RequestValidation.Problem(errors);
            }

            if (await db.Products.AnyAsync(p => p.Name == request.Name, ct))
            {
                return RequestValidation.Conflict($"Product '{request.Name}' already exists.");
            }

            var product = new ProductEntity { Name = request.Name, RequiresSubscription = request.RequiresSubscription, Policies = request.Policies ?? PolicySet.Empty };
            db.Products.Add(product);
            await recorder.CommitAsync("product.create", "product", product.Name, ct);
            return Results.Created($"{Prefix}/products/{product.Name}", ToResponse(product));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapPut("/products/{name}", async (string name, ProductRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var product = await db.Products.Include(p => p.Apis).Include(p => p.Plans).AsSplitQuery().FirstOrDefaultAsync(p => p.Name == name, ct);
            if (product is null)
            {
                return RequestValidation.NotFound("Product", name);
            }

            product.RequiresSubscription = request.RequiresSubscription;
            product.Policies = request.Policies ?? PolicySet.Empty;
            await recorder.CommitAsync("product.update", "product", name, ct);
            return Results.Ok(ToResponse(product));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapDelete("/products/{name}", async (string name, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Name == name, ct);
            if (product is null)
            {
                return RequestValidation.NotFound("Product", name);
            }

            if (await db.Subscriptions.AnyAsync(s => s.ProductId == product.Id, ct))
            {
                return RequestValidation.Conflict($"Product '{name}' still has subscriptions.");
            }

            db.Products.Remove(product);
            await recorder.CommitAsync("product.delete", "product", name, ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapGet("/products/{product}/plans", async (string product, GatewrightDbContext db, CancellationToken ct) =>
            await db.Products.AsNoTracking().Include(p => p.Plans).FirstOrDefaultAsync(p => p.Name == product, ct) is { } found
                ? Results.Ok(found.Plans.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new PlanResponse(p.Name, p.Description)))
                : RequestValidation.NotFound("Product", product))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPut("/products/{product}/plans/{name}", async (string product, string name, PlanRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            RequestValidation.CheckName("name", name, errors);
            if (errors.Count > 0)
            {
                return RequestValidation.Problem(errors);
            }

            var owner = await db.Products.Include(p => p.Plans).FirstOrDefaultAsync(p => p.Name == product, ct);
            if (owner is null)
            {
                return RequestValidation.NotFound("Product", product);
            }

            var plan = owner.Plans.FirstOrDefault(p => p.Name == name);
            var created = plan is null;
            if (plan is null)
            {
                plan = new PlanEntity { ProductId = owner.Id, Name = name };
                db.Plans.Add(plan);
            }

            plan.Description = request.Description;
            await recorder.CommitAsync(created ? "plan.create" : "plan.update", "plan", $"{product}/{name}", ct);
            var body = new PlanResponse(plan.Name, plan.Description);
            return created ? Results.Created($"{Prefix}/products/{product}/plans/{name}", body) : Results.Ok(body);
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapDelete("/products/{product}/plans/{name}", async (string product, string name, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var plan = await db.Plans.FirstOrDefaultAsync(p => p.Name == name && db.Products.Any(x => x.Id == p.ProductId && x.Name == product), ct);
            if (plan is null)
            {
                return RequestValidation.NotFound("Plan", $"{product}/{name}");
            }

            if (await db.Subscriptions.AnyAsync(s => s.PlanId == plan.Id, ct))
            {
                return RequestValidation.Conflict($"Plan '{name}' is used by subscriptions.");
            }

            db.Plans.Remove(plan);
            await recorder.CommitAsync("plan.delete", "plan", $"{product}/{name}", ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminRoles.AdminPolicy);
    }

    private static void MapConsumers(RouteGroupBuilder group)
    {
        group.MapGet("/consumers", async (GatewrightDbContext db, CancellationToken ct) =>
            (await db.Consumers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(c => new ConsumerResponse(c.Name, c.Contact)))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/consumers/{name}", async (string name, GatewrightDbContext db, CancellationToken ct) =>
            await db.Consumers.AsNoTracking().FirstOrDefaultAsync(c => c.Name == name, ct) is { } consumer
                ? Results.Ok(new ConsumerResponse(consumer.Name, consumer.Contact))
                : RequestValidation.NotFound("Consumer", name))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPost("/consumers", async (ConsumerRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            RequestValidation.CheckName("name", request.Name, errors);
            if (errors.Count > 0)
            {
                return RequestValidation.Problem(errors);
            }

            if (await db.Consumers.AnyAsync(c => c.Name == request.Name, ct))
            {
                return RequestValidation.Conflict($"Consumer '{request.Name}' already exists.");
            }

            db.Consumers.Add(new ConsumerEntity { Name = request.Name, Contact = request.Contact });
            await recorder.CommitAsync("consumer.create", "consumer", request.Name, ct);
            return Results.Created($"{Prefix}/consumers/{request.Name}", new ConsumerResponse(request.Name, request.Contact));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapPut("/consumers/{name}", async (string name, ConsumerRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Name == name, ct);
            if (consumer is null)
            {
                return RequestValidation.NotFound("Consumer", name);
            }

            consumer.Contact = request.Contact;
            await recorder.CommitAsync("consumer.update", "consumer", name, ct);
            return Results.Ok(new ConsumerResponse(consumer.Name, consumer.Contact));
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapDelete("/consumers/{name}", async (string name, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Name == name, ct);
            if (consumer is null)
            {
                return RequestValidation.NotFound("Consumer", name);
            }

            if (await db.Subscriptions.AnyAsync(s => s.ConsumerId == consumer.Id, ct))
            {
                return RequestValidation.Conflict($"Consumer '{name}' still has subscriptions.");
            }

            db.Consumers.Remove(consumer);
            await recorder.CommitAsync("consumer.delete", "consumer", name, ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminRoles.AdminPolicy);
    }

    private static void MapSharedPolicies(RouteGroupBuilder group)
    {
        group.MapGet("/policies", async (GatewrightDbContext db, CancellationToken ct) =>
            (await db.SharedPolicies.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct))
                .ToDictionary(p => p.Name, p => p.Definition))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapGet("/policies/{name}", async (string name, GatewrightDbContext db, CancellationToken ct) =>
            await db.SharedPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name, ct) is { } policy
                ? Results.Ok(policy.Definition)
                : RequestValidation.NotFound("Shared policy", name))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPut("/policies/{name}", async (string name, PolicyReference definition, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            RequestValidation.CheckName("name", name, errors);
            if (string.IsNullOrWhiteSpace(definition.Type) || definition.Ref is not null)
            {
                errors["type"] = ["A shared policy must declare a type and cannot reference another shared policy."];
            }

            if (errors.Count > 0)
            {
                return RequestValidation.Problem(errors);
            }

            var policy = await db.SharedPolicies.FirstOrDefaultAsync(p => p.Name == name, ct);
            var created = policy is null;
            if (policy is null)
            {
                policy = new SharedPolicyEntity { Name = name };
                db.SharedPolicies.Add(policy);
            }

            policy.Definition = definition;
            await recorder.CommitAsync(created ? "policy.create" : "policy.update", "policy", name, ct);
            return created ? Results.Created($"{Prefix}/policies/{name}", definition) : Results.Ok(definition);
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapDelete("/policies/{name}", async (string name, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var policy = await db.SharedPolicies.FirstOrDefaultAsync(p => p.Name == name, ct);
            if (policy is null)
            {
                return RequestValidation.NotFound("Shared policy", name);
            }

            db.SharedPolicies.Remove(policy);
            await recorder.CommitAsync("policy.delete", "policy", name, ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapGet("/scope-policies", async (GatewrightDbContext db, CancellationToken ct) =>
        {
            var scope = await db.ScopePolicies.AsNoTracking().FirstOrDefaultAsync(ct);
            return new ScopePoliciesDto { Global = scope?.Global ?? PolicySet.Empty, Gateway = scope?.Gateway ?? PolicySet.Empty };
        }).RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPut("/scope-policies", async (ScopePoliciesDto body, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var scope = await db.ScopePolicies.FirstOrDefaultAsync(ct);
            if (scope is null)
            {
                scope = new ScopePoliciesEntity();
                db.ScopePolicies.Add(scope);
            }

            scope.Global = body.Global;
            scope.Gateway = body.Gateway;
            await recorder.CommitAsync("scope-policies.update", "scope-policies", "global+gateway", ct);
            return Results.Ok(body);
        }).RequireAuthorization(AdminRoles.AdminPolicy);
    }

    private static void MapHostnames(RouteGroupBuilder group)
    {
        group.MapGet("/hostnames", async (GatewrightDbContext db, CancellationToken ct) =>
            (await db.Hostnames.AsNoTracking().OrderBy(h => h.Name).ToListAsync(ct))
                .Select(h => new HostnameResponse(h.Name, h.Certificate, h.RedirectHttpToHttps, h.Hsts)))
            .RequireAuthorization(AdminRoles.ReaderPolicy);

        group.MapPut("/hostnames/{name}", async (string name, HostnameRequest request, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var candidate = new HostnameDefinition
            {
                Name = name,
                Certificate = request.Certificate,
                RedirectHttpToHttps = request.RedirectHttpToHttps,
                Hsts = request.Hsts,
            };
            var errors = new Gatewright.Core.Validation.GatewayConfigValidator()
                .Validate(new GatewayConfig { Hostnames = [candidate] })
                .Where(e => e.Code.StartsWith("hostname.", StringComparison.Ordinal))
                .Select(e => $"{e.Code}: {e.Message}")
                .ToArray();
            if (errors.Length > 0)
            {
                return RequestValidation.Problem("hostname", string.Join(' ', errors));
            }

            var hostname = await db.Hostnames.FirstOrDefaultAsync(h => h.Name == name, ct);
            var created = hostname is null;
            if (hostname is null)
            {
                hostname = new HostnameEntity { Name = name };
                db.Hostnames.Add(hostname);
            }

            hostname.Certificate = request.Certificate;
            hostname.RedirectHttpToHttps = request.RedirectHttpToHttps;
            hostname.Hsts = request.Hsts;
            await recorder.CommitAsync(created ? "hostname.create" : "hostname.update", "hostname", name, ct);
            var body = new HostnameResponse(hostname.Name, hostname.Certificate, hostname.RedirectHttpToHttps, hostname.Hsts);
            return created ? Results.Created($"{Prefix}/hostnames/{name}", body) : Results.Ok(body);
        }).RequireAuthorization(AdminRoles.AdminPolicy);

        group.MapDelete("/hostnames/{name}", async (string name, GatewrightDbContext db, ChangeRecorder recorder, CancellationToken ct) =>
        {
            var hostname = await db.Hostnames.FirstOrDefaultAsync(h => h.Name == name, ct);
            if (hostname is null)
            {
                return RequestValidation.NotFound("Hostname", name);
            }

            db.Hostnames.Remove(hostname);
            await recorder.CommitAsync("hostname.delete", "hostname", name, ct);
            return Results.NoContent();
        }).RequireAuthorization(AdminRoles.AdminPolicy);
    }
}
