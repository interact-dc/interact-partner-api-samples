using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Interact.PartnerApi;

/// <summary>
/// One of your users, as your own sign-in (for example Microsoft Entra ID) knows them.
/// </summary>
/// <param name="Subject">Your stable id for the user, such as their Entra object id.</param>
/// <param name="Tenant">The tenant they belong to: your id for their organisation.</param>
/// <param name="Role"><c>admin</c>, <c>member</c> or <c>viewer</c>.</param>
public sealed record PartnerUser(string Subject, string Tenant, string Role, string? Name = null, string? Email = null);

/// <summary>
/// Signs the two kinds of JWT your system sends to Interact's token endpoint
/// (OAuth 2.0 <c>private_key_jwt</c>, RFC 7523):
/// a <b>client assertion</b>, proving the request comes from your system, and a
/// <b>subject token</b>, saying which of your users it acts for (RFC 8693).
/// Each is used once and lives two minutes.
/// </summary>
public sealed class AssertionSigner
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly string _clientId;
    private readonly string _audience;
    private readonly SigningCredentials _credentials;
    private readonly TimeProvider _time;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public AssertionSigner(string clientId, Uri tokenUrl, ECDsa key, TimeProvider? time = null)
    {
        _clientId = clientId;
        _audience = tokenUrl.ToString();
        _credentials = new SigningCredentials(
            new ECDsaSecurityKey(key) { KeyId = KeyTools.KeyId(key) }, SecurityAlgorithms.EcdsaSha256);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Proves the request comes from your system: <c>iss</c> and <c>sub</c> are your client id.</summary>
    public string ClientAssertion() => Sign(new Dictionary<string, object> { ["sub"] = _clientId });

    /// <summary>Says which of your users a token is for. Only the profile details you have are sent.</summary>
    public string SubjectToken(PartnerUser user)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Subject,
            ["tenant"] = user.Tenant,
            ["role"] = user.Role,
        };
        if (user.Name is not null) claims["name"] = user.Name;
        if (user.Email is not null) claims["email"] = user.Email;
        return Sign(claims);
    }

    private string Sign(Dictionary<string, object> claims)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        claims["jti"] = Guid.NewGuid().ToString("N");  // used once: a replay is refused
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _clientId,
            Audience = _audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            Claims = claims,
            SigningCredentials = _credentials,
        });
    }
}
