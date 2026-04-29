using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;

namespace NexCode.Service.Plugins;

/// <summary>
/// Verifies an Authenticode-style detached signature on a plugin folder. The manifest's
/// <c>signature</c> field is expected to be a base64 blob of the form
/// <c>{cert_b64}.{sig_b64}</c> where <c>sig</c> is an RSA-PKCS1 SHA256 signature over a
/// canonical hash of every file in the install folder (sorted by relative path).
/// Production hardening (cert chain validation, revocation, marketplace pinning) lands later.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PluginSignatureVerifier(ILogger<PluginSignatureVerifier> logger)
{
    public bool Verify(string installPath, PluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Signature))
        {
            logger.LogWarning(
                "Plugin '{Name}' has no signature; running in trusted mode. Production builds will require signed plugins.",
                manifest.Name);
            return true;
        }

        try
        {
            var separatorIndex = manifest.Signature.IndexOf('.');
            if (separatorIndex <= 0 || separatorIndex == manifest.Signature.Length - 1)
            {
                logger.LogWarning("Plugin '{Name}' has malformed signature.", manifest.Name);
                return false;
            }

            var certBytes = Convert.FromBase64String(manifest.Signature[..separatorIndex]);
            var sigBytes = Convert.FromBase64String(manifest.Signature[(separatorIndex + 1)..]);

            using var cert = X509CertificateLoader.LoadCertificate(certBytes);
            using var rsa = cert.GetRSAPublicKey();
            if (rsa is null)
            {
                logger.LogWarning("Plugin '{Name}' signature certificate has no RSA public key.", manifest.Name);
                return false;
            }

            var folderHash = ComputeFolderHash(installPath);
            return rsa.VerifyData(folderHash, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Plugin '{Name}' signature verification threw.", manifest.Name);
            return false;
        }
    }

    /// <summary>Stable hash over every file (excluding the manifest itself) sorted by relative path.</summary>
    public static byte[] ComputeFolderHash(string installPath)
    {
        if (!Directory.Exists(installPath))
        {
            return Array.Empty<byte>();
        }

        using var sha = SHA256.Create();
        var entries = Directory
            .EnumerateFiles(installPath, "*", SearchOption.AllDirectories)
            .Where(p => !string.Equals(
                Path.GetFileName(p),
                PluginManifest.ManifestFileName,
                StringComparison.OrdinalIgnoreCase))
            .Select(p => new
            {
                Relative = Path.GetRelativePath(installPath, p).Replace('\\', '/'),
                Full = p
            })
            .OrderBy(e => e.Relative, StringComparer.Ordinal)
            .ToList();

        using var aggregate = new MemoryStream();
        foreach (var entry in entries)
        {
            aggregate.Write(Encoding.UTF8.GetBytes(entry.Relative));
            aggregate.WriteByte(0);
            using var fileStream = File.OpenRead(entry.Full);
            fileStream.CopyTo(aggregate);
            aggregate.WriteByte(0);
        }

        aggregate.Position = 0;
        return sha.ComputeHash(aggregate);
    }
}
