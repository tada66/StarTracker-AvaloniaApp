using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Star_Tracker.Models;
using Star_Tracker.Models.Protocol;
using Star_Tracker.Services;

namespace Star_Tracker.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ICameraService _cameraService;
    private readonly IMountService? _mountService;

    // ── Camera info ──────────────────────────────────────────────

    [ObservableProperty] private string _cameraName = "No Camera";
    [ObservableProperty] private int _batteryPercent;

    public string BatteryDisplay => $"Battery: {BatteryPercent}%";

    partial void OnBatteryPercentChanged(int value) =>
        OnPropertyChanged(nameof(BatteryDisplay));

    // ── Connection ───────────────────────────────────────────────

    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isCameraConnected;

    public string CameraConnectButtonText => IsCameraConnected ? "Disconnect" : "Connect";

    partial void OnIsCameraConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CameraConnectButtonText));
    }

    // ── Live View ────────────────────────────────────────────────

    [ObservableProperty] private Bitmap? _liveViewFrame;
    [ObservableProperty] private bool _isLiveViewActive;
    [ObservableProperty] private double _liveViewFps;
    [ObservableProperty] private bool _isMagnified;

    // ── ISO ──────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<string> _availableIsoValues = new();
    [ObservableProperty] private string? _selectedIso;

    partial void OnSelectedIsoChanged(string? value)
    {
        if (value is not null && _settingsLoaded)
            _ = _cameraService.SetIsoAsync(value);
    }

    // ── Shutter Speed ────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<ShutterSpeed> _availableShutterSpeeds = new();
    [ObservableProperty] private ShutterSpeed? _selectedShutterSpeed;

    /// <summary>
    /// Text displayed in the editable shutter speed ComboBox.
    /// When the user commits a value (presses Enter or leaves the field),
    /// we try to parse it as a shutter speed.
    /// </summary>
    [ObservableProperty] private string _shutterSpeedText = "";

    partial void OnSelectedShutterSpeedChanged(ShutterSpeed? value)
    {
        if (value is not null)
        {
            ShutterSpeedText = value.DisplayName;
            if (_settingsLoaded)
                _ = _cameraService.SetShutterSpeedAsync(value.ToProtocolString());
        }
        OnPropertyChanged(nameof(IsBulbMode));
        OnPropertyChanged(nameof(CaptureButtonText));
    }

    /// <summary>
    /// Called when the user commits text in the shutter speed ComboBox.
    /// Parses the text and either selects an existing item or creates a custom one.
    /// </summary>
    public async Task CommitShutterSpeedText()
    {
        if (string.IsNullOrWhiteSpace(ShutterSpeedText))
            return;

        // If the text already matches the currently selected item, nothing to do.
        // This prevents dropdown selections from being overridden by LostFocus.
        if (SelectedShutterSpeed is not null
            && string.Equals(SelectedShutterSpeed.DisplayName, ShutterSpeedText, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Check if the text matches a different existing item
        foreach (var speed in AvailableShutterSpeeds)
        {
            if (string.Equals(speed.DisplayName, ShutterSpeedText, StringComparison.OrdinalIgnoreCase))
            {
                SelectedShutterSpeed = speed;
                return;
            }
        }

        // Handle "Bulb" text explicitly (not a parseable number)
        if (ShutterSpeedText.Trim().Equals("Bulb", StringComparison.OrdinalIgnoreCase))
        {
            var bulb = ShutterSpeed.FromProtocolString("Bulb");
            AvailableShutterSpeeds.Add(bulb);
            SelectedShutterSpeed = bulb;
            await _cameraService.SetShutterSpeedAsync("Bulb");
            return;
        }

        // Try to parse as a custom shutter speed
        if (TryParseShutterSpeed(ShutterSpeedText, out double seconds))
        {
            var custom = new ShutterSpeed(seconds, isCustom: true);

            // Check if a speed with this exact time already exists
            bool found = false;
            for (int i = 0; i < AvailableShutterSpeeds.Count; i++)
            {
                if (Math.Abs(AvailableShutterSpeeds[i].Seconds - seconds) < 0.0001)
                {
                    SelectedShutterSpeed = AvailableShutterSpeeds[i];
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                if (seconds >= 1.0)
                {
                    int insertIndex = AvailableShutterSpeeds.Count;
                    for (int i = 0; i < AvailableShutterSpeeds.Count; i++)
                    {
                        if (AvailableShutterSpeeds[i].Seconds < seconds)
                        {
                            insertIndex = i;
                            break;
                        }
                    }
                    AvailableShutterSpeeds.Insert(insertIndex, custom);
                }
                else
                {
                    AvailableShutterSpeeds.Add(custom);
                }
                SelectedShutterSpeed = custom;
            }

            await _cameraService.SetShutterSpeedAsync(custom.ToProtocolString());
        }
        else
        {
            Debug.WriteLine($"[ViewModel] Invalid shutter speed: '{ShutterSpeedText}'");
            if (SelectedShutterSpeed is not null)
                ShutterSpeedText = SelectedShutterSpeed.DisplayName;
        }
    }

    /// <summary>
    /// Parses a shutter speed string. Accepts formats like:
    /// - "154", "154s", "154 s" (seconds)
    /// - "1/100", "1/100s" (fractions)
    /// - "0.01" (decimal seconds)
    /// Returns true if successfully parsed, with seconds as the output value.
    /// </summary>
    private static bool TryParseShutterSpeed(string input, out double seconds)
    {
        seconds = 0;
        input = input.Trim().ToLowerInvariant();

        if (input.EndsWith('s'))
            input = input[..^1].Trim();

        var fractionMatch = Regex.Match(input, @"^1/(\d+(?:\.\d+)?)$");
        if (fractionMatch.Success)
        {
            if (double.TryParse(fractionMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) && denominator > 0)
            {
                seconds = 1.0 / denominator;
                return true;
            }
        }

        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0)
        {
            seconds = value;
            return true;
        }

        return false;
    }

    // ── Aperture ─────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<string> _availableApertures = new();
    [ObservableProperty] private string? _selectedAperture;

    partial void OnSelectedApertureChanged(string? value)
    {
        if (value is not null && _settingsLoaded)
            _ = _cameraService.SetApertureAsync(value);
    }

    // ── Focus Mode ───────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<string> _availableFocusModes = new();
    [ObservableProperty] private string? _selectedFocusMode;

    partial void OnSelectedFocusModeChanged(string? value)
    {
        if (value is not null && _settingsLoaded)
            _ = _cameraService.SetFocusModeAsync(value);
    }

    // ── Focus stepping ───────────────────────────────────────────

    [ObservableProperty] private decimal _focusStepCount = 1;

    // ── Bulb mode ────────────────────────────────────────────────

    [ObservableProperty] private double _bulbDurationSeconds = 30;

    /// <summary>
    /// True when the selected shutter speed is "Bulb", meaning the user
    /// must specify a duration manually.
    /// </summary>
    public bool IsBulbMode => SelectedShutterSpeed is { IsBulb: true };

    /// <summary>
    /// Capture button label that reflects bulb mode and capturing state.
    /// </summary>
    public string CaptureButtonText => IsCapturing ? "Capturing..."
        : IsBulbMode ? "Bulb Capture"
        : "Capture image";

    // ── Repeat capture ───────────────────────────────────────────

    [ObservableProperty] private bool _repeatCapture;

    // ── Capture state ────────────────────────────────────────────

    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string? _lastCapturedFile;

    partial void OnIsCapturingChanged(bool value) =>
        OnPropertyChanged(nameof(CaptureButtonText));

    // ── Mount state ──────────────────────────────────────────────

    [ObservableProperty] private int _mountX;
    [ObservableProperty] private int _mountY;
    [ObservableProperty] private int _mountZ;
    [ObservableProperty] private float _mountTemperature;
    [ObservableProperty] private bool _motorsEnabled;
    [ObservableProperty] private bool _motorsPaused;
    [ObservableProperty] private bool _celestialTracking;
    [ObservableProperty] private int _fanSpeed;
    [ObservableProperty] private bool _referenceLost;
    [ObservableProperty] private decimal _mountMoveRate = 5;

    // Computed position displays (arcseconds → degrees)
    public string MountXDisplay => $"{MountX}″ ({MountX / 3600.0:F4}°)";
    public string MountYDisplay => $"{MountY}″ ({MountY / 3600.0:F4}°)";
    public string MountZDisplay => $"{MountZ}″ ({MountZ / 3600.0:F4}°)";
    public string MountTempDisplay => $"{MountTemperature:F1}°C";
    public string FanSpeedDisplay => $"{FanSpeed}%";
    public string MotorStateDisplay => !MotorsEnabled ? "Off" : MotorsPaused ? "Paused" : "Running";

    // Dynamic button text
    public string StopEnableButtonText => MotorsEnabled ? "STOP MOTORS" : "ENABLE MOTORS";
    public string PauseResumeButtonText => MotorsPaused ? "Resume movement" : "Pause movement";

    partial void OnMountXChanged(int value) => OnPropertyChanged(nameof(MountXDisplay));
    partial void OnMountYChanged(int value) => OnPropertyChanged(nameof(MountYDisplay));
    partial void OnMountZChanged(int value) => OnPropertyChanged(nameof(MountZDisplay));
    partial void OnMountTemperatureChanged(float value) => OnPropertyChanged(nameof(MountTempDisplay));
    partial void OnFanSpeedChanged(int value) => OnPropertyChanged(nameof(FanSpeedDisplay));
    partial void OnMotorsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(MotorStateDisplay));
        OnPropertyChanged(nameof(StopEnableButtonText));
    }
    partial void OnMotorsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(MotorStateDisplay));
        OnPropertyChanged(nameof(PauseResumeButtonText));
    }

    // ── Calibration / Alignment ──────────────────────────────────

    [ObservableProperty] private string _currentRa = "";
    [ObservableProperty] private string _currentDec = "";
    [ObservableProperty] private string _calibrationRa = "";
    [ObservableProperty] private string _calibrationDec = "";
    [ObservableProperty] private bool _isAligned;
    [ObservableProperty] private int _calibrationPointCount;

    // New Alignment Status Properties
    [ObservableProperty] private string _alignmentQuality = "";
    [ObservableProperty] private string _alignmentQualityColor = "#FFFFFF"; // Default white
    [ObservableProperty] private double _averageResidualArcmin;
    [ObservableProperty] private double _maxPairErrorDeg;
    [ObservableProperty] private int _activeStarCount;
    [ObservableProperty] private int _rejectedStarCount;

    public string CalibrationStatusText => IsAligned
        ? $"Aligned ({CalibrationPointCount} pts)"
        : CalibrationPointCount > 0
            ? $"Calibrating ({CalibrationPointCount} pts)"
            : "Not Calibrated";

    public string AlignmentResidualsText => CalibrationPointCount >= 2 
        ? $"Err: {AverageResidualArcmin:F1}' / Max: {MaxPairErrorDeg:F3}°"
        : "Need >= 2 stars";

    public string AlignmentStarsText => CalibrationPointCount > 0
        ? $"Stars: {ActiveStarCount} active / {RejectedStarCount} rej"
        : "";

    public bool HasAlignmentQuality => !string.IsNullOrEmpty(AlignmentQuality) && CalibrationPointCount >= 2;

    partial void OnIsAlignedChanged(bool value) => OnPropertyChanged(nameof(CalibrationStatusText));
    partial void OnCalibrationPointCountChanged(int value) 
    {
        OnPropertyChanged(nameof(CalibrationStatusText));
        OnPropertyChanged(nameof(AlignmentResidualsText));
        OnPropertyChanged(nameof(AlignmentStarsText));
        OnPropertyChanged(nameof(HasAlignmentQuality));
    }

    partial void OnAverageResidualArcminChanged(double value) => OnPropertyChanged(nameof(AlignmentResidualsText));
    partial void OnMaxPairErrorDegChanged(double value) => OnPropertyChanged(nameof(AlignmentResidualsText));
    partial void OnActiveStarCountChanged(int value) => OnPropertyChanged(nameof(AlignmentStarsText));
    partial void OnRejectedStarCountChanged(int value) => OnPropertyChanged(nameof(AlignmentStarsText));
    partial void OnAlignmentQualityChanged(string value) => OnPropertyChanged(nameof(HasAlignmentQuality));

    private void UpdateAlignmentStatus(AlignmentStatusPayload status)
    {
        IsAligned = status.IsAligned;
        CalibrationPointCount = status.PointCount;
        
        AverageResidualArcmin = status.AverageResidualArcmin ?? 0;
        MaxPairErrorDeg = status.MaxPairErrorDeg ?? 0;
        ActiveStarCount = status.ActiveStarCount ?? 0;
        RejectedStarCount = status.RejectedCount ?? 0;
        
        AlignmentQuality = status.Quality ?? "";
        AlignmentQualityColor = AlignmentQuality switch
        {
            "EXCELLENT" => "#44FF44", // Green
            "OK" => "#FFFF44",        // Yellow
            "MARGINAL" => "#FFAA00",  // Orange
            "REJECTED" => "#FF4444",  // Red
            _ => "#FFFFFF"            // White fallback
        };

        if (AlignmentQuality == "MARGINAL")
        {
            ShowError("Alignment is MARGINAL. Consider re-doing alignment with better stars.");
        }
        else if (AlignmentQuality == "REJECTED")
        {
            ShowError("Alignment REJECTED due to high error. Please discard and restart alignment.");
        }
    }

    // ── Error notifications ──────────────────────────────────────

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isErrorVisible;

    private CancellationTokenSource? _errorDismissCts;

    public void ShowError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ErrorMessage = message;
            IsErrorVisible = true;

            // Auto-dismiss after 6 seconds
            _errorDismissCts?.Cancel();
            _errorDismissCts = new CancellationTokenSource();
            var ct = _errorDismissCts.Token;
            _ = Task.Delay(6000, ct).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() => IsErrorVisible = false);
            }, TaskContinuationOptions.NotOnCanceled);
        });
    }

    [RelayCommand]
    private void DismissError()
    {
        _errorDismissCts?.Cancel();
        IsErrorVisible = false;
    }

    [RelayCommand]
    private void DismissReferenceWarning()
    {
        ReferenceLost = false;
    }

    // ── Live view restart delegate ───────────────────────────────

    /// <summary>
    /// Set by the code-behind to allow the ViewModel to restart live view after capture.
    /// </summary>
    public Func<Task>? RestartLiveViewAsync { get; set; }

    /// <summary>
    /// Set by the code-behind to allow the ViewModel to stop live view before capture.
    /// </summary>
    public Func<Task>? StopLiveViewAsync { get; set; }

    // ── Camera connect/disconnect delegates ──────────────────────

    /// <summary>
    /// Set by the code-behind to handle camera connection.
    /// </summary>
    public Func<Task>? ConnectCameraAsync { get; set; }

    /// <summary>
    /// Set by the code-behind to handle camera disconnection.
    /// </summary>
    public Func<Task>? DisconnectCameraAsync { get; set; }

    // ── Internal state ───────────────────────────────────────────

    private bool _settingsLoaded;
    private bool _isDisposed;

    // ── Constructor ──────────────────────────────────────────────

    public MainWindowViewModel(ICameraService cameraService, IMountService? mountService = null)
    {
        _cameraService = cameraService;
        _mountService = mountService;

        // Subscribe to camera events
        _cameraService.CameraStatusReceived += OnCameraStatus;
        _cameraService.CaptureCompleteReceived += OnCaptureComplete;

        // Subscribe to mount events
        if (_mountService is not null)
        {
            _mountService.MountStatusReceived += OnMountStatus;
            _mountService.MountPositionReceived += OnMountPosition;
            _mountService.ReferenceLost += OnReferenceLost;
        }
    }

    // ── Event handlers ───────────────────────────────────────────

    private void OnCameraStatus(CameraStatusEventPayload status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (status.Model is not null)
                CameraName = status.Model;
            if (status.Battery is not null && int.TryParse(status.Battery.TrimEnd('%'), out int bat))
                BatteryPercent = bat;
        });
    }

    private void OnCaptureComplete(CaptureCompleteEventPayload capture)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LastCapturedFile = capture.Url;
            Debug.WriteLine($"[ViewModel] Capture complete: {capture.Url}");
        });
    }

    private void OnMountStatus(MountStatusEventPayload status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MountX = status.X;
            MountY = status.Y;
            MountZ = status.Z;
            MountTemperature = status.Temperature;
            MotorsEnabled = status.MotorsEnabled;
            MotorsPaused = status.MotorsPaused;
            CelestialTracking = status.CelestialTracking;
            FanSpeed = status.FanSpeed;
        });
    }

    private void OnMountPosition(MountPositionPayload pos)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MountX = pos.X;
            MountY = pos.Y;
            MountZ = pos.Z;
        });
    }

    private void OnReferenceLost()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ReferenceLost = true;
            Debug.WriteLine("[ViewModel] WARNING: Mount reference lost!");
            // Don't auto-dismiss - this is a persistent warning the user must acknowledge
        });
    }

    /// <summary>
    /// Call from LiveViewService.FrameReceived to update the live view bitmap on the UI thread.
    /// </summary>
    public void UpdateLiveViewFrame(Bitmap frame)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var old = LiveViewFrame;
            LiveViewFrame = frame;
            old?.Dispose();
        });
    }

    // ── Load settings ────────────────────────────────────────────

    /// <summary>
    /// Loads settings from the camera service and initial mount status. Call once after construction.
    /// </summary>
    [RelayCommand]
    public async Task LoadSettings()
    {
        try
        {
            if (_mountService is not null)
            {
                try
                {
                    var status = await _mountService.AlignmentStatusAsync();
                    UpdateAlignmentStatus(status);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ViewModel] Failed to load alignment status: {ex.Message}");
                }
            }

            var settings = await _cameraService.GetSettingsAsync();

            CameraName = settings.CameraName;
            BatteryPercent = settings.BatteryPercent;

            AvailableIsoValues = settings.AvailableIsoValues;
            AvailableShutterSpeeds = settings.AvailableShutterSpeeds;
            AvailableApertures = settings.AvailableApertures;
            AvailableFocusModes = settings.AvailableFocusModes;

            SelectedIso = settings.SelectedIso;
            SelectedShutterSpeed = settings.SelectedShutterSpeed;
            SelectedAperture = settings.SelectedAperture;
            SelectedFocusMode = settings.SelectedFocusMode;

            _settingsLoaded = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Failed to load settings: {ex.Message}");
        }
    }

    // ── Camera commands ──────────────────────────────────────────

    [RelayCommand]
    private async Task ConnectDisconnectCamera()
    {
        try
        {
            if (IsCameraConnected)
            {
                if (DisconnectCameraAsync is not null)
                    await DisconnectCameraAsync();
            }
            else
            {
                if (ConnectCameraAsync is not null)
                    await ConnectCameraAsync();
            }
        }
        catch (Exception ex)
        {
            ShowError($"Camera connection error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Capture()
    {
        if (IsCapturing) return;

        try
        {
            IsCapturing = true;

            do
            {
                // Stop live view before capture (it stops on the device side anyway)
                if (StopLiveViewAsync is not null)
                {
                    try { await StopLiveViewAsync(); } catch { }
                }

                CaptureResultPayload result;
                if (IsBulbMode)
                {
                    if (BulbDurationSeconds <= 0)
                    {
                        ShowError("Bulb duration must be greater than 0");
                        break;
                    }
                    result = await _cameraService.CaptureBulbAsync(BulbDurationSeconds);
                }
                else
                {
                    result = await _cameraService.CaptureAsync();
                }

                LastCapturedFile = result.Filename;

                // Restart live view after capture
                if (RestartLiveViewAsync is not null)
                {
                    try { await RestartLiveViewAsync(); } catch { }
                }
            }
            while (RepeatCapture);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Capture failed: {ex.Message}");
            ShowError($"Capture failed: {ex.Message}");
        }
        finally
        {
            IsCapturing = false;

            // Ensure live view is running even if capture failed
            if (RestartLiveViewAsync is not null && !IsLiveViewActive)
            {
                try { await RestartLiveViewAsync(); } catch { }
            }
        }
    }

    [RelayCommand]
    private async Task Magnify()
    {
        try
        {
            if (IsMagnified)
            {
                await _cameraService.MagnifyOffAsync();
                IsMagnified = false;
            }
            else
            {
                await _cameraService.MagnifyAsync();
                IsMagnified = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Magnify failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MagnifyOff()
    {
        try
        {
            await _cameraService.MagnifyOffAsync();
            IsMagnified = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] MagnifyOff failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task FocusCloser()
    {
        try
        {
            await _cameraService.FocusAsync("closer", (int)FocusStepCount);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Focus closer failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task FocusFarther()
    {
        try
        {
            await _cameraService.FocusAsync("further", (int)FocusStepCount);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Focus farther failed: {ex.Message}");
        }
    }

    // ── Mount commands ───────────────────────────────────────────

    [RelayCommand]
    private async Task StopMotors()
    {
        if (_mountService is null) return;
        try
        {
            if (MotorsEnabled)
            {
                await _mountService.StopAsync();
                Debug.WriteLine("[ViewModel] Motors stopped");
            }
            else
            {
                await _mountService.ResumeAsync();
                Debug.WriteLine("[ViewModel] Motors enabled (resumed)");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Stop/Enable motors failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PauseResumeMovement()
    {
        if (_mountService is null) return;
        try
        {
            if (MotorsPaused)
            {
                await _mountService.ResumeAsync();
                MotorsPaused = false;
            }
            else
            {
                await _mountService.PauseAsync();
                MotorsPaused = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Pause/Resume failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MountMoveUp()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("x", (int)MountMoveRate * 30);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount move up failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MountMoveDown()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("x", -(int)MountMoveRate * 30);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount move down failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MountMoveLeft()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("z", (int)MountMoveRate * 30);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount move left failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MountMoveRight()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("z", -(int)MountMoveRate * 30);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount move right failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MountMoveUpCoarse()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("x", (int)MountMoveRate * 15000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount coarse move up failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MountMoveDownCoarse()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("x", -(int)MountMoveRate * 15000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount coarse move down failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MountMoveLeftCoarse()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("z", (int)MountMoveRate * 15000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount coarse move left failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MountMoveRightCoarse()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("z", -(int)MountMoveRate * 15000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount coarse move right failed: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private async Task MountHome()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveStaticAsync("z", 0);
            await _mountService.MoveStaticAsync("y", 0);
            await _mountService.MoveStaticAsync("x", 0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount coarse move left failed: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private async Task MountTiltClockwise()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("y", (int)MountMoveRate * 15000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount coarse move right failed: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private async Task MountTiltAnticlockwise()
    {
        if (_mountService is null) return;
        try
        {
            await _mountService.MoveRelativeAsync("y", -(int)MountMoveRate * 15000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Mount coarse move right failed: {ex.Message}");
        }
    }

    // ── Calibration / Alignment commands ─────────────────────────

    [RelayCommand]
    private async Task GoTrack()
    {
        if (_mountService is null) return;
        try
        {
            if (!TryParseCoordinate(CurrentRa, out double ra) || !TryParseCoordinate(CurrentDec, out double dec))
            {
                ShowError("Invalid RA or Dec format");
                return;
            }
            await _mountService.StartTrackingAsync(ra, dec);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] GoTo failed: {ex.Message}");
            ShowError($"Go-to failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StartAlignment()
    {
        if (_mountService is null) return;
        try
        {
            var initStatus = await _mountService.AlignmentInitAsync();
            IsAligned = false;
            CalibrationPointCount = initStatus.PointCount;
            AlignmentQuality = "";
            AlignmentQualityColor = "#FFFFFF";
            AverageResidualArcmin = 0;
            MaxPairErrorDeg = 0;
            ActiveStarCount = 0;
            RejectedStarCount = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Init alignment failed: {ex.Message}");
            ShowError($"Init alignment failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddCalibrationPoint()
    {
        if (_mountService is null) return;
        try
        {
            if (!TryParseCoordinate(CalibrationRa, out double ra) || !TryParseCoordinate(CalibrationDec, out double dec))
            {
                ShowError("Invalid RA or Dec format");
                return;
            }
            var status = await _mountService.AlignmentAddStarAsync(ra, dec);
            UpdateAlignmentStatus(status);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Add calibration point failed: {ex.Message}");
            ShowError($"Add calibration point failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task FinalizeCalibration()
    {
        if (_mountService is null) return;
        try
        {
            var status = await _mountService.AlignmentStatusAsync();
            UpdateAlignmentStatus(status);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Finalize calibration failed: {ex.Message}");
            ShowError($"Finalize calibration failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DiscardCalibration()
    {
        if (_mountService is null) return;
        try
        {
            var initStatus = await _mountService.AlignmentInitAsync(); // Reset alignment
            IsAligned = false;
            CalibrationPointCount = initStatus.PointCount;
            AlignmentQuality = "";
            AlignmentQualityColor = "#FFFFFF";
            AverageResidualArcmin = 0;
            MaxPairErrorDeg = 0;
            ActiveStarCount = 0;
            RejectedStarCount = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ViewModel] Discard calibration failed: {ex.Message}");
            ShowError($"Discard calibration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a coordinate string in decimal degrees or DD:MM:SS.S / HH:MM:SS.SS format.
    /// Also supports formats like +49° 18′ 47.7602″ and 13h 47m 32.43776s.
    /// </summary>
    private static bool TryParseCoordinate(string input, out double degrees)
    {
        degrees = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        input = input.Trim();

        // Try decimal first
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out degrees))
            return true;

        // Try formats with separators (:, spaces, °, ', ", h, m, s)
        var match = Regex.Match(input, @"^([+-]?)\s*(\d+)[°h:\s]+(\d+)['′m:\s]+(\d+(?:\.\d+)?)[""″s\s]*$");
        if (match.Success)
        {
            int sign = match.Groups[1].Value == "-" ? -1 : 1;
            double d = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            double m = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            double s = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            degrees = sign * (d + m / 60.0 + s / 3600.0);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Releases VM-held resources and unsubscribes from service events during app shutdown.
    /// Safe to call multiple times.
    /// </summary>
    public void Shutdown()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        _errorDismissCts?.Cancel();
        _errorDismissCts?.Dispose();
        _errorDismissCts = null;

        _cameraService.CameraStatusReceived -= OnCameraStatus;
        _cameraService.CaptureCompleteReceived -= OnCaptureComplete;

        if (_mountService is not null)
        {
            _mountService.MountStatusReceived -= OnMountStatus;
            _mountService.MountPositionReceived -= OnMountPosition;
            _mountService.ReferenceLost -= OnReferenceLost;
        }

        RestartLiveViewAsync = null;
        StopLiveViewAsync = null;
        ConnectCameraAsync = null;
        DisconnectCameraAsync = null;

        var old = LiveViewFrame;
        LiveViewFrame = null;
        old?.Dispose();
    }
}
