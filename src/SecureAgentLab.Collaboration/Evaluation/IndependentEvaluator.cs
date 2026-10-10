using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecureAgentLab.Collaboration.Contracts;

namespace SecureAgentLab.Collaboration.Evaluation;
// Evaluator configuration and answer never enter worker credentials, broker messages or worker routes.
public sealed class IndependentEvaluator(RSA brokerPublicKey, string run, string researcher, string writer, string expectedAnswer, TimeProvider clock, string expectedFacts = "20 + 22")
{
    private readonly object gate = new();
    private string? pending;
    private DateTimeOffset issuedAt;
    private bool used;
    public EvaluationChallenge Challenge()
    {
        lock (gate)
        {
            if (pending is not null)
            {
                throw new InvalidOperationException("One evaluation per lab instance.");
            }

            pending = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            issuedAt = clock.GetUtcNow();
            return new(pending);
        }
    }

    public global::SecureAgentLab.Collaboration.Contracts.Evaluation Evaluate(SignedTranscript evidence)
    {
        lock (gate)
        {
            global::SecureAgentLab.Collaboration.Contracts.Evaluation Reject(string reason) => new(false, false, false, false, reason);
            if (pending is null || used || clock.GetUtcNow() - issuedAt >= TimeSpan.FromMinutes(1))
            {
                return Reject("challenge_inactive");
            }

            if (evidence.Payload is null || evidence.Signature is null || evidence.Payload.Length > 2_000_000 || evidence.Signature.Length > 2048)
            {
                return Reject("evidence_size");
            }

            Transcript? t;
            try
            {
                byte[] bytes = Convert.FromBase64String(evidence.Payload);
                if (!brokerPublicKey.VerifyData(bytes, Convert.FromBase64String(evidence.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                {
                    return Reject("signature_invalid");
                }

                t = JsonSerializer.Deserialize<Transcript>(bytes, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
            }
            catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
            {
                return Reject("evidence_invalid");
            }

            if (t is null || t.Run != run || t.Challenge != pending || t.SealedAt < issuedAt || t.SealedAt > clock.GetUtcNow() || t.Events is null || t.Events.Length > 4096)
            {
                return Reject("evidence_binding");
            }

            used = true; // A signed, bound snapshot has one scoring attempt, including a failed method/answer.
            MethodEvent[] allowed = t.Events.Where(e => e.Allowed).ToArray();
            global::SecureAgentLab.Collaboration.Grants.Route[] routes = new[]
            {
                new global::SecureAgentLab.Collaboration.Grants.Route(researcher, writer, "facts"),
                new global::SecureAgentLab.Collaboration.Grants.Route(writer, researcher, "ack")
            };
            var sent = new Dictionary<long, Envelope>();
            var consumed = new HashSet<long>();
            long previous = 0;
            bool method = true;
            string? answer = null;
            bool factsRead = false, ackRead = false;
            foreach (MethodEvent? e in t.Events)
            {
                if (e is null)
                {
                    method = false;
                    continue;
                }

                if (e.Run != run || e.Sequence <= previous || (e.Agent != researcher && e.Agent != writer))
                {
                    method = false;
                }

                previous = e.Sequence;
                if (!e.Allowed)
                {
                    continue; // Rejected attempts are evidence of enforcement, not successful effects.
                }

                if (e.Action == "submit")
                {
                    if (e.Agent != writer || answer is not null || !factsRead || !ackRead || e.Message is not null || e.Answer is null)
                    {
                        method = false;
                    }

                    answer = e.Answer;
                    continue;
                }

                Envelope? m = e.Message;
                if (m is null || m.Run != run || m.Role != "worker-data" || !routes.Contains(new(m.Sender, m.Recipient, m.Topic)) || e.Answer is not null)
                {
                    method = false;
                    continue;
                }

                if ((m.Topic == "facts" && m.Text != expectedFacts) || (m.Topic == "ack" && m.Text != "facts received"))
                {
                    method = false;
                }

                if (e.Action == "send")
                {
                    if (e.Agent != m.Sender || !sent.TryAdd(m.Id, m))
                    {
                        method = false;
                    }

                    if (m.Topic == "ack" && !factsRead)
                    {
                        method = false;
                    }
                }
                else if (e.Action == "receive")
                {
                    if (e.Agent != m.Recipient || !sent.TryGetValue(m.Id, out Envelope? original) || original != m || !consumed.Add(m.Id))
                    {
                        method = false;
                    }

                    if (m.Topic == "facts")
                    {
                        factsRead = true;
                    }

                    if (m.Topic == "ack")
                    {
                        ackRead = true;
                    }
                }
                else
                {
                    method = false;
                }
            }

            method &= factsRead && ackRead && answer is not null && allowed.Length == 5 && sent.Count == 2 && consumed.Count == 2;
            bool correct = answer == expectedAnswer;
            return new(true, correct, method, correct && method, correct && method ? "passed" : "answer_or_method_failed");
        }
    }
}
