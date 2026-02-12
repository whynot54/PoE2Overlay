using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PoE2Overlay.Services;

public sealed class ClipboardMonitor : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private HwndSource? _hwndSource;
    private nint _hwnd;
    private bool _disposed;

    public event EventHandler<string>? ClipboardChanged;

    public void Start(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
        AddClipboardFormatListener(_hwnd);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            OnClipboardUpdate();
            handled = true;
        }
        return nint.Zero;
    }

    private void OnClipboardUpdate()
    {
        if (!GameWindowDetector.IsGameForeground())
            return;

        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (ItemParser.IsPoEItem(text))
                {
                    ClipboardChanged?.Invoke(this, text);
                }
            }
        }
        catch
        {
            // Clipboard can throw if locked by another process — safe to ignore
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != nint.Zero)
            RemoveClipboardFormatListener(_hwnd);

        _hwndSource?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);
}
