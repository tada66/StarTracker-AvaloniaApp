using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Star_Tracker.Services;
using Star_Tracker.Services.Connection;
using Star_Tracker.Services.LiveView;
using Star_Tracker.ViewModels;

namespace Star_Tracker
{
    public partial class MainWindow : Window
    {
        private DeviceConnection? _connection;
        private NetworkCameraService? _cameraService;
        private NetworkMountService? _mountService;
        private LiveViewService? _liveViewService;
        private MainWindowViewModel? _viewModel;
        private string? _cameraConnectionString;
        private bool _isShuttingDown;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await ShowConnectionDialog();
        }

        private async void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            await ShutdownAsync();
        }

        private async Task ShutdownAsync()
        {
            if (_isShuttingDown)
                return;
            _isShuttingDown = true;

            try
            {
                if (_viewModel is not null)
                    _viewModel.IsConnected = false;

                await StopLiveView();

                if (_viewModel is not null)
                {
                    _viewModel.Shutdown();
                    _viewModel = null;
                }

                if (_connection is not null)
                {
                    _connection.ErrorReceived -= OnDeviceError;
                    _connection.ConnectionStateChanged -= OnConnectionStateChanged;
                    try { await _connection.DisconnectAsync(); } catch { }
                    _connection.Dispose();
                    _connection = null;
                }

                _cameraService = null;
                _mountService = null;
                DataContext = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Cleanup error: {ex.Message}");
            }
        }

        private async Task ShowConnectionDialog()
        {
            var dialog = new ConnectionDialog();
            await dialog.ShowDialog(this);

            if (dialog.Result is { State: ConnectionState.Connected } connection)
            {
                _connection = connection;
                await InitializeServices();
            }
            else
            {
                // User closed without connecting - close the app
                Close();
            }
        }

        private async Task InitializeServices()
        {
            if (_connection is null) return;

            // Create services backed by the real device connection
            _cameraService = new NetworkCameraService(_connection);
            _mountService = new NetworkMountService(_connection);

            // Create view model with real services
            _viewModel = new MainWindowViewModel(_cameraService, _mountService);
            DataContext = _viewModel;

            // Wire up live view restart/stop delegates so the ViewModel can manage live view lifecycle
            _viewModel.RestartLiveViewAsync = RestartLiveView;
            _viewModel.StopLiveViewAsync = StopLiveView;

            // Wire up camera connect/disconnect delegates
            _viewModel.ConnectCameraAsync = ConnectCamera;
            _viewModel.DisconnectCameraAsync = DisconnectCamera;

            // Set initial connection status
            _viewModel.ConnectionStatus = $"Connected to {_connection.Host}";
            _viewModel.IsConnected = true;

            // Subscribe to device errors
            _connection.ErrorReceived += OnDeviceError;

            // Listen for connection drops to show reconnect dialog
            _connection.ConnectionStateChanged += OnConnectionStateChanged;

            // Try to list cameras and connect to the first one
            try
            {
                var cameras = await _cameraService.ListCamerasAsync();
                if (cameras.Length > 0)
                {
                    _cameraConnectionString = cameras[0].ConnectionString;
                    var info = await _cameraService.ConnectCameraAsync(_cameraConnectionString);
                    _viewModel.IsCameraConnected = true;
                    Debug.WriteLine($"[MainWindow] Connected to camera: {info.Model}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Camera connect error: {ex.Message}");
                _viewModel.ShowError($"Camera connect error: {ex.Message}");
            }

            // Load camera settings into the ViewModel
            await _viewModel.LoadSettings();

            // Start live view
            await StartLiveView();
        }

        private async Task StartLiveView()
        {
            if (_isShuttingDown || _cameraService is null || _connection is null || _viewModel is null) return;

            try
            {
                // Open UDP socket BEFORE telling the device to start streaming
                _liveViewService = new LiveViewService();
                int localPort = _liveViewService.Start();
                _liveViewService.FrameReceived += OnLiveViewFrame;
                _liveViewService.FpsUpdated += OnLiveViewFpsUpdated;
                _liveViewService.NoDataReceived += OnLiveViewNoData;

                // Tell the device to start sending frames to our port
                await _cameraService.StartLiveviewAsync(localPort);
                _viewModel.IsLiveViewActive = true;

                Debug.WriteLine($"[MainWindow] Live view started on UDP port {localPort}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Live view start error: {ex.Message}");
                _liveViewService?.Dispose();
                _liveViewService = null;
            }
        }

        private async Task StopLiveView()
        {
            // Stop UI updates first so pending capture flows do not restart streaming during shutdown.
            if (_viewModel is not null)
                _viewModel.IsLiveViewActive = false;

            if (_liveViewService is not null)
            {
                _liveViewService.FrameReceived -= OnLiveViewFrame;
                _liveViewService.FpsUpdated -= OnLiveViewFpsUpdated;
                _liveViewService.NoDataReceived -= OnLiveViewNoData;
                _liveViewService.Dispose();
                _liveViewService = null;
            }

            if (_cameraService is not null)
            {
                try { await _cameraService.StopLiveviewAsync(); }
                catch (Exception ex) { Debug.WriteLine($"[MainWindow] Stop live view error: {ex.Message}"); }
            }
        }

        private async Task RestartLiveView()
        {
            await StartLiveView();
        }

        private async Task ConnectCamera()
        {
            if (_cameraService is null || _viewModel is null) return;

            try
            {
                // List cameras and connect to the first one
                var cameras = await _cameraService.ListCamerasAsync();
                if (cameras.Length > 0)
                {
                    _cameraConnectionString = cameras[0].ConnectionString;
                    var info = await _cameraService.ConnectCameraAsync(_cameraConnectionString);
                    _viewModel.IsCameraConnected = true;

                    // Load settings and start live view
                    await _viewModel.LoadSettings();
                    await StartLiveView();
                }
                else
                {
                    _viewModel.ShowError("No cameras found on the device");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Camera connect error: {ex.Message}");
                _viewModel.ShowError($"Camera connect error: {ex.Message}");
            }
        }

        private async Task DisconnectCamera()
        {
            if (_cameraService is null || _viewModel is null) return;

            try
            {
                // Stop live view first
                await StopLiveView();

                // Disconnect camera
                await _cameraService.DisconnectCameraAsync();
                _viewModel.IsCameraConnected = false;
                _viewModel.CameraName = "No Camera";

                // Clear the live view frame
                var old = _viewModel.LiveViewFrame;
                _viewModel.LiveViewFrame = null;
                old?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Camera disconnect error: {ex.Message}");
                _viewModel.ShowError($"Camera disconnect error: {ex.Message}");
            }
        }

        private void OnLiveViewFrame(Avalonia.Media.Imaging.Bitmap frame)
        {
            _viewModel?.UpdateLiveViewFrame(frame);
        }

        private void OnLiveViewFpsUpdated(double fps)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel is not null)
                    _viewModel.LiveViewFps = fps;
            });
        }

        private void OnLiveViewNoData()
        {
            _viewModel?.ShowError("No live view data received. Check firewall settings - inbound UDP may be blocked.");
        }

        private void OnDeviceError(string error)
        {
            _viewModel?.ShowError(error);
        }

        private void OnConnectionStateChanged(ConnectionState state)
        {
            if (_isShuttingDown)
                return;

            if (state == ConnectionState.Disconnected)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    if (_isShuttingDown)
                        return;

                    Debug.WriteLine("[MainWindow] Connection lost, showing reconnect dialog");

                    // Clean up old services
                    _liveViewService?.Dispose();
                    _liveViewService = null;

                    if (_viewModel is not null)
                    {
                        _viewModel.IsLiveViewActive = false;
                        _viewModel.ConnectionStatus = "Disconnected";
                        _viewModel.IsConnected = false;
                    }

                    if (_connection is not null)
                    {
                        _connection.ErrorReceived -= OnDeviceError;
                        _connection.ConnectionStateChanged -= OnConnectionStateChanged;
                        _connection.Dispose();
                    }
                    _connection = null;

                    // Show connection dialog again
                    await ShowConnectionDialog();
                });
            }
        }

        /// <summary>
        /// Called when a key is pressed in the shutter speed ComboBox.
        /// Commits the value when Enter is pressed, reading text directly from the control.
        /// </summary>
        private async void ShutterSpeedComboBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is ComboBox combo && DataContext is MainWindowViewModel vm)
            {
                var text = combo.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    vm.ShutterSpeedText = text;
                    await vm.CommitShutterSpeedText();
                }
            }
        }
    }
}
