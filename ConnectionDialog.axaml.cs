using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Star_Tracker.Services.Connection;
using Star_Tracker.Services.Discovery;

namespace Star_Tracker;

/// <summary>
/// Modal dialog that scans for devices via mDNS and allows manual IP entry.
/// Returns a connected DeviceConnection when the user selects a device,
/// or null if the dialog is closed without connecting.
/// </summary>
public partial class ConnectionDialog : Window
{
    private readonly DeviceDiscoveryService _discovery = new();
    private readonly DeviceConnection _connection = new();
    private CancellationTokenSource? _scanCts;

    /// <summary>The connected DeviceConnection, or null if cancelled.</summary>
    public DeviceConnection? Result { get; private set; }

    public ConnectionDialog()
    {
        InitializeComponent();
        DeviceList.ItemsSource = _discovery.Devices;
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        await StartScanning();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _scanCts?.Cancel();
        _discovery.Dispose();
    }

    private async Task StartScanning()
    {
        _scanCts = new CancellationTokenSource();
        ScanningPanel.IsVisible = true;

        try
        {
            // Scan for 10 seconds
            await _discovery.ScanAsync(_scanCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConnectionDialog] Scan error: {ex.Message}");
        }
        finally
        {
            if (!_scanCts.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ScanningPanel.IsVisible = false;
                    if (_discovery.Devices.Count == 0)
                        StatusText.Text = "No devices found. Enter an IP address manually.";
                });
            }
        }
    }

    private async void DeviceList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DeviceList.SelectedItem is DeviceDiscoveryService.DiscoveredDevice device)
        {
            await ConnectToDevice(device.Host, device.WsPort, device.UdpPort, device.HttpPort);
        }
    }

    private async void ConnectManual_Click(object? sender, RoutedEventArgs e)
    {
        await ConnectManualIp();
    }

    private async void ManualIpBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await ConnectManualIp();
        }
    }

    private async Task ConnectManualIp()
    {
        var ip = ManualIpBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            StatusText.Text = "Please enter an IP address.";
            return;
        }

        // Support host:port format
        int wsPort = 4400, udpPort = 4401, httpPort = 4402;
        if (ip.Contains(':'))
        {
            var parts = ip.Split(':');
            ip = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], out int port))
            {
                wsPort = port;
                udpPort = port + 1;
                httpPort = port + 2;
            }
        }

        await ConnectToDevice(ip, wsPort, udpPort, httpPort);
    }

    private async Task ConnectToDevice(string host, int wsPort, int udpPort, int httpPort)
    {
        _scanCts?.Cancel();
        SetUiConnecting(true);
        StatusText.Text = $"Connecting to {host}:{wsPort}...";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _connection.ConnectAsync(host, wsPort, udpPort, httpPort, cts.Token);

            if (_connection.State == ConnectionState.Connected)
            {
                HeaderText.Text = "Connected!";
                StatusText.Text = $"Connected to {host}";
                Result = _connection;

                // Small delay so the user sees "Connected!" before the dialog closes
                await Task.Delay(400);
                Close();
            }
            else
            {
                StatusText.Text = "Connection failed. Check the address and try again.";
                SetUiConnecting(false);
            }
        }
        catch (TimeoutException)
        {
            StatusText.Text = "Connection timed out. Check the address and try again.";
            SetUiConnecting(false);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Connection failed: {ex.Message}";
            Debug.WriteLine($"[ConnectionDialog] Connect error: {ex}");
            SetUiConnecting(false);
        }
    }

    private void SetUiConnecting(bool connecting)
    {
        ConnectManualButton.IsEnabled = !connecting;
        ManualIpBox.IsEnabled = !connecting;
        DeviceList.IsEnabled = !connecting;
        if (connecting)
            HeaderText.Text = "Connecting...";
        else
            HeaderText.Text = "Looking for device...";
    }
}
