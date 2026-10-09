using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using SecureAgentLab.Core;
using SecureAgentLab.Transport;

// Adversarial probes: these intentionally bypass the proposal client and policy gateway.
internal static class IsolationChecks
{
    internal static async Task Run()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("Container checks require Linux.");
        var status = File.ReadAllLines("/proc/self/status");
        Require(status.Single(x => x.StartsWith("Uid:")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).All(x => x == "1654"), "non-root worker UID");
        Require(status.Single(x => x.StartsWith("CapEff:")).EndsWith("0000000000000000"), "no effective capabilities");
        Require(status.Single(x => x.StartsWith("NoNewPrivs:")).TrimEnd().EndsWith("1"), "no privilege escalation");
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>())
            Require(!name.StartsWith("Lab__", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Audit__", StringComparison.OrdinalIgnoreCase), "no host configuration: " + name);
        Require(!File.Exists("/var/run/docker.sock") && !Directory.Exists("/protected") && !Directory.Exists("/root/.aws"), "no host socket, protected storage or host credentials");
        try { File.WriteAllText("/app/forbidden", "probe"); throw new InvalidOperationException("Root filesystem is writable."); }
        catch (UnauthorizedAccessException) { Console.WriteLine("PASS root filesystem rejects writes"); }
        catch (IOException) { Console.WriteLine("PASS root filesystem rejects writes"); }
        Require(File.ReadAllLines("/proc/mounts").Any(x => { var fields = x.Split(' '); return fields[1] == "/" && fields[3].Split(',').Contains("ro"); }), "root mount is read-only");
        Require(!File.Exists("/workspace/own-run"), "previous run scratch is absent");
        File.WriteAllText("/workspace/own-run", "private synthetic scratch");
        Require(File.ReadAllText("/workspace/own-run") == "private synthetic scratch", "private writable run storage");
        foreach (var target in new[] { ("127.0.0.1", 5188), ("169.254.169.254", 80), ("10.0.0.1", 80), ("172.16.0.1", 80), ("192.168.0.1", 80), ("203.0.113.1", 443), ("::1", 5188), ("::1", 8080), ("fd00::1", 80), ("2001:db8::1", 443) })
        {
            using var socket = new Socket(IPAddress.Parse(target.Item1).AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
            try { await socket.ConnectAsync(IPAddress.Parse(target.Item1), target.Item2, timeout.Token); }
            catch (Exception e) when (e is SocketException or OperationCanceledException) { Console.WriteLine($"PASS direct TCP blocked: {target}"); continue; }
            throw new InvalidOperationException($"Direct TCP unexpectedly connected: {target}");
        }
        using (var dns = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
            // Valid query for example.invalid; a response would expose an unapproved DNS channel.
            byte[] query = [0x12,0x34,1,0,0,1,0,0,0,0,0,0,7,101,120,97,109,112,108,101,7,105,110,118,97,108,105,100,0,0,1,0,1];
            await dns.SendToAsync(query, SocketFlags.None, new IPEndPoint(IPAddress.Parse("127.0.0.11"), 53));
            try { await dns.ReceiveAsync(new byte[512], SocketFlags.None, timeout.Token); throw new InvalidOperationException("DNS channel is reachable."); }
            catch (OperationCanceledException) { Console.WriteLine("PASS direct DNS blocked"); }
        }
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri("http://127.0.0.1:8080"), Timeout = TimeSpan.FromSeconds(5) };
        foreach (var route in new[] { "/operator/audit", "/operator/runs", "/worker/proposals?destination=http://169.254.169.254", "/worker/../operator/audit", "/worker/%2e%2e/operator/audit" })
        {
            using var response = await client.GetAsync(route);
            Require(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed or HttpStatusCode.BadRequest, "broker denies route " + route);
        }
        using var negative = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(new Proposal(Operation.ReadDocument, "documents/task")));
        Require(negative.StatusCode == HttpStatusCode.Unauthorized, "broker forwards only to authenticated gateway");
        using var hostSpoof = new HttpRequestMessage(HttpMethod.Post, "/worker/proposals")
        {
            Content = JsonContent.Create(new ExecuteRequest(new Proposal(Operation.ReadDocument, "documents/task")))
        };
        hostSpoof.Headers.Host = "example.invalid";
        using var fixedDestination = await client.SendAsync(hostSpoof);
        Require(fixedDestination.StatusCode == HttpStatusCode.Unauthorized, "Host cannot select a different upstream");
        using var tunnel = new HttpRequestMessage(new HttpMethod("CONNECT"), "/worker/proposals");
        using var tunnelDenied = await client.SendAsync(tunnel);
        Require(tunnelDenied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.MethodNotAllowed, "no CONNECT tunnel");
        Console.WriteLine("Isolation probes passed; running authorized proposal scenario next.");
    }
    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + label);
        Console.WriteLine("PASS " + label);
    }
}
