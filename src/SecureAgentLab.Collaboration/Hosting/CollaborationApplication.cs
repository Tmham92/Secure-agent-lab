using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SecureAgentLab.Collaboration.DemoActors;
using SecureAgentLab.Collaboration.Evaluation;
using SecureAgentLab.Transport.Credentials;

namespace SecureAgentLab.Collaboration.Hosting;

internal static class CollaborationApplication
{
    internal static async Task RunAsync(string[] args)
    {
        if (args.FirstOrDefault() == "--container-agent")
        {
            await ContainerActors.Agent(args[1]);
            return;
        }

        if (args.FirstOrDefault() == "--container-supervisor")
        {
            await ContainerActors.Supervisor(args[1]);
            return;
        }

        string Required(string key) => Environment.GetEnvironmentVariable(key) ?? throw new ArgumentException("Missing " + key);
        string mode = Required("Collaboration_Mode");
        if (mode is not ("broker" or "evaluator"))
        {
            throw new ArgumentException("Unknown service role.");
        }

        var credentials = new CredentialService(new("phase8-supervisor", "phase8-" + mode, Required("Collaboration_CredentialKey")));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(Required(mode == "broker" ? "Collaboration_PrivateKey" : "Collaboration_PublicKey"));
        bool local = Environment.GetEnvironmentVariable("Collaboration_LoopbackHttp") == "true";
        await using WebApplication app = mode == "broker" ? CollaborationApi.Build(args, credentials, broker: new global::SecureAgentLab.Collaboration.Broker.Broker(credentials, rsa, TimeProvider.System), allowLoopbackHttp: local) : CollaborationApi.Build(args, credentials, evaluator: new IndependentEvaluator(rsa, "demo-run", "researcher", "writer", Required("Collaboration_ExpectedAnswer"), TimeProvider.System), allowLoopbackHttp: local);
        await app.StartAsync();
        Console.WriteLine("LISTEN " + app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        await app.WaitForShutdownAsync();
    }
}
