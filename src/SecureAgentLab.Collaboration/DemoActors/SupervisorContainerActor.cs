using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.Collaboration.Grants;
using SecureAgentLab.Transport.Credentials;
using static global::SecureAgentLab.Collaboration.DemoActors.ContainerActorClient;

namespace SecureAgentLab.Collaboration.DemoActors;

internal static class SupervisorContainerActor
{
    public static async Task RunAsync(string action)
    {
        string mode = Required("Collaboration_Mode");
        var issuer = new CredentialService(new("phase8-supervisor", "phase8-" + mode, Required("Collaboration_CredentialKey")));
        string op = issuer.Issue("supervisor", "operator", TimeSpan.FromMinutes(3));
        if (action == "eval-token")
        {
            Console.WriteLine(op);
            return;
        }

        using var broker = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5188"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        if (action == "issue")
        {
            global::SecureAgentLab.Collaboration.Grants.Route[] routes = [new("researcher", "writer", "facts"), new("writer", "researcher", "ack")];
            DateTimeOffset expiry = DateTimeOffset.UtcNow.AddMinutes(3);
            AgentCredential researcher = await Post<AgentCredential>(broker, op, "/operator/agents", new AgentGrant("demo-run", "researcher", expiry, 32, 2048, routes));
            AgentCredential writer = await Post<AgentCredential>(broker, op, "/operator/agents", new AgentGrant("demo-run", "writer", expiry, 32, 2048, routes));
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Researcher = researcher.Token,
                Writer = writer.Token
            }));
            return;
        }

        if (action != "evaluate")
        {
            throw new ArgumentException("Unknown supervisor action");
        }

        using var evaluator = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5190"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        string evalOp = Required("COLLAB_EVAL_OPERATOR");
        EvaluationChallenge c = await Post<EvaluationChallenge>(evaluator, evalOp, "/operator/challenge", new
        {
        });
        SignedTranscript evidence = await Post<SignedTranscript>(broker, op, "/operator/seal", new SealRequest("demo-run", c.Challenge));
        global::SecureAgentLab.Collaboration.Contracts.Evaluation result = await Post<global::SecureAgentLab.Collaboration.Contracts.Evaluation>(evaluator, evalOp, "/operator/evaluate", evidence);
        Check(result.Passed, "Isolated collaboration evaluation failed");
        string dir = "/tmp/evidence";
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "signed-transcript.json"), JsonSerializer.Serialize(evidence));
        File.WriteAllText(Path.Combine(dir, "evaluation.json"), JsonSerializer.Serialize(result));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(Required("Collaboration_PrivateKey"));
        File.WriteAllText(Path.Combine(dir, "broker-public-key.pem"), rsa.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine("PASS isolated collaboration: correct answer and authorized method, 2 messages and 1 submission");
    }
}
