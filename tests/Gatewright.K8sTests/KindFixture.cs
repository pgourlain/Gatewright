// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using k8s;

namespace Gatewright.K8sTests;

/// <summary>Marks a test that needs a Kubernetes cluster (kind). Opt-in: set <c>GATEWRIGHT_K8S_TESTS=1</c>.</summary>
public sealed class K8sFactAttribute : FactAttribute
{
    public K8sFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GATEWRIGHT_K8S_TESTS") != "1")
        {
            Skip = "Kubernetes tests are opt-in: set GATEWRIGHT_K8S_TESTS=1 (requires kind, kubectl and Docker).";
        }
    }
}

/// <summary>Provides a kind cluster (created when missing, left running so that reruns are fast) and a kubectl helper.</summary>
public sealed class KindFixture : IAsyncLifetime
{
    public const string ClusterName = "gatewright-test";
    public const string Context = "kind-" + ClusterName;

    public IKubernetes Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("GATEWRIGHT_K8S_TESTS") != "1")
        {
            return;
        }

        var clusters = await RunAsync("kind", "get", "clusters");
        if (!clusters.Split('\n', StringSplitOptions.RemoveEmptyEntries).Contains(ClusterName))
        {
            await RunAsync("kind", "create", "cluster", "--name", ClusterName, "--wait", "180s");
        }

        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(currentContext: Context);
        Client = new k8s.Kubernetes(config);
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        return Task.CompletedTask;
    }

    public static Task<string> KubectlAsync(string? stdin, params string[] args) => RunAsync("kubectl", stdin, ["--context", Context, .. args]);

    public static Task<string> RunAsync(string file, params string[] args) => RunAsync(file, null, args);

    public static async Task<string> RunAsync(string file, string? stdin, string[] args)
    {
        var start = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{file} {string.Join(' ', args)} failed ({process.ExitCode}): {error}");
        }

        return output;
    }
}

[CollectionDefinition(Name)]
public sealed class KindTestGroup : ICollectionFixture<KindFixture>
{
    public const string Name = "kind";
}
