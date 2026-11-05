using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Interact.PartnerApi;

/// <summary>
/// Your system's signing key: an ECDSA P-256 key pair (ES256).
/// Keep the private key secret, for example in Azure Key Vault. Send Interact
/// only the public JWKS, or publish it at a web address and send us that.
/// </summary>
public static class KeyTools
{
    /// <summary>A new P-256 key pair.</summary>
    public static ECDsa GenerateKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The private key as PKCS#8 PEM, for storing in your key vault or a protected file.</summary>
    public static string ExportPrivateKeyPem(ECDsa key) => key.ExportPkcs8PrivateKeyPem();

    public static ECDsa LoadPrivateKeyPem(string pem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }

    /// <summary>
    /// The key's id: its RFC 7638 thumbprint. Every token you sign names it in
    /// its <c>kid</c> header, so Interact knows which of your keys to check.
    /// </summary>
    public static string KeyId(ECDsa key)
    {
        var (x, y) = PublicPoint(key);
        // RFC 7638: the required members, in lexical order, no whitespace.
        var canonical = $$"""{"crv":"P-256","kty":"EC","x":"{{x}}","y":"{{y}}"}""";
        return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>The public key as a JWKS document: what you send Interact. It never holds the private key.</summary>
    public static string PublicJwks(ECDsa key)
    {
        var (x, y) = PublicPoint(key);
        var jwks = new
        {
            keys = new[]
            {
                new Dictionary<string, string>
                {
                    ["kty"] = "EC", ["crv"] = "P-256", ["x"] = x, ["y"] = y,
                    ["kid"] = KeyId(key), ["use"] = "sig", ["alg"] = "ES256",
                },
            },
        };
        return JsonSerializer.Serialize(jwks, new JsonSerializerOptions { WriteIndented = true });
    }

    private static (string X, string Y) PublicPoint(ECDsa key)
    {
        var point = key.ExportParameters(includePrivateParameters: false).Q;
        return (Base64UrlEncoder.Encode(point.X!), Base64UrlEncoder.Encode(point.Y!));
    }
}
