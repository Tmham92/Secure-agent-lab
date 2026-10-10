using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SecureAgentLab.Transport.Credentials;

namespace SecureAgentLab.Api.Authentication;

public sealed class LabAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, CredentialService credentials) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Credential? c = credentials.Validate(header[7..]);
        if (c is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid credential."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, c.Subject),
            new(ClaimTypes.Role, c.Role)
        };
        if (c.GrantFingerprint is not null)
        {
            claims.Add(new("grant", c.GrantFingerprint));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
