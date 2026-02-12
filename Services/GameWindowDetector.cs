using System.Diagnostics;

namespace PoE2Overlay.Services;

public static class GameWindowDetector
{
    private static readonly string[] GameProcessNames =
    [
        "PathOfExileSteam",
        "PathOfExile",
        "PathOfExile_x64",
        "PathOfExile_x64Steam"
    ];

    public static bool DebugMode { get; set; }

    public static bool IsGameForeground()
    {
        if (DebugMode)
            return true;
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == nint.Zero)
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
            return false;

        try
        {
            var process = Process.GetProcessById((int)processId);
            return GameProcessNames.Any(name =>
                process.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
