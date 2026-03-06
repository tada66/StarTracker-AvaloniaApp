using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Star_Tracker.Models.Protocol;

namespace Star_Tracker.Services.Connection;

/// <summary>
/// Manages the WebSocket connection to the device.
/// Handles request/response correlation via UUIDs and dispatches events.
/// Uses a manual HTTP upgrade to avoid .NET's strict Sec-WebSocket-Accept validation,
/// which fails with some embedded device WebSocket servers.
/// </summary>
public class DeviceConnection : IDisposable
{
    private WebSocket? _ws;
    private TcpClient? _tcp;
    private CancellationTokenSource? _receiveCts;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WsMessage>> _pendingRequests = new();

    /// <summary>Fired for every event pushed by the server (type == "event").</summary>
    public event Action<WsMessage>? EventReceived;

    /// <summary>Fired when the connection state changes.</summary>
    public event Action<ConnectionState>? ConnectionStateChanged;

    /// <summary>Fired when an error event is received that is not correlated to a request.</summary>
    public event Action<string>? ErrorReceived;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public string? Host { get; private set; }
    public int WsPort { get; private set; } = 4400;
    public int UdpPort { get; private set; } = 4401;
    public int HttpPort { get; private set; } = 4402;

    /// <summary>
    /// Connect to the device WebSocket at the given host and port.
    /// Uses a manual HTTP upgrade handshake to tolerate non-standard server responses.
    /// </summary>
    public async Task ConnectAsync(string host, int wsPort = 4400, int udpPort = 4401, int httpPort = 4402, CancellationToken ct = default)
    {
        if (State == ConnectionState.Connected)
            await DisconnectAsync();

        Host = host;
        WsPort = wsPort;
        UdpPort = udpPort;
        HttpPort = httpPort;

        SetState(ConnectionState.Connecting);

        try
        {
            // Connect raw TCP
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(host, wsPort, ct);
            var stream = _tcp.GetStream();

            // Perform WebSocket HTTP upgrade handshake manually
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            var request = $"GET / HTTP/1.1\r\n" +
                          $"Host: {host}:{wsPort}\r\n" +
                          $"Upgrade: websocket\r\n" +
                          $"Connection: Upgrade\r\n" +
                          $"Sec-WebSocket-Key: {key}\r\n" +
                          $"Sec-WebSocket-Version: 13\r\n" +
                          $"\r\n";

            var requestBytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(requestBytes, ct);

            // Read HTTP response headers (tolerate any Sec-WebSocket-Accept value)
            var responseBuilder = new StringBuilder();
            var buffer = new byte[1];
            while (!responseBuilder.ToString().EndsWith("\r\n\r\n"))
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
                if (bytesRead == 0) throw new IOException("Connection closed during handshake");
                responseBuilder.Append((char)buffer[0]);
            }

            var response = responseBuilder.ToString();
            Debug.WriteLine($"[DeviceConnection] Handshake response:\n{response}");

            if (!response.StartsWith("HTTP/1.1 101"))
            {
                throw new WebSocketException($"Server rejected WebSocket upgrade: {response.Split('\r')[0]}");
            }

            // Create WebSocket from the upgraded stream (skip client-side Accept validation)
            _ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
            {
                IsServer = false,
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            });

            _receiveCts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoop(_receiveCts.Token));

            SetState(ConnectionState.Connected);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceConnection] Connect failed: {ex.Message}");
            _tcp?.Dispose();
            _tcp = null;
            SetState(ConnectionState.Disconnected);
            throw;
        }
    }

    /// <summary>
    /// Disconnect from the device.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _receiveCts?.Cancel();

        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
            }
            catch { /* ignore close errors */ }
        }

        _ws?.Dispose();
        _ws = null;
        _tcp?.Dispose();
        _tcp = null;

        // Fail all pending requests
        foreach (var kvp in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
                tcs.TrySetCanceled();
        }

        SetState(ConnectionState.Disconnected);
    }

    /// <summary>
    /// Send a request and wait for the correlated response.
    /// </summary>
    public async Task<WsMessage> SendRequestAsync(string topic, string action, object? payload = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected");

        var id = Guid.NewGuid().ToString();
        var request = new
        {
            type = "request",
            id,
            topic,
            action,
            payload = payload ?? new { }
        };

        var tcs = new TaskCompletionSource<WsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        var json = JsonSerializer.Serialize(request);
        var bytes = Encoding.UTF8.GetBytes(json);

        Debug.WriteLine($"[DeviceConnection] >> {json}");

        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);

        // Wait for response with timeout
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        catch (TaskCanceledException)
        {
            _pendingRequests.TryRemove(id, out _);
            throw new TimeoutException($"Request {action} timed out after {effectiveTimeout.TotalSeconds}s");
        }
    }

    /// <summary>
    /// Send a request and deserialize the response payload.
    /// </summary>
    public async Task<T> SendRequestAsync<T>(string topic, string action, object? payload = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var response = await SendRequestAsync(topic, action, payload, timeout, ct);

        if (response.Type == "error")
        {
            var error = DeserializePayload<ErrorPayload>(response);
            throw new DeviceException(error?.Error ?? "Unknown error");
        }

        return DeserializePayload<T>(response)
               ?? throw new InvalidOperationException($"Failed to deserialize response payload for {action}");
    }

    /// <summary>
    /// Send a request for commands that return a simple payload (fire and check for error).
    /// </summary>
    public async Task SendCommandAsync(string topic, string action, object? payload = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var response = await SendRequestAsync(topic, action, payload, timeout, ct);

        if (response.Type == "error")
        {
            var error = DeserializePayload<ErrorPayload>(response);
            throw new DeviceException(error?.Error ?? "Unknown error");
        }
    }

    // ── Internals ────────────────────────────────────────────────

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[1024 * 64];

        try
        {
            while (!ct.IsCancellationRequested && _ws is { State: WebSocketState.Open })
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.WriteLine("[DeviceConnection] Server closed connection");
                        SetState(ConnectionState.Disconnected);
                        return;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                var json = sb.ToString();
                Debug.WriteLine($"[DeviceConnection] << {json}");

                try
                {
                    var msg = JsonSerializer.Deserialize<WsMessage>(json);
                    if (msg is not null)
                        HandleMessage(msg);
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"[DeviceConnection] JSON parse error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException ex)
        {
            Debug.WriteLine($"[DeviceConnection] WebSocket error: {ex.Message}");
        }
        finally
        {
            SetState(ConnectionState.Disconnected);
        }
    }

    private void HandleMessage(WsMessage msg)
    {
        switch (msg.Type)
        {
            case "response":
            case "error":
                if (msg.Id is not null && _pendingRequests.TryRemove(msg.Id, out var tcs))
                {
                    tcs.TrySetResult(msg);
                }
                else if (msg.Type == "error")
                {
                    var error = DeserializePayload<ErrorPayload>(msg);
                    ErrorReceived?.Invoke(error?.Error ?? "Unknown error");
                }
                break;

            case "event":
                EventReceived?.Invoke(msg);
                break;
        }
    }

    private static T? DeserializePayload<T>(WsMessage msg)
    {
        if (msg.Payload is null)
            return default;

        return JsonSerializer.Deserialize<T>(msg.Payload.Value.GetRawText());
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        ConnectionStateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _ws?.Dispose();
        _tcp?.Dispose();

        foreach (var kvp in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
                tcs.TrySetCanceled();
        }
    }
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected
}

public class DeviceException : Exception
{
    public DeviceException(string message) : base(message) { }
}
