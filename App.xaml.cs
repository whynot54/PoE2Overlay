using System.Windows;
using PoE2Overlay.Services;

namespace PoE2Overlay;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--debug", StringComparer.OrdinalIgnoreCase))
        {
            GameWindowDetector.DebugMode = true;
        }
    }
}
