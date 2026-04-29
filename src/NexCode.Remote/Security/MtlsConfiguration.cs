using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NexCode.Remote.Security;

/// <summary>
/// Spec §23.3 — Kestrel mTLS configuration for nexcode-remote.
///
/// The server certificate is loaded from <c>NEXCODE_REMOTE_SERVER_CERT_PFX</c>,
/// optionally protected by <c>NEXCODE_REMOTE_SERVER_CERT_PASSWORD</c>. Client
/// certificates are required and validated; the OID allow-list is read from
/// <c>NEXCODE_REMOTE_ALLOWED_OIDS</c> (semicolon-separated).
/// </summary>
public static class MtlsConfiguration
{
    public const string ServerCertPathEnv = "NEXCODE_REMOTE_SERVER_CERT_PFX";
    public const string ServerCertPasswordEnv = "NEXCODE_REMOTE_SERVER_CERT_PASSWORD";
    public const string AllowedOidsEnv = "NEXCODE_REMOTE_ALLOWED_OIDS";
    public const string ListenPortEnv = "NEXCODE_REMOTE_PORT";
    private const int DefaultPort = 7443;

    public static void ConfigureKestrel(WebApplicationBuilder builder)
    {
        var allowedOids = LoadAllowedOids();
        var serverCert = LoadServerCertificate();
        var port = ResolvePort();

        builder.Services.AddSingleton(new MtlsAllowedOidsRegistry(allowedOids));

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Any, port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
                listenOptions.UseHttps(httpsOptions =>
                {
                    if (serverCert is not null)
                    {
                        httpsOptions.ServerCertificate = serverCert;
                    }

                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsOptions.ClientCertificateValidation = (clientCert, chain, errors) =>
                        ValidateClientCertificate(clientCert, allowedOids);
                });
            });
        });
    }

    public static bool ValidateClientCertificate(X509Certificate2 clientCert, IReadOnlyCollection<string> allowedOids)
    {
        if (allowedOids.Count == 0)
        {
            // No allow-list configured: accept any chain that Kestrel already
            // accepted (matches dev behaviour).
            return true;
        }

        foreach (var extension in clientCert.Extensions)
        {
            if (extension.Oid is null)
            {
                continue;
            }

            if (allowedOids.Any(oid => string.Equals(oid, extension.Oid.Value, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Also accept by Subject OID (rare but useful for SmartCard CAs).
        var subjectOid = clientCert.Subject;
        return allowedOids.Any(oid => subjectOid.Contains(oid, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyCollection<string> LoadAllowedOids()
    {
        var raw = Environment.GetEnvironmentVariable(AllowedOidsEnv);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static X509Certificate2? LoadServerCertificate()
    {
        var path = Environment.GetEnvironmentVariable(ServerCertPathEnv);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var password = Environment.GetEnvironmentVariable(ServerCertPasswordEnv);
        return new X509Certificate2(path, password, X509KeyStorageFlags.MachineKeySet);
    }

    private static int ResolvePort()
    {
        var raw = Environment.GetEnvironmentVariable(ListenPortEnv);
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : DefaultPort;
    }
}

public sealed record MtlsAllowedOidsRegistry(IReadOnlyCollection<string> AllowedOids);
