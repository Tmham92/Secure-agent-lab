namespace SecureAgentLab.Transport.Configuration;

public sealed record CredentialSettings(string Issuer, string Audience, string SigningKey)
{
    public byte[] Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
        {
            throw new ArgumentException("Issuer and audience are required.");
        }

        byte[] key = Convert.FromBase64String(SigningKey);
        if (key.Length < 32)
        {
            throw new ArgumentException("Signing key needs at least 32 random bytes.");
        }

        return key;
    }
}
