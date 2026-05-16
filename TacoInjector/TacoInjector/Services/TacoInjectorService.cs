using System.Globalization;
using TacoInjector.Models;

namespace TacoInjector.Services;

public sealed class TacoInjectorService(
    IInjectionBackend injectionBackend,
    SettingsService settingsService,
    FilePermissionService filePermissionService)
{
    private const double DefaultDelaySeconds = 5;
    private const double DelayComparisonTolerance = 0.000_001;
    public async ValueTask<InjectionResult> InjectOnceAsync(
        InjectorSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var processId = ProcessFinder.FindProcessId(settings.ProcessName);

        if (processId is null)
        {
            return InjectionResult.Failure("Can't find process! | 0");
        }

        if (!IsValidDllPath(settings.DllPath))
        {
            return InjectionResult.Failure(
                $"Process found! | {processId.Value} | invalid file path");
        }

        var fullDllPath = Path.GetFullPath(settings.DllPath);

        filePermissionService.AllowAllApplicationPackagesReadExecute(fullDllPath);

        var result = await injectionBackend.InjectAsync(
            processId.Value,
            fullDllPath,
            cancellationToken);

        await settingsService.SaveAsync(settings, cancellationToken);

        return result;
    }

    public async Task RunAutoInjectAsync(
        InjectorSettings settings,
        Func<string, Task> reportStatusAsync,
        CancellationToken cancellationToken)
    {
        var delaySeconds = NormalizeDelayOrDefault(settings.DelaySeconds);
        var injectedProcessIds = new HashSet<int>();
        var formattedDelay = FormatDelaySeconds(delaySeconds);

        await reportStatusAsync(
            IsOneSecond(delaySeconds)
                ? "AutoInject: Enabled | trying every second"
                : $"AutoInject: Enabled | trying every {formattedDelay} seconds");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(delaySeconds));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsValidDllPath(settings.DllPath))
            {
                await reportStatusAsync("AutoInject: Disabled | invalid DLL path");
                return;
            }

            var processId = ProcessFinder.FindProcessId(settings.ProcessName);

            if (processId is null)
            {
                await reportStatusAsync("AutoInject: Can't find process! | 0");
                continue;
            }

            if (injectedProcessIds.Contains(processId.Value))
            {
                await reportStatusAsync($"AutoInject: Already injected! | {processId.Value}");
                continue;
            }

            var result = await InjectOnceAsync(settings, cancellationToken);
            await reportStatusAsync(result.Message);

            if (result.Succeeded)
            {
                injectedProcessIds.Add(processId.Value);
            }
            else if (!IsValidDllPath(settings.DllPath))
            {
                await reportStatusAsync("AutoInject: Disabled | invalid DLL path");
                return;
            }
        }
    }

    private static double NormalizeDelayOrDefault(double delaySeconds)
    {
        return double.IsFinite(delaySeconds) && delaySeconds > 0
            ? delaySeconds
            : DefaultDelaySeconds;
    }

    private static bool IsOneSecond(double delaySeconds)
    {
        return Math.Abs(delaySeconds - 1.0) < DelayComparisonTolerance;
    }

    private static string FormatDelaySeconds(double delaySeconds)
    {
        delaySeconds = NormalizeDelayOrDefault(delaySeconds);

        return delaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool IsValidDllPath(string? dllPath)
    {
        return !string.IsNullOrWhiteSpace(dllPath)
               && string.Equals(Path.GetExtension(dllPath), ".dll", StringComparison.OrdinalIgnoreCase)
               && File.Exists(dllPath);
    }
}
