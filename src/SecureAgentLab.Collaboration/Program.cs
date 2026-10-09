using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SecureAgentLab.Collaboration;
using SecureAgentLab.Transport;

if (args.FirstOrDefault() == "--container-agent") { await ContainerActors.Agent(args[1]); return; }
if (args.FirstOrDefault() == "--container-supervisor") { await ContainerActors.Supervisor(args[1]); return; }
string Required(string key) => Environment.GetEnvironmentVariable(key) ?? throw new ArgumentException("Missing " + key);
var mode = Required("Collaboration_Mode");
if (mode is not ("broker" or "evaluator")) throw new ArgumentException("Unknown service role.");
var credentials = new CredentialService(new("phase8-supervisor", "phase8-" + mode, Required("Collaboration_CredentialKey")));
using var rsa = RSA.Create();
rsa.ImportFromPem(Required(mode == "broker" ? "Collaboration_PrivateKey" : "Collaboration_PublicKey"));
var local = Environment.GetEnvironmentVariable("Collaboration_LoopbackHttp") == "true";
await using var app = mode == "broker"
    ? CollaborationApi.Build(args, credentials, broker: new Broker(credentials, rsa, TimeProvider.System), allowLoopbackHttp: local)
    : CollaborationApi.Build(args, credentials, evaluator: new IndependentEvaluator(rsa, "demo-run", "researcher", "writer", Required("Collaboration_ExpectedAnswer"), TimeProvider.System), allowLoopbackHttp: local);
await app.StartAsync();
Console.WriteLine("LISTEN " + app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
await app.WaitForShutdownAsync();
