// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Discovery;
using Gatewright.Kubernetes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gatewright.K8sTests;

[Collection(KindTestGroup.Name)]
public sealed class KubernetesDiscoveryTests(KindFixture kind) : IAsyncLifetime
{
    private sealed class MemoryStore : IDiscoveryStore
    {
        private DiscoveryState _state = DiscoveryState.Empty;

        public DiscoveryState Current => Volatile.Read(ref _state);

        public Task SaveAsync(DiscoveryState state, CancellationToken cancellationToken)
        {
            Volatile.Write(ref _state, state);
            return Task.CompletedTask;
        }

        public Task<DiscoveryState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
    }

    private sealed class AlwaysLeader : ILeaderElection
    {
        public bool IsLeader => true;

        public Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken) => Task.FromResult(CancellationToken.None);
    }

    private readonly string _namespace = "gw-" + Guid.NewGuid().ToString("N")[..8];

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("GATEWRIGHT_K8S_TESTS") != "1")
        {
            return;
        }

        await KindFixture.KubectlAsync(null, "create", "namespace", _namespace);
    }

    public async Task DisposeAsync()
    {
        if (Environment.GetEnvironmentVariable("GATEWRIGHT_K8S_TESTS") == "1")
        {
            await KindFixture.KubectlAsync(null, "delete", "namespace", _namespace, "--wait=false");
        }
    }

    private string Manifest(string annotations) => $$"""
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: orders
          namespace: {{_namespace}}
        spec:
          replicas: 1
          selector:
            matchLabels: { app: orders }
          template:
            metadata:
              labels: { app: orders }
            spec:
              terminationGracePeriodSeconds: 1
              containers:
                - name: app
                  image: registry.k8s.io/pause:3.9
                  ports: [{ containerPort: 8080 }]
        ---
        apiVersion: v1
        kind: Service
        metadata:
          name: orders
          namespace: {{_namespace}}
          annotations:
        {{annotations}}
        spec:
          selector: { app: orders }
          ports:
            - port: 80
              targetPort: 8080
        ---
        apiVersion: v1
        kind: Service
        metadata:
          name: hidden
          namespace: {{_namespace}}
        spec:
          selector: { app: orders }
          ports:
            - port: 80
              targetPort: 8080
        """;

    private static async Task<bool> WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(250);
        }

        return condition();
    }

    [K8sFact]
    public async Task Discovery_FollowsAnnotatedServicesAndScalingWithoutLosingEndpoints()
    {
        var store = new MemoryStore();
        var options = Options.Create(new KubernetesOptions { Enabled = true, DebounceMilliseconds = 100 });
        using var discovery = new KubernetesDiscoveryService(kind.Client, new AlwaysLeader(), store, options, NullLogger<KubernetesDiscoveryService>.Instance);
        await discovery.StartAsync(CancellationToken.None);
        var reference = $"{_namespace}/orders:80";

        await KindFixture.KubectlAsync(Manifest($"""
              gatewright.io/expose: "true"
              gatewright.io/openapi-path: /swagger/v1/swagger.json
              gatewright.io/base-path: /orders
              gatewright.io/product: starter
        """), "apply", "-f", "-");

        (await WaitAsync(() => store.Current.Endpoints.For(reference).Count == 1, TimeSpan.FromSeconds(90))).ShouldBeTrue("one ready pod expected");
        var service = store.Current.Services.Single(s => s.Namespace == _namespace);
        service.Name.ShouldBe("orders");
        service.OpenApiPath.ShouldBe("/swagger/v1/swagger.json");
        service.BasePath.ShouldBe("/orders");
        service.Product.ShouldBe("starter");
        store.Current.Services.ShouldNotContain(s => s.Name == "hidden");

        await KindFixture.KubectlAsync(null, "-n", _namespace, "scale", "deployment/orders", "--replicas=3");
        (await WaitAsync(() => store.Current.Endpoints.For(reference).Count == 3, TimeSpan.FromSeconds(90))).ShouldBeTrue("three ready pods expected");

        await KindFixture.KubectlAsync(null, "-n", _namespace, "scale", "deployment/orders", "--replicas=1");
        (await WaitAsync(() => store.Current.Endpoints.For(reference).Count == 1, TimeSpan.FromSeconds(90))).ShouldBeTrue("back to one pod");

        await KindFixture.KubectlAsync(null, "-n", _namespace, "delete", "service", "orders");
        (await WaitAsync(() => store.Current.Services.All(s => s.Namespace != _namespace), TimeSpan.FromSeconds(30))).ShouldBeTrue("service removed");

        await discovery.StopAsync(CancellationToken.None);
    }
}
