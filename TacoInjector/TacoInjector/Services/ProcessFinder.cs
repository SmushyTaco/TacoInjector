using System.Diagnostics;

namespace TacoInjector.Services;

public static class ProcessFinder
{
    public static int? FindProcessId(string processName)
    {
        var normalizedName = Path.GetFileNameWithoutExtension(processName.Trim());

        if (string.IsNullOrWhiteSpace(normalizedName))
            return null;

        foreach (var process in Process.GetProcessesByName(normalizedName))
        {
            using (process)
            {
                return process.Id;
            }
        }

        return null;
    }
}