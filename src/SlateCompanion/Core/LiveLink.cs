using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace SlateCompanion;

/// <summary>
/// Asks a running Savage Wind where Link is, over its local read-only companion socket (127.0.0.1; one request line,
/// one JSON reply line; see Savage Wind's docs/CONTROL.md). Only `pos` is ever sent. The connection is kept open
/// between questions, so each poll is one short line each way instead of a new connection.
/// </summary>
public static class LiveLink
{
    static readonly SemaphoreSlim gate = new(1, 1);
    static TcpClient? client;
    static NetworkStream? stream;
    static int clientPort;
    static readonly byte[] PosLine = "pos\n"u8.ToArray();
    static readonly byte[] buffer = new byte[4096];

    public static async Task<JsonObject> QueryAsync(int port, CancellationToken ct)
    {
        var result = new JsonObject { ["connected"] = false };
        if (port is < 1 or > 65535) { result["error"] = "bad port"; return result; }
        await gate.WaitAsync(ct);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(900));
            if (client == null || !client.Connected || clientPort != port)
            {
                Close();
                client = new TcpClient { NoDelay = true };
                await client.ConnectAsync("127.0.0.1", port, cts.Token);
                stream = client.GetStream();
                clientPort = port;
            }
            result["connected"] = true;
            await stream!.WriteAsync(PosLine, cts.Token);
            var line = await ReadLine(stream, cts.Token);
            var pos = line == null ? null : JsonNode.Parse(line);
            if (line == null) Close();  // the game closed the connection: reconnect next time
            if (pos?["ok"]?.GetValue<bool>() == true && pos["data"] is JsonObject data)
            {
                result["x"] = data["x"]?.DeepClone();
                result["y"] = data["y"]?.DeepClone();
                result["z"] = data["z"]?.DeepClone();
                result["stamina"] = data["stamina"]?.DeepClone();
                // The place line carries yaw and the hour: "swr:1 x=.. y=.. z=.. yaw=90.0 pitch=-10.0 hour=18.50".
                if (data["place"]?.GetValue<string>() is string place)
                    foreach (var part in place.Split(' '))
                    {
                        var kv = part.Split('=', 2);
                        if (kv.Length == 2 && kv[0] is "yaw" or "hour" &&
                            double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                            result[kv[0]] = v;
                    }
            }
            else result["error"] = pos?["error"]?.GetValue<string>() ?? "no reply";
        }
        // Savage Wind answers once per game frame, so a connection that gets no answer means its start screen is open.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Close();
            result["error"] = result["connected"]?.GetValue<bool>() == true
                ? "Savage Wind is open; waiting for you to load a save" : "timed out";
        }
        catch (SocketException) { Close(); result["connected"] = false; result["error"] = "Savage Wind isn't running, or its Slate Companion switch is off"; }
        catch (IOException e) { Close(); result["connected"] = false; result["error"] = e.Message; }
        catch (System.Text.Json.JsonException) { Close(); result["error"] = "unreadable reply"; }
        finally { gate.Release(); }
        return result;
    }

    static void Close()
    {
        try { stream?.Dispose(); client?.Dispose(); } catch (Exception) { }
        stream = null; client = null;
    }

    static async Task<string?> ReadLine(NetworkStream s, CancellationToken ct)
    {
        var line = new MemoryStream();
        while (line.Length < 1 << 20)
        {
            int n = await s.ReadAsync(buffer, ct);
            if (n == 0) return null;
            int nl = Array.IndexOf(buffer, (byte)'\n', 0, n);
            if (nl >= 0) { line.Write(buffer, 0, nl); return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length); }
            line.Write(buffer, 0, n);
        }
        return null;
    }
}
