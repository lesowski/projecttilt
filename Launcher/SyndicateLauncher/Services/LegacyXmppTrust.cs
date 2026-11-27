using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SyndicateLauncher.Services;

/// <summary>
/// Pre-season (1.7.2), Season 1 (1.8, 1.8.2) and Season 2 (1.11) only send their chat/party login over TLS
/// (backend <c>src/Utils/syndicateLegacyXmpp.js</c>, TCP 7071), and they only trust root certificates built
/// into the game exe. Without TLS the login never happens ("XMPP log-in credentials not valid (6)"), the party
/// never syncs and the lobby waits on "Waiting for players to ready up..." forever.
///
/// Each install makes its own loopback-only, self-signed certificate (no key ships with the source), keeps it in
/// <c>%APPDATA%\SyndicateLauncher\legacy-xmpp-tls</c>, hands it to the backend (TILT_LEGACY_XMPP_TLS) and writes
/// it into the game in place of a built-in root of the same size: the expired UTN-USERFirst-Hardware root (2019),
/// or a certificate an earlier Tilt / Project Tilted launcher put there. The untouched exe is kept next to it.
/// </summary>
internal static class LegacyXmppTrust
{
    /// <summary>DER size of the built-in root that is replaced; the new certificate must match it exactly.</summary>
    private const int CertSize = 1144;
    private const string Marker = "Tilt loopback XMPP";
    /// <summary>Certificates earlier launchers wrote into the game carry one of these in an OU.</summary>
    private static readonly string[] PatchedMarkers = { Marker, "Tilted local XMPP" };
    /// <summary>Start of a 1144-byte certificate with an 860-byte TBS part (all the candidates share it).</summary>
    private static readonly byte[] CertPrefix = { 0x30, 0x82, 0x04, 0x74, 0x30, 0x82, 0x03, 0x5C };
    private const string ExpiredRoot = "MIIEdDCCA1ygAwIBAgIQRL4Mi1AAJLQR0zYq/mUK/TANBgkqhkiG9w0BAQUFADCBlzELMAkGA1UEBhMCVVMxCzAJBgNVBAgTAlVUMRcwFQYDVQQHEw5TYWx0IExha2UgQ2l0eTEeMBwGA1UEChMVVGhlIFVTRVJUUlVTVCBOZXR3b3JrMSEwHwYDVQQLExhodHRwOi8vd3d3LnVzZXJ0cnVzdC5jb20xHzAdBgNVBAMTFlVUTi1VU0VSRmlyc3QtSGFyZHdhcmUwHhcNOTkwNzA5MTgxMDQyWhcNMTkwNzA5MTgxOTIyWjCBlzELMAkGA1UEBhMCVVMxCzAJBgNVBAgTAlVUMRcwFQYDVQQHEw5TYWx0IExha2UgQ2l0eTEeMBwGA1UEChMVVGhlIFVTRVJUUlVTVCBOZXR3b3JrMSEwHwYDVQQLExhodHRwOi8vd3d3LnVzZXJ0cnVzdC5jb20xHzAdBgNVBAMTFlVUTi1VU0VSRmlyc3QtSGFyZHdhcmUwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCx98M4P7Sof885glFn0G2f0v9Y8+efK+wNiVSZuTiZFvfgIXlIwrthdBKWHTxqctU8EGc6Oe0rE81m65UJM6Rsl7HoxuzBdXmcRl6Nq9Bq/bkqVRcQVLMZ8Jr28bFdtqdt++BxF2uiiPsA3/4aMXcMmgF6sTLjKwEHOG7DpV4jvEWbe1DByTCP2+UretNb+zNAHqDVmBe8i4fDidNdoI6yqqr2jmmIBsX6iSHzCJ1pLgkzmykNRg+MzEk0sGlRvfkGzWitZky8PqxhvQqIDsjfPe58BEydCl5rkdbux+0ojatNh4lz0G6k0B4WixThdkQDf2Os5M1JnMWS9KsyoUhbAgMBAAGjgbkwgbYwCwYDVR0PBAQDAgHGMA8GA1UdEwEB/wQFMAMBAf8wHQYDVR0OBBYEFKFyXyYbKJhDlV0HN9WFlp1L0sNFMEQGA1UdHwQ9MDswOaA3oDWGM2h0dHA6Ly9jcmwudXNlcnRydXN0LmNvbS9VVE4tVVNFUkZpcnN0LUhhcmR3YXJlLmNybDAxBgNVHSUEKjAoBggrBgEFBQcDAQYIKwYBBQUHAwUGCCsGAQUFBwMGBggrBgEFBQcDBzANBgkqhkiG9w0BAQUFAAOCAQEARxkP3nTGmZev/K0oXnWO6y1n7k57K9cM//bey1WiCuFMVGWTYGufEpytXoMs61quwOQt9ABjHbjAbPLPSbtNk28GpgoiskliCE7/yMgUsogWXecB5BKV5UU0s4tpvc+0hY91UZ59Ojg6FEgSxvunOxqNDYJAB+gECJChicsZUN/KHAG8HQQZexB2lzvukJDKxA4fFm517zP4029bHpbj4HR3dHuKom4t3XbWOTCC8KucUvIqx69JXn7HaOWCgchqJ/kniCrVWFCVH/A7HFe7fRQ5YiuayZSSKqMiDP+JJn1fIytH1xUdqWqeUQ0qUZ6B+dQ7XnASfxAynB67nfhmqA==";
    private static readonly object Gate = new();

    public static bool AppliesTo(string? version) => version is "1.7.2" or "1.8" or "1.8.2" or "1.11";

    /// <summary>Folder with key.pem and cert.pem, created on first use. Read by the backend's legacy XMPP adapter.</summary>
    public static string TlsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SyndicateLauncher", "legacy-xmpp-tls");

    /// <summary>The certificate (DER) for this install, made the first time it is needed.</summary>
    public static byte[] EnsureCertificate()
    {
        lock (Gate)
        {
            string certFile = Path.Combine(TlsDirectory, "cert.pem"), keyFile = Path.Combine(TlsDirectory, "key.pem");
            try
            {
                if (File.Exists(certFile) && File.Exists(keyFile))
                {
                    using var saved = X509Certificate2.CreateFromPem(File.ReadAllText(certFile));
                    using var key = RSA.Create();
                    key.ImportFromPem(File.ReadAllText(keyFile));
                    using var savedKey = saved.GetRSAPublicKey();
                    if (saved.RawData.Length == CertSize && savedKey is not null && saved.NotAfter > DateTime.Now.AddDays(30)
                        && savedKey.ExportParameters(false).Modulus.AsSpan().SequenceEqual(key.ExportParameters(false).Modulus))
                        return saved.RawData;
                }
            }
            catch { /* damaged files: make a new pair below */ }

            using var rsa = RSA.Create(2048);
            byte[] der = Create(rsa);
            Directory.CreateDirectory(TlsDirectory);
            File.WriteAllText(keyFile, rsa.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(certFile, PemEncoding.WriteString("CERTIFICATE", der) + "\n");
            return der;
        }
    }

    /// <summary>
    /// A CA certificate for prod.ol.epicgames.com (the game checks the name) padded to exactly <see cref="CertSize"/>
    /// bytes, so it fits in the place of the root it replaces.
    /// </summary>
    private static byte[] Create(RSA rsa)
    {
        var notBefore = DateTimeOffset.UtcNow.Date.AddDays(-1);
        for (int pad = 0; pad < 200; pad++)
        {
            var name = new X500DistinguishedNameBuilder();
            name.AddCountryOrRegion("US");
            name.AddOrganizationName("Project Tilt");
            name.AddOrganizationalUnitName(Marker + " " + new string('x', pad));
            name.AddCommonName("prod.ol.epicgames.com");
            var subject = name.Build();
            var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var ski = new X509SubjectKeyIdentifierExtension(request.PublicKey, false);
            request.CertificateExtensions.Add(ski);
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(ski));
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("prod.ol.epicgames.com");
            san.AddDnsName("*.ol.epicgames.com");
            san.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var cert = request.Create(subject, X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
                notBefore, notBefore.AddYears(10), new byte[] { 0x2A });
            if (cert.RawData.Length == CertSize) return cert.RawData;
            if (cert.RawData.Length > CertSize) break;
        }
        throw new InvalidOperationException("Could not make a chat certificate of the right size.");
    }

    /// <summary>Writes this install's certificate into an early-season game exe. Call while the game is closed.</summary>
    public static void Ensure(string gameExe, string? version, Action<string>? log)
    {
        if (!AppliesTo(version) || !File.Exists(gameExe)) return;
        byte[] cert = EnsureCertificate();
        byte[] data = File.ReadAllBytes(gameExe);
        if (data.AsSpan().IndexOf((ReadOnlySpan<byte>)cert) >= 0) return;

        int at = data.AsSpan().IndexOf((ReadOnlySpan<byte>)Convert.FromBase64String(ExpiredRoot));
        if (at < 0) at = FindPatched(data);
        if (at < 0)
        {
            log?.Invoke("Party chat: this game has no built-in certificate Tilt can replace, so ready-up and invites may not work.");
            return;
        }

        string backup = gameExe + ".tilt-original";
        if (!File.Exists(backup) && !File.Exists(gameExe + ".tilted-original")) File.Copy(gameExe, backup);
        var attributes = File.GetAttributes(gameExe);
        if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(gameExe, attributes & ~FileAttributes.ReadOnly);
        using (var stream = new FileStream(gameExe, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            stream.Position = at;
            stream.Write(cert, 0, cert.Length);
        }
        log?.Invoke("Party chat: installed Tilt's certificate in the game (Fortnite " + version + ").");
    }

    /// <summary>Offset of a certificate an earlier launcher wrote into the game, or -1.</summary>
    private static int FindPatched(byte[] data)
    {
        foreach (var marker in PatchedMarkers)
        {
            int hit = data.AsSpan().IndexOf((ReadOnlySpan<byte>)Encoding.ASCII.GetBytes(marker));
            if (hit < 0) continue;
            int from = Math.Max(0, hit - 400);
            int start = data.AsSpan(from, hit - from).LastIndexOf((ReadOnlySpan<byte>)CertPrefix);
            if (start >= 0 && from + start + CertSize <= data.Length) return from + start;
        }
        return -1;
    }
}
