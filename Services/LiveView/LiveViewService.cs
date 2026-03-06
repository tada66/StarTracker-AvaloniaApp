using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Star_Tracker.Services.LiveView;

/// <summary>
/// Receives JPEG frames via UDP from the device and exposes them as Avalonia Bitmaps.
/// Handles both unfragmented and fragmented datagrams per the protocol spec.
/// </summary>
public class LiveViewService : IDisposable
{
    private UdpClient? _udpClient;
    private CancellationTokenSource? _receiveCts;
    private readonly ConcurrentDictionary<uint, FragmentedFrame> _fragments = new();

    /// <summary>The local port the UDP socket is bound to.</summary>
    public int LocalPort { get; private set; }

    /// <summary>Fired on the receive thread when a complete JPEG frame is available.</summary>
    public event Action<Bitmap>? FrameReceived;

    /// <summary>Fired when the FPS counter updates (approximately once per second).</summary>
    public event Action<double>? FpsUpdated;

    /// <summary>Fired if no UDP datagrams are received within the initial timeout period.</summary>
    public event Action? NoDataReceived;

    /// <summary>Current frames per second.</summary>
    public double Fps { get; private set; }

    private int _frameCount;
    private DateTime _fpsWindowStart = DateTime.UtcNow;

    /// <summary>
    /// Open a UDP socket on a local port and start receiving frames.
    /// Returns the local port number (to pass to camera.liveview.start).
    /// </summary>
    public int Start(int preferredPort = 0)
    {
        Stop();

        // Explicitly use IPv4 (AddressFamily.InterNetwork) to ensure we receive
        // IPv4 UDP datagrams from the device. On some .NET/Windows configurations,
        // the default UdpClient may bind to IPv6 (::) instead of 0.0.0.0.
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, preferredPort));
        LocalPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

        _receiveCts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoop(_receiveCts.Token));

        Debug.WriteLine($"[LiveView] Listening on UDP port {LocalPort} (IPv4, 0.0.0.0:{LocalPort})");
        return LocalPort;
    }

    /// <summary>
    /// Stop receiving frames and close the socket.
    /// </summary>
    public void Stop()
    {
        _receiveCts?.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
        _fragments.Clear();
        Fps = 0;
    }

    private int _datagramCount;

    private async Task ReceiveLoop(CancellationToken ct)
    {
        if (_udpClient is null) return;

        // Start a timer to warn if no datagrams arrive within 5 seconds
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000, ct);
                if (_datagramCount == 0)
                {
                    Debug.WriteLine("[LiveView] WARNING: No UDP datagrams received after 5 seconds!");
                    Debug.WriteLine("[LiveView] Possible causes:");
                    Debug.WriteLine($"[LiveView]   - Windows Firewall blocking inbound UDP on port {LocalPort}");
                    Debug.WriteLine("[LiveView]   - Device and PC not on the same subnet");
                    Debug.WriteLine("[LiveView]   - camera.liveview.start failed or returned an error");
                    NoDataReceived?.Invoke();
                }
            }
            catch (OperationCanceledException) { }
        }, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync(ct);
                var data = result.Buffer;

                _datagramCount++;
                if (_datagramCount <= 5)
                {
                    Debug.WriteLine($"[LiveView] Datagram #{_datagramCount}: {data.Length} bytes from {result.RemoteEndPoint}");
                    if (data.Length >= 6)
                        Debug.WriteLine($"[LiveView]   Header bytes: {data[0]:X2} {data[1]:X2} {data[2]:X2} {data[3]:X2} {data[4]:X2} {data[5]:X2}");
                }

                if (data.Length < 6)
                    continue;

                ProcessDatagram(data);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (SocketException ex)
        {
            Debug.WriteLine($"[LiveView] Socket error: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            // Socket was closed during shutdown
        }
    }

    private void ProcessDatagram(byte[] data)
    {
        // Read sequence number (first 4 bytes, little-endian)
        uint seqNo = BitConverter.ToUInt32(data, 0);

        // Determine if fragmented:
        // Unfragmented datagrams have JPEG header (0xFF 0xD8) at bytes 4-5.
        // Fragmented datagrams have chunkIndex at byte 4 and chunkCount at byte 5,
        // where chunkCount > 1.
        bool isFragmented = false;

        if (data.Length > 6)
        {
            byte possibleChunkIndex = data[4];
            byte possibleChunkCount = data[5];

            // If chunkCount > 1 and the data doesn't start with JPEG magic
            if (possibleChunkCount > 1 && !(data[4] == 0xFF && data[5] == 0xD8))
            {
                isFragmented = true;
            }
        }

        if (isFragmented)
        {
            byte chunkIndex = data[4];
            byte chunkCount = data[5];

            var chunk = new byte[data.Length - 6];
            Buffer.BlockCopy(data, 6, chunk, 0, chunk.Length);

            var frame = _fragments.GetOrAdd(seqNo, _ => new FragmentedFrame(chunkCount));
            frame.AddChunk(chunkIndex, chunk);

            if (frame.IsComplete)
            {
                _fragments.TryRemove(seqNo, out _);
                var jpegData = frame.Assemble();
                EmitFrame(jpegData);
            }

            // Clean up old fragments (more than 10 sequence numbers behind)
            foreach (var key in _fragments.Keys)
            {
                if (seqNo - key > 10)
                    _fragments.TryRemove(key, out _);
            }
        }
        else
        {
            // Unfragmented: bytes 4..end are the JPEG
            var jpegData = new byte[data.Length - 4];
            Buffer.BlockCopy(data, 4, jpegData, 0, jpegData.Length);
            EmitFrame(jpegData);
        }
    }

    private void EmitFrame(byte[] jpegData)
    {
        try
        {
            using var ms = new MemoryStream(jpegData);
            var bitmap = new Bitmap(ms);

            // Update FPS counter
            _frameCount++;
            var elapsed = (DateTime.UtcNow - _fpsWindowStart).TotalSeconds;
            if (elapsed >= 1.0)
            {
                Fps = _frameCount / elapsed;
                _frameCount = 0;
                _fpsWindowStart = DateTime.UtcNow;
                FpsUpdated?.Invoke(Fps);
                Debug.WriteLine($"[LiveView] FPS: {Fps:F1}, frame size: {jpegData.Length} bytes");
            }

            FrameReceived?.Invoke(bitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LiveView] Failed to decode JPEG ({jpegData.Length} bytes): {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// Tracks chunks for a single fragmented frame.
    /// </summary>
    private class FragmentedFrame
    {
        private readonly byte[][] _chunks;
        private readonly bool[] _received;
        private readonly int _count;

        public FragmentedFrame(int chunkCount)
        {
            _count = chunkCount;
            _chunks = new byte[chunkCount][];
            _received = new bool[chunkCount];
        }

        public void AddChunk(int index, byte[] data)
        {
            if (index < _count)
            {
                _chunks[index] = data;
                _received[index] = true;
            }
        }

        public bool IsComplete
        {
            get
            {
                for (int i = 0; i < _count; i++)
                {
                    if (!_received[i]) return false;
                }
                return true;
            }
        }

        public byte[] Assemble()
        {
            int totalLength = 0;
            for (int i = 0; i < _count; i++)
                totalLength += _chunks[i].Length;

            var result = new byte[totalLength];
            int offset = 0;
            for (int i = 0; i < _count; i++)
            {
                Buffer.BlockCopy(_chunks[i], 0, result, offset, _chunks[i].Length);
                offset += _chunks[i].Length;
            }
            return result;
        }
    }
}
