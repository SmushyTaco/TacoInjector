namespace TacoInjector.Models;

public sealed record InjectorSettings
{
    public bool CustomTarget { get; init; }

    public string ProcessName { get; init; } = "minecraft.windows.exe";

    public string DllPath { get; init; } = "Click \"Select DLL\" to select the dll file";

    public double DelaySeconds { get; init; } = 5;
}