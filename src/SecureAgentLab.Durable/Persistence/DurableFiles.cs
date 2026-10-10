using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureAgentLab.Durable.Recovery;

namespace SecureAgentLab.Durable.Persistence;

public static class DurableFiles
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    public static IDisposable Lock(string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        object gate = Gates.GetOrAdd(directory, _ => new object());
        Monitor.Enter(gate);
        try
        {
            long until = Environment.TickCount64 + 5000;
            while (true)
            {
                try
                {
                    return new Lease(gate, new FileStream(Path.Combine(directory, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                }
                catch (IOException) when (Environment.TickCount64 < until)
                {
                    Thread.Sleep(5);
                }
            }
        }
        catch
        {
            Monitor.Exit(gate);
            throw;
        }
    }

    public static void Write<T>(string path, T value)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path)) ?? throw new AuthorizationDependencyException("invalid_storage");
    public static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    public static string Hash<T>(T input) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
    private sealed class Lease(object gate, FileStream stream) : IDisposable
    {
        public void Dispose()
        {
            stream.Dispose();
            Monitor.Exit(gate);
        }
    }
}
