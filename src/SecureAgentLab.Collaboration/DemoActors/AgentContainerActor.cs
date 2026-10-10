using System.Net;
using SecureAgentLab.Collaboration.Contracts;
using static global::SecureAgentLab.Collaboration.DemoActors.ContainerActorClient;

namespace SecureAgentLab.Collaboration.DemoActors;

internal static class AgentContainerActor
{
    public static async Task RunAsync(string action)
    {
        if (action == "idle")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return;
        }

        string token = Required("COLLAB_WORKER_TOKEN");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            BaseAddress = new Uri("http://127.0.0.1:8080"),
            Timeout = TimeSpan.FromSeconds(3)
        };
        if (action == "probes")
        {
            foreach (string? secret in new[]
            {
                "Collaboration_CredentialKey",
                "Collaboration_PrivateKey",
                "Collaboration_PublicKey",
                "Collaboration_ExpectedAnswer",
                "COLLAB_EVAL_OPERATOR"
            }

            )
            {
                Check(Environment.GetEnvironmentVariable(secret) is null, "Secret configuration exposed");
            }

            Check(!File.Exists("/workspace/peer.txt") && !Directory.Exists("/protected") && !File.Exists("/var/run/docker.sock"), "Foreign workspace/mount exposed");
            try
            {
                File.WriteAllText("/app/root-write", "bad");
                throw new InvalidOperationException("Writable root");
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }

            using HttpResponseMessage ready = await http.PostAsJsonAsync("/worker/send", new SendMessage("writer", "facts", "probe"));
            Check(ready.StatusCode == HttpStatusCode.Unauthorized, "Relay/backend readiness did not reach authentication");
            foreach (string? url in new[]
            {
                "http://127.0.0.1:5188",
                "http://127.0.0.1:5190",
                "http://[::1]:5190",
                "http://169.254.169.254/latest/meta-data",
                "http://172.17.0.1:80"
            }

            )
            {
                using var denied = new HttpClient(new HttpClientHandler { UseProxy = false })
                {
                    Timeout = TimeSpan.FromSeconds(1)
                };
                try
                {
                    using HttpResponseMessage response = await denied.GetAsync(url);
                    throw new InvalidOperationException("Direct bypass returned HTTP: " + url);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                }
            }

            foreach (string? channel in new[]
            {
                "operator/seal",
                "worker/cache",
                "worker/artifacts",
                "worker/logs"
            }

            )
            {
                using HttpRequestMessage req = Request(token, "/" + channel, new
                {
                });
                using HttpResponseMessage res = await http.SendAsync(req);
                Check(res.StatusCode == HttpStatusCode.Forbidden, "Relay exposed alternate channel");
            }

            File.WriteAllText("/workspace/peer.txt", "private synthetic scratch");
            Console.WriteLine("PASS private workspace, readonly root, secret absence, relay readiness, direct IPv4/IPv6/metadata/host denial and alternate channels");
            return;
        }

        BrokerResult result;
        switch (action)
        {
            case "facts":
                result = await Post<BrokerResult>(http, token, "/worker/send", new SendMessage("writer", "facts", "20 + 22"));
                break;
            case "combine":
                result = await Post<BrokerResult>(http, token, "/worker/receive", new ReceiveMessage("researcher", "facts"));
                Check(result.Allowed && result.Message is { Role: "worker-data", Run: "demo-run", Sender: "researcher" }, "Fact envelope failed");
                string[] numbers = result.Message!.Text.Split(" + ");
                Check(numbers.Length == 2 && numbers.All(n => int.TryParse(n, out int x) && x is >= 0 and <= 100), "Invalid fact grammar");
                File.WriteAllText("/workspace/answer", (int.Parse(numbers[0]) + int.Parse(numbers[1])).ToString(System.Globalization.CultureInfo.InvariantCulture));
                result = await Post<BrokerResult>(http, token, "/worker/send", new SendMessage("researcher", "ack", "facts received"));
                break;
            case "ack":
                result = await Post<BrokerResult>(http, token, "/worker/receive", new ReceiveMessage("writer", "ack"));
                break;
            case "submit":
                result = await Post<BrokerResult>(http, token, "/worker/submit", new SubmitAnswer(File.ReadAllText("/workspace/answer")));
                break;
            default:
                throw new ArgumentException("Unknown agent action");
        }

        Check(result.Allowed, action + " failed: " + result.Reason);
        Console.WriteLine("PASS isolated agent " + action);
    }
}
