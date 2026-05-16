using System.Text.Json;
using System.Text.Json.Serialization;
using TacoInjector.Models;

namespace TacoInjector.Services;

public sealed class SettingsService
{
    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TacoInjector");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public async ValueTask<InjectorSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            var defaults = new InjectorSettings();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        await using var stream = File.OpenRead(SettingsPath);

        return await JsonSerializer.DeserializeAsync(
            stream,
            TacoInjectorJsonContext.Default.InjectorSettings,
            cancellationToken) ?? new InjectorSettings();
    }

    public async ValueTask SaveAsync(
        InjectorSettings settings,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(SettingsDirectory);

        await using var stream = File.Create(SettingsPath);

        await JsonSerializer.SerializeAsync(
            stream,
            settings,
            TacoInjectorJsonContext.Default.InjectorSettings,
            cancellationToken);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InjectorSettings))]
internal sealed partial class TacoInjectorJsonContext : JsonSerializerContext;
