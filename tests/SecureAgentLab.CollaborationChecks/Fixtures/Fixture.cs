using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.Collaboration.Evaluation;
using SecureAgentLab.Transport.Credentials;
namespace SecureAgentLab.CollaborationChecks.Fixtures;

sealed class Fixture : IDisposable
{
    public ManualClock Clock { get; } = new();
    public RSA Signing { get; } = RSA.Create(2048);

    private readonly RSA _publicKey = RSA.Create();
    public string Key { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public CredentialService Credentials
    {
        get;
    }
    public global::SecureAgentLab.Collaboration.Broker.Broker Broker
    {
        get;
    }
    public IndependentEvaluator Evaluator
    {
        get;
    }
    public Dictionary<string, string> Tokens { get; } = [];

    public Fixture(int calls = 32, int bytes = 2048)
    {
        Credentials = new(new("phase8-supervisor", "phase8-broker", Key), Clock);
        Broker = new(Credentials, Signing, Clock);
        _publicKey.ImportFromPem(Signing.ExportSubjectPublicKeyInfoPem());
        Evaluator = new(_publicKey, "demo-run", "researcher", "writer", "42", Clock);
        global::SecureAgentLab.Collaboration.Grants.Route[] routes = [new("researcher", "writer", "facts"), new("writer", "researcher", "ack")];
        Register("researcher", "demo-run", routes, calls, bytes);
        Register("writer", "demo-run", routes, 100, bytes);
    }

    public void Register(string agent, string run, global::SecureAgentLab.Collaboration.Grants.Route[] routes, int calls = 32, int bytes = 2048) => Tokens.Add(agent, Broker.Register(new(run, agent, Clock.GetUtcNow().AddMinutes(2), calls, bytes, routes)));
    public Credential Caller(string agent) => Credentials.Validate(Tokens[agent]) ?? JsonSerializer.Deserialize<Credential>(Convert.FromBase64String(Tokens[agent].Split('.')[1]))!; // Expiry domain check also exercised without HTTP.
    public BrokerResult Send(string agent, string to, string topic, string text) => Broker.Execute(Caller(agent), "send", new(to, topic, text));
    public BrokerResult Receive(string agent, string from, string topic) => Broker.Execute(Caller(agent), "receive", receive: new(from, topic));
    public BrokerResult Submit(string answer) => Broker.Execute(Caller("writer"), "submit", submit: new(answer));
    public void Complete(string answer = "42")
    {
        if (!Send("researcher", "writer", "facts", "20 + 22").Allowed || !Receive("writer", "researcher", "facts").Allowed || !Send("writer", "researcher", "ack", "facts received").Allowed || !Receive("researcher", "writer", "ack").Allowed || !Submit(answer).Allowed)
        {
            throw new InvalidOperationException("Authorized method failed");
        }
    }

    public global::SecureAgentLab.Collaboration.Contracts.Evaluation Score() => Evaluator.Evaluate(Broker.Seal("demo-run", Evaluator.Challenge().Challenge));
    public void Dispose()
    {
        Signing.Dispose();
        _publicKey.Dispose();
    }
}
