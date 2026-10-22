using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace LokiHost;

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    /// <summary>--settings &lt;file&gt;: use this settings file instead of %APPDATA%\LokiHost\lokihost.json (tests keep a
    /// scratch host beside the real one this way).</summary>
    public static string? SettingsPath { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--settings") SettingsPath = Path.GetFullPath(args[i + 1]);
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
