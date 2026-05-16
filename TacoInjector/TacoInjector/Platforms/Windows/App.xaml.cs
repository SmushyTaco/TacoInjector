using System.Text;

namespace TacoInjector.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TacoInjector",
        "crash.log");

    public App()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);

        UnhandledException += (_, args) =>
        {
            LogException("Microsoft.UI.Xaml.Application.UnhandledException", args.Exception);
            args.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                LogException("AppDomain.CurrentDomain.UnhandledException", exception);
            else
                LogText($"AppDomain.CurrentDomain.UnhandledException: {args.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp()
    {
        try
        {
            return MauiProgram.CreateMauiApp();
        }
        catch (Exception exception)
        {
            LogException("CreateMauiApp failed", exception);
            throw;
        }
    }

    private static void LogException(string source, Exception exception)
    {
        var builder = new StringBuilder();

        builder.AppendLine("============================================================");
        builder.AppendLine(DateTimeOffset.Now.ToString("O"));
        builder.AppendLine(source);
        builder.AppendLine(exception.ToString());

        File.AppendAllText(CrashLogPath, builder.ToString());
    }

    private static void LogText(string text)
    {
        File.AppendAllText(
            CrashLogPath,
            $"============================================================{Environment.NewLine}{DateTimeOffset.Now:O}{Environment.NewLine}{text}{Environment.NewLine}");
    }
}
