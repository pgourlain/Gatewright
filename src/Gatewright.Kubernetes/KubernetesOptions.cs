// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Kubernetes;

/// <summary>Kubernetes discovery settings, bound from <c>Kubernetes</c>. Only the control plane reads the Kubernetes API.</summary>
public sealed class KubernetesOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Kubernetes";

    /// <summary>Gets or sets a value indicating whether Kubernetes discovery is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the kubeconfig path for out-of-cluster use; in-cluster credentials are used when empty.</summary>
    public string? KubeConfig { get; set; }

    /// <summary>Gets or sets the kubeconfig context to use.</summary>
    public string? KubeContext { get; set; }

    /// <summary>Gets or sets the annotation prefix.</summary>
    public string AnnotationPrefix { get; set; } = "gatewright.io/";

    /// <summary>Gets or sets the namespace of the leader election Lease. Defaults to the namespace of the pod.</summary>
    public string? LeaseNamespace { get; set; }

    /// <summary>Gets or sets the Lease name.</summary>
    public string LeaseName { get; set; } = "gatewright-controlplane";

    /// <summary>Gets or sets the identity of this replica in the election. Defaults to the machine name plus a random suffix.</summary>
    public string? Identity { get; set; }

    /// <summary>Gets or sets how long a Lease is valid without renewal, in seconds.</summary>
    public int LeaseDurationSeconds { get; set; } = 15;

    /// <summary>Gets or sets how long the leader keeps trying to renew before giving up leadership, in seconds.</summary>
    public int RenewDeadlineSeconds { get; set; } = 10;

    /// <summary>Gets or sets how often candidates retry to acquire or renew the Lease, in seconds.</summary>
    public int RetryPeriodSeconds { get; set; } = 2;

    /// <summary>Gets or sets how long changes are collected before the state is written, in milliseconds.</summary>
    public int DebounceMilliseconds { get; set; } = 300;
}
