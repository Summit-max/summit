using System.Net.Sockets;
using System.Text;

namespace Summit.Api;

/// <summary>
/// Cliente mínimo do protocolo Source RCON (usado pra trocar mapa/senha nos servidores
/// do pool "quente" sem precisar reiniciar a instância — ver docs/plano-aws.md).
/// </summary>
public class RconClient : IDisposable
{
    private const int SERVERDATA_AUTH = 3;
    private const int SERVERDATA_AUTH_RESPONSE = 2;
    private const int SERVERDATA_EXECCOMMAND = 2;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private int _nextId = 1;

    public async Task<bool> ConnectAndAuthAsync(string ip, int port, string password, int timeoutMs = 5000)
    {
        _client = new TcpClient();
        var connectTask = _client.ConnectAsync(ip, port);
        if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask)
            return false;

        _stream = _client.GetStream();
        var authId = _nextId++;
        await SendPacketAsync(authId, SERVERDATA_AUTH, password);

        // servidor manda um SERVERDATA_RESPONSE_VALUE vazio antes do AUTH_RESPONSE real —
        // só o pacote de tipo SERVERDATA_AUTH_RESPONSE (2) conta pra autenticação de fato
        for (var i = 0; i < 3; i++)
        {
            var (id, type, _) = await ReadPacketAsync(timeoutMs);
            if (type == SERVERDATA_AUTH_RESPONSE) return id == authId;
        }
        return false;
    }

    public async Task<string> ExecCommandAsync(string command, int timeoutMs = 5000)
    {
        if (_stream == null) throw new InvalidOperationException("Não conectado.");
        var id = _nextId++;
        await SendPacketAsync(id, SERVERDATA_EXECCOMMAND, command);
        var (_, _, body) = await ReadPacketAsync(timeoutMs);
        return body;
    }

    private async Task SendPacketAsync(int id, int type, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var packetSize = 4 + 4 + bodyBytes.Length + 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(packetSize);
        w.Write(id);
        w.Write(type);
        w.Write(bodyBytes);
        w.Write((byte)0);
        w.Write((byte)0);
        var bytes = ms.ToArray();
        await _stream!.WriteAsync(bytes);
    }

    private async Task<(int id, int type, string body)> ReadPacketAsync(int timeoutMs)
    {
        var sizeBytes = await ReadExactAsync(4, timeoutMs);
        var size = BitConverter.ToInt32(sizeBytes, 0);
        var rest = await ReadExactAsync(size, timeoutMs);
        var id = BitConverter.ToInt32(rest, 0);
        var type = BitConverter.ToInt32(rest, 4);
        var bodyLen = Math.Max(0, size - 4 - 4 - 2);
        var body = bodyLen > 0 ? Encoding.UTF8.GetString(rest, 8, bodyLen) : string.Empty;
        return (id, type, body);
    }

    private async Task<byte[]> ReadExactAsync(int count, int timeoutMs)
    {
        var buf = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var readTask = _stream!.ReadAsync(buf.AsMemory(offset, count - offset)).AsTask();
            if (await Task.WhenAny(readTask, Task.Delay(timeoutMs)) != readTask)
                throw new TimeoutException("RCON não respondeu a tempo.");
            var n = await readTask;
            if (n == 0) throw new IOException("Conexão RCON fechada pelo servidor.");
            offset += n;
        }
        return buf;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }
}
