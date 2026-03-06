using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Star_Tracker.Services.Discovery;

/// <summary>
/// Discovers devices advertising _bpcontrol._tcp.local via mDNS.
/// Sends PTR queries to 224.0.0.251:5353 and listens for responses
/// on the mDNS multicast group.
/// </summary>
public class DeviceDiscoveryService : IDisposable
{
    public ObservableCollection<DiscoveredDevice> Devices { get; } = new();

    private CancellationTokenSource? _scanCts;

    /// <summary>
    /// Represents a discovered device on the network.
    /// </summary>
    public class DiscoveredDevice
    {
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public int WsPort { get; set; } = 4400;
        public int UdpPort { get; set; } = 4401;
        public int HttpPort { get; set; } = 4402;

        public override string ToString() => $"{Name} ({Host})";
    }

    /// <summary>
    /// Start scanning for devices. Sends mDNS queries periodically.
    /// Runs entirely on a background thread to avoid blocking the UI.
    /// </summary>
    public async Task ScanAsync(CancellationToken ct = default)
    {
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedCt = _scanCts.Token;

        await Task.Run(async () =>
        {
            UdpClient? udp = null;
            try
            {
                // Create a raw socket first, set options, bind to 5353, then wrap in UdpClient.
                // We must bind to port 5353 because mDNS responses are multicast to 224.0.0.251:5353,
                // not unicast back to the querier's ephemeral port.
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Bind(new IPEndPoint(IPAddress.Any, 5353));

                udp = new UdpClient { Client = socket };

                // Join multicast group for mDNS on ALL IPv4 network interfaces
                var mcastAddr = IPAddress.Parse("224.0.0.251");
                try
                {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up || !ni.SupportsMulticast)
                            continue;

                        foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                                continue;
                            try
                            {
                                socket.SetSocketOption(
                                    SocketOptionLevel.IP,
                                    SocketOptionName.AddMembership,
                                    new MulticastOption(mcastAddr, unicast.Address));
                            }
                            catch { /* interface may not support multicast */ }
                        }
                    }
                }
                catch
                {
                    // Fallback: join on default interface
                    try { udp.JoinMulticastGroup(mcastAddr); } catch { }
                }

                // Build mDNS PTR query for _bpcontrol._tcp.local
                var query = BuildMdnsQuery("_bpcontrol._tcp.local");
                var mcastEndpoint = new IPEndPoint(mcastAddr, 5353);

                // Collect all local IPv4 addresses to send query on each interface
                var localAddresses = new List<IPAddress>();
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;
                    foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                            localAddresses.Add(unicast.Address);
                    }
                }

                // Send query multiple times on each interface for reliability
                for (int i = 0; i < 3 && !linkedCt.IsCancellationRequested; i++)
                {
                    foreach (var localAddr in localAddresses)
                    {
                        try
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.IP,
                                SocketOptionName.MulticastInterface,
                                localAddr.GetAddressBytes());
                            await udp.SendAsync(query, query.Length, mcastEndpoint);
                        }
                        catch { /* send failed on this interface */ }
                    }
                    if (i < 2) await Task.Delay(500, linkedCt);
                }

                while (!linkedCt.IsCancellationRequested)
                {
                    try
                    {
                        var result = await udp.ReceiveAsync(linkedCt);
                        if (result.Buffer.Length >= 12)
                        {
                            ushort flags = ReadBE16(result.Buffer, 2);
                            bool isResp = (flags & 0x8000) != 0;
                            ushort anCnt = ReadBE16(result.Buffer, 6);
                            if (isResp && anCnt > 0)
                                ParseMdnsResponse(result.Buffer, result.RemoteEndPoint.Address.ToString());
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        // Timeout or socket error, keep looping
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { /* scan error */ }
            finally
            {
                try { udp?.Close(); } catch { }
                udp?.Dispose();
            }
        }, linkedCt);
    }

    /// <summary>
    /// Manually add a device by IP address using default ports.
    /// </summary>
    public DiscoveredDevice AddManualDevice(string host, int wsPort = 4400, int udpPort = 4401, int httpPort = 4402)
    {
        var device = new DiscoveredDevice
        {
            Name = host,
            Host = host,
            WsPort = wsPort,
            UdpPort = udpPort,
            HttpPort = httpPort,
        };
        Devices.Add(device);
        return device;
    }

    /// <summary>
    /// Builds a minimal mDNS query packet for a PTR record.
    /// All fields are big-endian per DNS wire format.
    /// </summary>
    private static byte[] BuildMdnsQuery(string serviceName)
    {
        var ms = new MemoryStream();

        // Header (12 bytes, all big-endian)
        WriteBE16(ms, 0);     // ID = 0
        WriteBE16(ms, 0);     // Flags = 0 (standard query)
        WriteBE16(ms, 1);     // QDCount = 1 question
        WriteBE16(ms, 0);     // ANCount = 0
        WriteBE16(ms, 0);     // NSCount = 0
        WriteBE16(ms, 0);     // ARCount = 0

        // QNAME: each label prefixed by its length, terminated by 0
        foreach (var label in serviceName.Split('.'))
        {
            ms.WriteByte((byte)label.Length);
            var labelBytes = Encoding.ASCII.GetBytes(label);
            ms.Write(labelBytes, 0, labelBytes.Length);
        }
        ms.WriteByte(0); // Root label

        WriteBE16(ms, 12);    // QTYPE = PTR (12)
        WriteBE16(ms, 1);     // QCLASS = IN (1)

        return ms.ToArray();
    }

    private static void WriteBE16(MemoryStream ms, ushort value)
    {
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)(value & 0xFF));
    }

    private static ushort ReadBE16(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    /// <summary>
    /// Parse an mDNS response packet. Looks for PTR, SRV, TXT, and A records
    /// related to _bpcontrol._tcp.local.
    /// </summary>
    private void ParseMdnsResponse(byte[] data, string sourceIp)
    {
        try
        {
            if (data.Length < 12) return;

            ushort qdCount = ReadBE16(data, 4);
            ushort anCount = ReadBE16(data, 6);
            ushort nsCount = ReadBE16(data, 8);
            ushort arCount = ReadBE16(data, 10);

            int offset = 12;

            // Skip question section
            for (int i = 0; i < qdCount && offset < data.Length; i++)
            {
                offset = SkipDnsName(data, offset);
                if (offset < 0 || offset + 4 > data.Length) return;
                offset += 4; // QTYPE + QCLASS
            }

            // Gather info from all resource record sections
            string? deviceName = null;
            string? resolvedIp = null;
            int wsPort = 4400;
            bool foundService = false;
            var txtProps = new Dictionary<string, string>();

            int totalRecords = anCount + nsCount + arCount;
            for (int i = 0; i < totalRecords && offset < data.Length; i++)
            {
                string recordName = ReadDnsName(data, offset, out int nameEnd);
                offset = nameEnd;
                if (offset + 10 > data.Length) break;

                ushort rtype = ReadBE16(data, offset);
                ushort rdLength = ReadBE16(data, offset + 8);
                offset += 10;

                if (offset + rdLength > data.Length) break;

                bool nameMatchesService = recordName.Contains("_bpcontrol", StringComparison.OrdinalIgnoreCase);

                switch (rtype)
                {
                    case 12: // PTR
                        string ptrTarget = ReadDnsName(data, offset, out _);
                        if (ptrTarget.Contains("_bpcontrol", StringComparison.OrdinalIgnoreCase))
                        {
                            foundService = true;
                            var dotIdx = ptrTarget.IndexOf("._bpcontrol", StringComparison.OrdinalIgnoreCase);
                            if (dotIdx > 0)
                                deviceName = ptrTarget[..dotIdx];
                        }
                        break;

                    case 33: // SRV
                        if (nameMatchesService && offset + 6 <= data.Length)
                        {
                            foundService = true;
                            wsPort = ReadBE16(data, offset + 4);
                        }
                        break;

                    case 16: // TXT
                        if (nameMatchesService)
                        {
                            foundService = true;
                            ParseTxtRecord(data, offset, rdLength, txtProps);
                        }
                        break;

                    case 1: // A (IPv4)
                        if (offset + 4 <= data.Length)
                            resolvedIp = $"{data[offset]}.{data[offset + 1]}.{data[offset + 2]}.{data[offset + 3]}";
                        break;
                }

                offset += rdLength;
            }

            if (!foundService) return;

            string ip = resolvedIp ?? sourceIp;

            if (txtProps.TryGetValue("name", out var txtName))
                deviceName = txtName;
            deviceName ??= ip;

            if (txtProps.TryGetValue("ws_port", out var wsPortStr) && int.TryParse(wsPortStr, out int parsedWsPort))
                wsPort = parsedWsPort;

            int udpPort = wsPort + 1;
            int httpPort = wsPort + 2;

            if (txtProps.TryGetValue("udp_port", out var udpPortStr) && int.TryParse(udpPortStr, out int parsedUdpPort))
                udpPort = parsedUdpPort;
            if (txtProps.TryGetValue("http_port", out var httpPortStr) && int.TryParse(httpPortStr, out int parsedHttpPort))
                httpPort = parsedHttpPort;

            var device = new DiscoveredDevice
            {
                Name = deviceName,
                Host = ip,
                WsPort = wsPort,
                UdpPort = udpPort,
                HttpPort = httpPort,
            };

            Dispatcher.UIThread.Post(() =>
            {
                if (!Devices.Any(d => d.Host == ip))
                    Devices.Add(device);
            });
        }
        catch { /* parse error */ }
    }

    /// <summary>
    /// Parse TXT record RDATA: sequence of length-prefixed strings "key=value".
    /// </summary>
    private static void ParseTxtRecord(byte[] data, int offset, int length, Dictionary<string, string> props)
    {
        int end = offset + length;
        while (offset < end)
        {
            int txtLen = data[offset++];
            if (txtLen == 0 || offset + txtLen > end) break;
            string txt = Encoding.ASCII.GetString(data, offset, txtLen);
            offset += txtLen;
            int eq = txt.IndexOf('=');
            if (eq > 0)
                props[txt[..eq].ToLowerInvariant()] = txt[(eq + 1)..];
        }
    }

    /// <summary>
    /// Read a DNS name from the packet, handling compression pointers.
    /// </summary>
    private static string ReadDnsName(byte[] data, int offset, out int newOffset)
    {
        var labels = new List<string>();
        bool jumped = false;
        newOffset = offset;

        int maxIterations = 64;
        while (maxIterations-- > 0 && offset < data.Length)
        {
            byte len = data[offset];
            if (len == 0)
            {
                if (!jumped) newOffset = offset + 1;
                break;
            }
            else if ((len & 0xC0) == 0xC0)
            {
                if (offset + 1 >= data.Length) break;
                int pointer = ((len & 0x3F) << 8) | data[offset + 1];
                if (!jumped) newOffset = offset + 2;
                jumped = true;
                offset = pointer;
            }
            else
            {
                offset++;
                if (offset + len > data.Length) break;
                labels.Add(Encoding.ASCII.GetString(data, offset, len));
                offset += len;
                if (!jumped) newOffset = offset;
            }
        }

        return string.Join(".", labels);
    }

    /// <summary>
    /// Skip a DNS name in the packet (don't need to decode it).
    /// Returns the offset after the name, or -1 on error.
    /// </summary>
    private static int SkipDnsName(byte[] data, int offset)
    {
        int maxIterations = 64;
        while (maxIterations-- > 0 && offset < data.Length)
        {
            byte len = data[offset];
            if (len == 0)
                return offset + 1;
            else if ((len & 0xC0) == 0xC0)
                return offset + 2;
            else
                offset += 1 + len;
        }
        return -1;
    }

    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}
