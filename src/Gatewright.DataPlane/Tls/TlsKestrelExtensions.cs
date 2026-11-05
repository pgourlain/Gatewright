// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatewright.DataPlane.Tls;

/// <summary>Wires TLS termination with SNI certificate selection into Kestrel.</summary>
public static class TlsKestrelExtensions
{
    /// <summary>Registers the TLS options, the certificate store and the reload service.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration; <c>Gateway:Tls</c> is bound.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGatewrightTls(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<TlsOptions>(configuration.GetSection(TlsOptions.SectionName));
        services.TryAddSingleton<CertificateStore>();
        services.AddSingleton<Gatewright.Core.Security.ISecurityDataSink>(sp => sp.GetRequiredService<CertificateStore>());
        services.AddHostedService<CertificateReloadService>();
        return services;
    }

    /// <summary>
    /// Makes Kestrel listen on <c>Gateway:Tls:HttpPort</c> and <c>Gateway:Tls:HttpsPort</c> when TLS termination is enabled, presenting the
    /// certificate that matches the SNI host name (hot-reloaded). Does nothing when <c>HttpsPort</c> is 0.
    /// </summary>
    /// <param name="builder">The web host builder.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The web host builder.</returns>
    public static IWebHostBuilder UseGatewrightTls(this IWebHostBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new TlsOptions();
        configuration.GetSection(TlsOptions.SectionName).Bind(options);
        if (options.HttpsPort <= 0)
        {
            return builder;
        }

        return builder.ConfigureKestrel((context, kestrel) =>
        {
            var store = kestrel.ApplicationServices.GetRequiredService<CertificateStore>();
            if (options.HttpPort > 0)
            {
                kestrel.ListenAnyIP(options.HttpPort);
            }

            kestrel.ListenAnyIP(options.HttpsPort, listen => listen.UseHttps(https =>
            {
                https.ServerCertificateSelector = (_, serverName) => store.Select(serverName);
                https.ClientCertificateMode = ClientCertificateMode.NoCertificate;
            }));
        });
    }
}
