using System.IO;
using System.Net.WebSockets;
using System.Text;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrealityKEGCodeRunner;

internal class CrealityClient : IDisposable
{
    private readonly string _url;
    private static readonly ILog Log = LogManager.GetLogger(typeof(CrealityClient));
    private static readonly TimeSpan ConnectRetryInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private ClientWebSocket? _client;
    private readonly AsyncLock _sendLock = new();
    private readonly Timer _heartbeatTimer;
    private volatile bool _closed;

    public bool IsConnected => _client?.State == WebSocketState.Open;

    public event EventHandler<CrealityDataReceivedEventArgs>? DataReceived;
    public event EventHandler? IsConnectedChanged;

    public CrealityClient(string url)
    {
        _url = url;
        _heartbeatTimer = new Timer(OnHeartbeat, null, HeartbeatInterval, HeartbeatInterval);
    }

    private async void OnHeartbeat(object? state)
    {
        try
        {
            await SendAsync(
                new JObject
                {
                    ["ModeCode"] = "heart_beat",
                    ["msg"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                }.ToString(Formatting.None)
            );
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to send hearbeat", ex);
        }
    }

    public async Task Connect()
    {
        Log.Info("Connecting");

        var uri = new Uri(_url);
        var wsUri = $"ws://{uri.Host}:9999";

        try
        {
            _client?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug("Client dispose failed", ex);
        }

        _client = null;

        OnIsConnectedChanged();

        while (!_closed)
        {
            try
            {
                _client = new ClientWebSocket();

                await _client.ConnectAsync(new Uri(wsUri), default);

                break;
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to connect, retrying", ex);

                await Task.Delay(ConnectRetryInterval);
            }
        }

        Log.Info("Connected");

        OnIsConnectedChanged();

        StartReceiveLoop();

        await SendAsync(
            new JObject
            {
                ["method"] = "get",
                ["params"] = new JObject { ["reqProbedMatrix"] = 1 }
            }.ToString(Formatting.None)
        );
    }

    public async Task SendAsync(string message)
    {
        var buffer = Encoding.UTF8.GetBytes(message);

        await SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text);
    }

    private async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType)
    {
        var client = _client;
        if (client == null)
            throw new InvalidOperationException("Not connected");

        using (await _sendLock.LockAsync(SendTimeout))
        {
            await client.SendAsync(buffer, messageType, true, default);
        }
    }

    private async void StartReceiveLoop()
    {
        var client = _client;
        if (client == null)
            throw new InvalidOperationException("Not connected");

        var buffer = new byte[4096];
        using var stream = new MemoryStream();

        while (true)
        {
            try
            {
                stream.SetLength(0);

                while (true)
                {
                    var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), default);

                    stream.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                        break;
                }

                stream.Position = 0;

                using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);

                // ReSharper disable once MethodHasAsyncOverload
                HandleMessage(reader.ReadToEnd());
            }
            catch
            {
                break;
            }
        }

        if (!_closed)
            await Connect();
    }

    private void HandleMessage(string message)
    {
        OnDataReceived(new CrealityDataReceivedEventArgs(message));
    }

    public void Dispose()
    {
        _closed = true;

        _client?.Dispose();
        _client = null;

        OnIsConnectedChanged();

        _heartbeatTimer.Dispose();
        _sendLock.Dispose();
    }

    protected virtual void OnDataReceived(CrealityDataReceivedEventArgs e)
    {
        DataReceived?.Invoke(this, e);
    }

    protected virtual void OnIsConnectedChanged()
    {
        IsConnectedChanged?.Invoke(this, EventArgs.Empty);
    }
}
