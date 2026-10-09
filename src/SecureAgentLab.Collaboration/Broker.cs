using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureAgentLab.Transport;

namespace SecureAgentLab.Collaboration;

public sealed record Route(string Sender, string Recipient, string Topic);
public sealed record AgentGrant(string Run, string Agent, DateTimeOffset ExpiresAt, int Calls, int Bytes, Route[] Routes);
public sealed record SendMessage(string Recipient, string Topic, string Text);
public sealed record ReceiveMessage(string Sender, string Topic);
public sealed record SubmitAnswer(string Answer);
// Authority comes from authentication, never from message text or a caller-supplied role.
public sealed record Envelope(long Id, string Run, string Sender, string Recipient, string Topic, string Text, string Role = "worker-data");
public sealed record BrokerResult(bool Allowed, string Reason, Envelope? Message = null);
public sealed record MethodEvent(long Sequence, string Run, string Agent, string Action, bool Allowed, string Reason,
    Envelope? Message = null, string? Answer = null);
public sealed record Transcript(string Run, string Challenge, DateTimeOffset SealedAt, MethodEvent[] Events);
public sealed record SignedTranscript(string Payload, string Signature);

/// <summary>Single-process synthetic broker. One lock covers authorization, quotas, mailbox effects,
/// consumption and audit. State is bounded and volatile; restart is a new lab, never recovery.</summary>
public sealed class Broker(CredentialService credentials, RSA signingKey, TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<string, AgentGrant> grants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Calls, int Bytes)> usage = new(StringComparer.Ordinal);
    private readonly HashSet<string> revoked = new(StringComparer.Ordinal);
    private readonly HashSet<string> sealedRuns = new(StringComparer.Ordinal);
    private readonly List<Envelope> mailbox = [];
    private readonly List<MethodEvent> events = [];
    private bool stopped;
    private long nextMessage;
    public static string Fingerprint(AgentGrant grant) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(grant)));

    public string Register(AgentGrant grant)
    {
        lock (gate)
        {
            if (stopped) throw new InvalidOperationException("Broker stopped; new grants are blocked.");
            if (grants.Count >= 32 || string.IsNullOrWhiteSpace(grant.Run) || string.IsNullOrWhiteSpace(grant.Agent) ||
                grant.ExpiresAt <= clock.GetUtcNow() || grant.ExpiresAt > clock.GetUtcNow().AddMinutes(5) ||
                grant.Calls is < 1 or > 100 || grant.Bytes is < 1 or > 8192 || grant.Routes is null || grant.Routes.Length > 16 ||
                grant.Routes.Any(r => r is null || string.IsNullOrWhiteSpace(r.Sender) || string.IsNullOrWhiteSpace(r.Recipient) ||
                    string.IsNullOrWhiteSpace(r.Topic) || (r.Sender != grant.Agent && r.Recipient != grant.Agent)))
                throw new ArgumentException("Invalid bounded agent grant.");
            var copy = grant with { Routes = grant.Routes.ToArray() };
            grants.Add(copy.Agent, copy);
            usage.Add(copy.Agent, (0, 0));
            return credentials.Issue(copy.Agent, "worker", copy.ExpiresAt - clock.GetUtcNow(), Fingerprint(copy));
        }
    }

    public BrokerResult Execute(Credential caller, string action, SendMessage? send = null,
        ReceiveMessage? receive = null, SubmitAnswer? submit = null)
    {
        lock (gate)
        {
            if (caller.Role != "worker" || !grants.TryGetValue(caller.Subject, out var g) || caller.GrantFingerprint != Fingerprint(g))
                return new(false, "grant_binding");
            // Once full, no operation can execute without telemetry. Never evict the evidence prefix.
            if (events.Count >= 4096) return new(false, "audit_capacity");
            BrokerResult Finish(bool ok, string reason, Envelope? message = null, string? answer = null)
            {
                events.Add(new(events.Count + 1, g.Run, g.Agent, action, ok, reason, message, answer));
                return new(ok, reason, message);
            }
            if (stopped || revoked.Contains(g.Agent) || sealedRuns.Contains(g.Run) || g.ExpiresAt <= clock.GetUtcNow() || caller.ExpiresAt <= clock.GetUtcNow())
                return Finish(false, "inactive");
            var u = usage[g.Agent];
            if (u.Calls >= g.Calls) return Finish(false, "call_budget");
            usage[g.Agent] = u with { Calls = u.Calls + 1 }; // Authenticated denials consume attempts.
            bool HasRoute(string sender, string recipient, string topic) => g.Routes.Contains(new(sender, recipient, topic));
            bool Charge(string text)
            {
                var bytes = Encoding.UTF8.GetByteCount(text);
                if (bytes > 512 || bytes > g.Bytes - u.Bytes) return false;
                usage[g.Agent] = (u.Calls + 1, u.Bytes + bytes);
                return true;
            }
            if (action == "send" && send is not null)
            {
                if (send.Text is null || send.Recipient is null || send.Topic is null ||
                    !HasRoute(g.Agent, send.Recipient, send.Topic) || !grants.TryGetValue(send.Recipient, out var target) ||
                    target.Run != g.Run || revoked.Contains(target.Agent) || target.ExpiresAt <= clock.GetUtcNow())
                    return Finish(false, "route_denied");
                if (mailbox.Count(m => m.Run == g.Run) >= 8) return Finish(false, "mailbox_capacity");
                if (!Charge(send.Text)) return Finish(false, "byte_budget");
                var message = new Envelope(++nextMessage, g.Run, g.Agent, send.Recipient, send.Topic, send.Text);
                mailbox.Add(message);
                return Finish(true, "delivered", message);
            }
            if (action == "receive" && receive is not null)
            {
                if (!HasRoute(receive.Sender, g.Agent, receive.Topic)) return Finish(false, "route_denied");
                var message = mailbox.FirstOrDefault(m => m.Run == g.Run && m.Sender == receive.Sender && m.Recipient == g.Agent && m.Topic == receive.Topic);
                if (message is null) return Finish(false, "empty_mailbox");
                if (!Charge(message.Text)) return Finish(false, "byte_budget");
                mailbox.Remove(message);
                return Finish(true, "consumed", message);
            }
            if (action == "submit" && submit?.Answer is not null)
            {
                if (events.Any(e => e.Run == g.Run && e.Agent == g.Agent && e.Action == "submit" && e.Allowed)) return Finish(false, "already_submitted");
                if (!Charge(submit.Answer)) return Finish(false, "byte_budget");
                return Finish(true, "submitted", answer: submit.Answer);
            }
            return Finish(false, "channel_denied"); // No shared cache, artifact or worker-writable log surface.
        }
    }

    public SignedTranscript Seal(string run, string challenge)
    {
        lock (gate)
        {
            if (challenge.Length != 64 || !challenge.All(Uri.IsHexDigit) || !grants.Values.Any(g => g.Run == run) || !sealedRuns.Add(run))
                throw new InvalidOperationException("Unknown or already sealed run, or invalid challenge.");
            var payload = JsonSerializer.SerializeToUtf8Bytes(new Transcript(run, challenge, clock.GetUtcNow(), events.Where(e => e.Run == run).ToArray()));
            return new(Convert.ToBase64String(payload), Convert.ToBase64String(signingKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)));
        }
    }
    public void Revoke(string agent) { lock (gate) revoked.Add(agent); }
    public void Stop() { lock (gate) stopped = true; }
    public int Delivered { get { lock (gate) return events.Count(e => e.Allowed && e.Action == "send"); } }
    public int Submissions { get { lock (gate) return events.Count(e => e.Allowed && e.Action == "submit"); } }
}
