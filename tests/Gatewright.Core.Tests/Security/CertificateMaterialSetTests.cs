// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Security;

namespace Gatewright.Core.Tests.Security;

public sealed class CertificateMaterialSetTests
{
    [Fact]
    public void Hash_EmptySet_IsStable()
    {
        CertificateMaterialSet.Empty.Hash().ShouldBe(new CertificateMaterialSet(new Dictionary<string, CertificateMaterial>()).Hash());
    }

    [Fact]
    public void Hash_SameContent_IsEqual_RegardlessOfInputOrder()
    {
        var a = new CertificateMaterialSet(new Dictionary<string, CertificateMaterial>
        {
            ["ns/one"] = new("cert1", "key1"),
            ["ns/two"] = new("cert2", "key2"),
        });
        var b = new CertificateMaterialSet(new Dictionary<string, CertificateMaterial>
        {
            ["ns/two"] = new("cert2", "key2"),
            ["ns/one"] = new("cert1", "key1"),
        });

        a.Hash().ShouldBe(b.Hash());
    }

    [Fact]
    public void Hash_DifferentKeyMaterial_ChangesTheHash()
    {
        var a = new CertificateMaterialSet(new Dictionary<string, CertificateMaterial> { ["ns/one"] = new("cert", "key1") });
        var b = new CertificateMaterialSet(new Dictionary<string, CertificateMaterial> { ["ns/one"] = new("cert", "key2") });

        a.Hash().ShouldNotBe(b.Hash());
    }

    [Fact]
    public void Secrets_AreSortedByReference()
    {
        var set = new CertificateMaterialSet(new Dictionary<string, CertificateMaterial>
        {
            ["ns/zebra"] = new("c", "k"),
            ["ns/apple"] = new("c", "k"),
        });

        set.Secrets.Keys.ShouldBe(["ns/apple", "ns/zebra"]);
    }
}
