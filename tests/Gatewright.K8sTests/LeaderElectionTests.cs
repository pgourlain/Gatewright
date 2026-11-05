// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Kubernetes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gatewright.K8sTests;

[Collection(KindTestGroup.Name)]
public sealed class LeaderElectionTests(KindFixture kind)
{
    private static LeaseLeaderElection Candidate(KindFixture kind, string leaseName, string identity) =>
        new(
            kind.Client,
            Options.Create(new KubernetesOptions
            {
                LeaseNamespace = "default",
                LeaseName = leaseName,
                Identity = identity,
                LeaseDurationSeconds = 6,
                RenewDeadlineSeconds = 4,
                RetryPeriodSeconds = 1,
            }),
            NullLogger<LeaseLeaderElection>.Instance);

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
    public async Task OnlyOneReplicaLeads_AndAnotherTakesOverWhenTheLeaderStops()
    {
        var leaseName = "gw-test-" + Guid.NewGuid().ToString("N")[..8];
        var a = Candidate(kind, leaseName, "replica-a");
        var b = Candidate(kind, leaseName, "replica-b");
        await a.StartAsync(CancellationToken.None);
        await b.StartAsync(CancellationToken.None);

        (await WaitAsync(() => a.IsLeader || b.IsLeader, TimeSpan.FromSeconds(30))).ShouldBeTrue("a leader should be elected");
        await Task.Delay(2000);
        (a.IsLeader ^ b.IsLeader).ShouldBeTrue("exactly one replica leads");

        var (leader, follower) = a.IsLeader ? (a, b) : (b, a);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var term = await leader.WaitForLeadershipAsync(cts.Token);
        term.IsCancellationRequested.ShouldBeFalse();

        await leader.StopAsync(CancellationToken.None);

        term.IsCancellationRequested.ShouldBeTrue("the leader's term ends when it stops");
        (await WaitAsync(() => follower.IsLeader, TimeSpan.FromSeconds(40))).ShouldBeTrue("the other replica takes over");

        await follower.StopAsync(CancellationToken.None);
        await KindFixture.KubectlAsync(null, "-n", "default", "delete", "lease", leaseName, "--ignore-not-found");
        a.Dispose();
        b.Dispose();
    }
}
