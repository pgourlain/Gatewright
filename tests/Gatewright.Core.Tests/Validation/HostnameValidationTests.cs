// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.Validation;

namespace Gatewright.Core.Tests.Validation;

public sealed class HostnameValidationTests
{
    private static string[] Codes(params HostnameDefinition[] hostnames) =>
        [.. new GatewayConfigValidator().Validate(new GatewayConfig { Hostnames = hostnames }).Select(e => e.Code)];

    private static CertificateSource Files() => new() { File = new CertificateFiles("/tls/tls.crt", "/tls/tls.key") };

    [Theory]
    [InlineData("api.example.com")]
    [InlineData("*.example.com")]
    [InlineData("*")]
    [InlineData("localhost")]
    public void ValidNames_AreAccepted(string name)
    {
        Codes(new HostnameDefinition { Name = name, Certificate = Files() }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad host")]
    [InlineData("-bad.example.com")]
    [InlineData("a.*.example.com")]
    [InlineData("http://example.com")]
    public void InvalidNames_AreRejected(string name)
    {
        Codes(new HostnameDefinition { Name = name }).ShouldContain("hostname.invalid");
    }

    [Fact]
    public void DuplicateNames_IgnoringCase_AreRejected()
    {
        Codes(new HostnameDefinition { Name = "API.example.com" }, new HostnameDefinition { Name = "api.example.com" }).ShouldContain("hostname.duplicate");
    }

    [Fact]
    public void CertificateWithBothOrNeitherSource_IsRejected()
    {
        Codes(new HostnameDefinition { Name = "a.test", Certificate = new CertificateSource() }).ShouldContain("hostname.certificate-source");
        Codes(new HostnameDefinition { Name = "a.test", Certificate = new CertificateSource { File = new CertificateFiles("c", "k"), Secret = "ns/s" } })
            .ShouldContain("hostname.certificate-source");
    }

    [Fact]
    public void CertificateFilesAndSecretFormat_AreChecked()
    {
        Codes(new HostnameDefinition { Name = "a.test", Certificate = new CertificateSource { File = new CertificateFiles("", "k") } }).ShouldContain("hostname.certificate-files");
        Codes(new HostnameDefinition { Name = "a.test", Certificate = new CertificateSource { Secret = "just-a-name" } }).ShouldContain("hostname.certificate-secret");
        Codes(new HostnameDefinition { Name = "a.test", Certificate = new CertificateSource { Secret = "ns/name" } }).ShouldBeEmpty();
    }

    [Fact]
    public void NegativeHstsMaxAge_IsRejected()
    {
        Codes(new HostnameDefinition { Name = "a.test", Hsts = new HstsDefinition { MaxAgeSeconds = -1 } }).ShouldContain("hostname.hsts");
    }
}
