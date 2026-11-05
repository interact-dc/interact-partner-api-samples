using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Interact.PartnerApi.Tests;

public class KeyToolsTests
{
    [Fact]
    public void The_public_JWKS_carries_the_public_key_only()
    {
        using var key = KeyTools.GenerateKey();

        using var jwks = JsonDocument.Parse(KeyTools.PublicJwks(key));

        var jwk = jwks.RootElement.GetProperty("keys")[0];
        Assert.Equal("EC", jwk.GetProperty("kty").GetString());
        Assert.Equal("P-256", jwk.GetProperty("crv").GetString());
        Assert.Equal("ES256", jwk.GetProperty("alg").GetString());
        Assert.Equal("sig", jwk.GetProperty("use").GetString());
        Assert.False(jwk.TryGetProperty("d", out _), "the private key must never be in the JWKS");
    }

    [Fact]
    public void The_key_id_is_the_RFC_7638_thumbprint()
    {
        using var key = KeyTools.GenerateKey();
        var publicKey = new ECDsaSecurityKey(ECDsa.Create(key.ExportParameters(false)));
        var expected = Base64UrlEncoder.Encode(
            JsonWebKeyConverter.ConvertFromECDsaSecurityKey(publicKey).ComputeJwkThumbprint());

        using var jwks = JsonDocument.Parse(KeyTools.PublicJwks(key));

        Assert.Equal(expected, KeyTools.KeyId(key));
        Assert.Equal(expected, jwks.RootElement.GetProperty("keys")[0].GetProperty("kid").GetString());
    }

    [Fact]
    public void A_private_key_survives_a_round_trip_through_PEM()
    {
        using var key = KeyTools.GenerateKey();

        using var loaded = KeyTools.LoadPrivateKeyPem(KeyTools.ExportPrivateKeyPem(key));

        Assert.Equal(KeyTools.KeyId(key), KeyTools.KeyId(loaded));
    }
}

public class AssertionSignerTests
{
    private static readonly Uri TokenUrl = new("https://partners.example/oauth/token");
    private readonly ECDsa _key = KeyTools.GenerateKey();
    private readonly ManualTime _time = new(DateTimeOffset.UtcNow);

    private AssertionSigner Signer() => new("client-123", TokenUrl, _key, _time);

    private JsonWebToken Verify(string token)
    {
        var result = new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = new ECDsaSecurityKey(ECDsa.Create(_key.ExportParameters(false))),
            ValidIssuer = "client-123",
            ValidAudience = TokenUrl.ToString(),
        }).GetAwaiter().GetResult();
        Assert.True(result.IsValid, result.Exception?.Message);
        return (JsonWebToken)result.SecurityToken;
    }

    [Fact]
    public void A_client_assertion_names_the_client_and_is_for_the_token_url()
    {
        var token = Verify(Signer().ClientAssertion());

        Assert.Equal("client-123", token.Subject);
        Assert.Equal("ES256", token.Alg);
        Assert.Equal(KeyTools.KeyId(_key), token.Kid);
    }

    [Fact]
    public void A_client_assertion_lives_at_most_five_minutes()
    {
        var token = Verify(Signer().ClientAssertion());

        Assert.True(token.ValidTo - token.IssuedAt <= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Every_assertion_has_a_fresh_id()
    {
        var signer = Signer();

        Assert.NotEqual(Verify(signer.ClientAssertion()).Id, Verify(signer.ClientAssertion()).Id);
    }

    [Fact]
    public void A_subject_token_names_the_user_their_tenant_and_role()
    {
        var user = new PartnerUser("entra-oid-42", "contoso", "member", Name: "Bob Smith", Email: "bob@contoso.example");

        var token = Verify(Signer().SubjectToken(user));

        Assert.Equal("entra-oid-42", token.Subject);
        Assert.Equal("contoso", token.GetClaim("tenant").Value);
        Assert.Equal("member", token.GetClaim("role").Value);
        Assert.Equal("Bob Smith", token.GetClaim("name").Value);
        Assert.Equal("bob@contoso.example", token.GetClaim("email").Value);
    }

    [Fact]
    public void A_subject_token_leaves_out_what_it_was_not_given()
    {
        var token = Verify(Signer().SubjectToken(new PartnerUser("u1", "contoso", "viewer")));

        Assert.False(token.TryGetClaim("name", out _));
        Assert.False(token.TryGetClaim("email", out _));
    }
}
