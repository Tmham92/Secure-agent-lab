using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecureAgentLab.Transport.Configuration;

namespace SecureAgentLab.Transport.Credentials;
// An explicitly versioned educational credential format, NOT JWT/OIDC.
// HMAC keys reside only in the trusted operator/issuer and gateway, never the worker.
public sealed class CredentialService
{
    private readonly CredentialSettings settings;
    private readonly byte[] key;
    private readonly TimeProvider clock;
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public CredentialService(CredentialSettings settings, TimeProvider? clock = null)
    {
        this.settings = settings;
        key = settings.Validate();
        this.clock = clock ?? TimeProvider.System;
    }

    public string Issue(string subject, string role, TimeSpan lifetime, string? grantFingerprint = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(5) || string.IsNullOrWhiteSpace(subject) || (role != "worker" && role != "operator") || (role == "worker" && string.IsNullOrWhiteSpace(grantFingerprint)) || (role == "operator" && grantFingerprint is not null))
        {
            throw new ArgumentException("Invalid credential grant or lifetime.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Credential(settings.Issuer, settings.Audience, subject, role, now, now.Add(lifetime), grantFingerprint), Json));
        string signed = "v1." + payload;
        return signed + "." + Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed)));
    }

    public Credential? Validate(string token)
    {
        if (token.Length > 8192)
        {
            return null;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "v1")
        {
            return null;
        }

        try
        {
            byte[] supplied = Convert.FromBase64String(parts[2]);
            byte[] expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]));
            if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
            {
                return null;
            }

            Credential? c = JsonSerializer.Deserialize<Credential>(Convert.FromBase64String(parts[1]), Json);
            DateTimeOffset now = clock.GetUtcNow();
            if (c is null || c.Issuer != settings.Issuer || c.Audience != settings.Audience || string.IsNullOrWhiteSpace(c.Subject) || c.IssuedAt.Offset != TimeSpan.Zero || c.ExpiresAt.Offset != TimeSpan.Zero || c.IssuedAt > now || c.ExpiresAt <= now || c.ExpiresAt <= c.IssuedAt || c.ExpiresAt - c.IssuedAt > TimeSpan.FromMinutes(5) || (c.Role != "worker" && c.Role != "operator") || (c.Role == "worker" && string.IsNullOrWhiteSpace(c.GrantFingerprint)) || (c.Role == "operator" && c.GrantFingerprint is not null))
            {
                return null;
            }

            return c;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
