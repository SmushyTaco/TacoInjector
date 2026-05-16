using System.Globalization;
using TacoInjector.Models;
using TacoInjector.Services;
using TacoInjector.Services.Windows;

namespace TacoInjector;

public partial class MainPage
{
    private readonly TacoInjectorService _injectorService;
    private readonly SettingsService _settingsService;
    private readonly TrayIconService _trayIconService;

    private const string DllPathPlaceholder = "Click \"Select DLL\" to select the dll file";
    private const string OldDllPathPlaceholder = "Click \"Select\" to select the dll file";
    private const double DefaultDelaySeconds = 5;

    private CancellationTokenSource? _autoInjectCancellation;
    private InjectorSettings _settings = new();

    public MainPage()
    {
        InitializeComponent();

        _settingsService = new SettingsService();
        _injectorService = new TacoInjectorService(
            new NativeInjectionBackend(),
            _settingsService,
            new FilePermissionService());

        _trayIconService = TrayIconService.Instance;
        _trayIconService.RestoreRequested += OnTrayRestoreRequested;
        _trayIconService.HideRequested += OnTrayHideRequested;
        _trayIconService.ExitRequested += OnTrayExitRequested;

        try
        {
            _trayIconService.EnsureCreated();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Tray unavailable: {ex.Message}";
        }
    }

    private void OnTrayRestoreRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            WindowChrome.ShowAndActivate();
            StatusLabel.Text = "Ready  •  restored from tray";
        });
    }

    private void OnTrayHideRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = "Hidden to tray";
            _trayIconService.HideMainWindowToTray();
        });
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(ExitApplication);
    }

    private void OnWindowDragPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                WindowChrome.BeginDrag();
                break;

            case GestureStatus.Running:
                WindowChrome.DragBy(e.TotalX, e.TotalY);
                break;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            _settings = await _settingsService.LoadAsync();
            ApplySettingsToUi(_settings);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Config load failed: {ex.Message}";
        }
    }

    private async void OnInjectClicked(object? sender, EventArgs e)
    {
        if (!IsValidDllPath(DllPathEntry.Text))
        {
            StatusLabel.Text = "Select a valid .dll file first.";
            UpdateInjectButtonState();
            return;
        }

        try
        {
            var settings = ReadSettingsFromUi();
            var result = await _injectorService.InjectOnceAsync(settings);

            StatusLabel.Text = result.Message;
            _settings = settings;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Inject failed: {ex.Message}";
        }
    }

    private async void OnSelectClicked(object? sender, EventArgs e)
    {
        try
        {
            var dllFileType = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = [".dll"]
                });

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select the .dll file",
                FileTypes = dllFileType
            });

            if (result is null)
                return;

            DllPathEntry.Text = result.FullPath;
            UpdateInjectButtonState();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"File picker failed: {ex.Message}";
        }
    }

    private void OnDllPathChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateInjectButtonState();
    }

    private void OnHideClicked(object? sender, EventArgs e)
    {
        StatusLabel.Text = "Hidden to tray";
        _trayIconService.HideMainWindowToTray();
    }

    private void OnMinimizeClicked(object? sender, EventArgs e)
    {
        WindowChrome.Minimize();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        ExitApplication();
    }

    private void ExitApplication()
    {
        StopAutoInject();
        _trayIconService.Dispose();
        Application.Current?.Quit();
    }

    private void OnCustomTargetChanged(object? sender, CheckedChangedEventArgs e)
    {
        ProcessNameEntry.IsEnabled = e.Value;

        if (!e.Value)
        {
            ProcessNameEntry.Text = "minecraft.windows.exe";
        }
    }

    private async void OnAutoInjectChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (e.Value)
        {
            if (!IsValidDllPath(DllPathEntry.Text))
            {
                AutoInjectCheckBox.IsChecked = false;
                StatusLabel.Text = "Select a valid .dll file first.";
                UpdateInjectButtonState();
                return;
            }

            await StartAutoInjectAsync();
        }
        else
        {
            StopAutoInject();
        }
    }

    private async Task StartAutoInjectAsync()
    {
        StopAutoInject();

        var settings = ReadSettingsFromUi();
        DelayEntry.Text = FormatDelaySeconds(settings.DelaySeconds);

        _settings = settings;
        var cancellation = new CancellationTokenSource();
        _autoInjectCancellation = cancellation;

        SetControlsForAutoInject(isRunning: true);

        await _settingsService.SaveAsync(settings);

        _ = Task.Run(async () =>
        {
            try
            {
                await _injectorService.RunAutoInjectAsync(
                    settings,
                    UpdateStatusAsync,
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                await UpdateStatusAsync("AutoInject: Disabled");
            }
            catch (Exception ex)
            {
                await UpdateStatusAsync($"AutoInject failed: {ex.Message}");
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_autoInjectCancellation == cancellation)
                    {
                        _autoInjectCancellation.Dispose();
                        _autoInjectCancellation = null;
                    }

                    AutoInjectCheckBox.IsChecked = false;
                    SetControlsForAutoInject(isRunning: false);
                });
            }
        });
    }

    private void StopAutoInject()
    {
        _autoInjectCancellation?.Cancel();
        SetControlsForAutoInject(isRunning: false);
    }

    private Task UpdateStatusAsync(string message)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            StatusLabel.Text = message;
        });
    }

    private InjectorSettings ReadSettingsFromUi()
    {
        var delay = ParseDelayOrDefault(DelayEntry.Text);

        return new InjectorSettings
        {
            CustomTarget = CustomTargetCheckBox.IsChecked,
            ProcessName = string.IsNullOrWhiteSpace(ProcessNameEntry.Text)
                ? "minecraft.windows.exe"
                : ProcessNameEntry.Text.Trim(),
            DllPath = DllPathEntry.Text.Trim(),
            DelaySeconds = delay
        };
    }

    private void ApplySettingsToUi(InjectorSettings settings)
    {
        CustomTargetCheckBox.IsChecked = settings.CustomTarget;
        ProcessNameEntry.Text = settings.ProcessName;
        DllPathEntry.Text = NormalizeDllPathText(settings.DllPath);
        DelayEntry.Text = FormatDelaySeconds(NormalizeDelayOrDefault(settings.DelaySeconds));

        ProcessNameEntry.IsEnabled = settings.CustomTarget;
        UpdateInjectButtonState();
    }

    private void SetControlsForAutoInject(bool isRunning)
    {
        SelectButton.IsEnabled = !isRunning;
        DllPathEntry.IsEnabled = !isRunning;
        DelayEntry.IsEnabled = !isRunning;
        CustomTargetCheckBox.IsEnabled = !isRunning;

        SelectButton.Opacity = isRunning ? 0.55 : 1.0;

        ProcessNameEntry.IsEnabled = !isRunning && CustomTargetCheckBox.IsChecked;
        UpdateInjectButtonState();
    }

    private void UpdateInjectButtonState()
    {
        var hasValidDll = IsValidDllPath(DllPathEntry.Text);
        var autoInjectRunning = _autoInjectCancellation is not null;
        var canStartAction = !autoInjectRunning && hasValidDll;

        InjectButton.IsEnabled = canStartAction;
        InjectButton.Opacity = canStartAction ? 1.0 : 0.45;

        // Auto Inject should not be turn-on-able until the DLL path is valid.
        // Once it is already running, keep the checkbox enabled so the user can turn it off.
        AutoInjectCheckBox.IsEnabled = autoInjectRunning || hasValidDll;
        AutoInjectArea.Opacity = AutoInjectCheckBox.IsEnabled ? 1.0 : 0.45;
    }

    private static double ParseDelayOrDefault(string? delayText)
    {
        if (string.IsNullOrWhiteSpace(delayText))
            return DefaultDelaySeconds;

        var trimmed = delayText.Trim();

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var delaySeconds)
            && !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out delaySeconds))
        {
            return DefaultDelaySeconds;
        }

        return NormalizeDelayOrDefault(delaySeconds);
    }

    private static double NormalizeDelayOrDefault(double delaySeconds)
    {
        return double.IsFinite(delaySeconds) && delaySeconds > 0
            ? delaySeconds
            : DefaultDelaySeconds;
    }

    private static string FormatDelaySeconds(double delaySeconds)
    {
        delaySeconds = NormalizeDelayOrDefault(delaySeconds);

        return delaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool IsValidDllPath(string? dllPath)
    {
        var normalizedPath = NormalizeDllPathText(dllPath);

        return !string.Equals(normalizedPath, DllPathPlaceholder, StringComparison.Ordinal)
               && string.Equals(Path.GetExtension(normalizedPath), ".dll", StringComparison.OrdinalIgnoreCase)
               && File.Exists(normalizedPath);
    }

    private static string NormalizeDllPathText(string? dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath)
            || string.Equals(dllPath, OldDllPathPlaceholder, StringComparison.Ordinal))
        {
            return DllPathPlaceholder;
        }

        return dllPath.Trim();
    }
}
