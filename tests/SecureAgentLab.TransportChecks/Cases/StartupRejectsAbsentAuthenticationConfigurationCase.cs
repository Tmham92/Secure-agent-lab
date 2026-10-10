using SecureAgentLab.Core.Execution;
using SecureAgentLab.Transport.Credentials;
using SecureAgentLab.TransportChecks.Fixtures;
namespace SecureAgentLab.TransportChecks.Cases;

internal static class StartupRejectsAbsentAuthenticationConfigurationCase
{
    internal static Task Run(global::SecureAgentLab.Transport.Configuration.CredentialSettings settings)
    {
        CheckAssertions.Throws<ArgumentException>(() => new CredentialService(new("", "", settings.SigningKey)));
        CheckAssertions.Throws<ArgumentException>(() => new CredentialService(settings with { SigningKey = Convert.ToBase64String(new byte[8]) }));
        CheckAssertions.Throws<ArgumentNullException>(() => new Gateway(null!, new SyntheticExecutor()));
        return Task.CompletedTask;
    }
}
