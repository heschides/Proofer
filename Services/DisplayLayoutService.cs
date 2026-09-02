using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Sati.Services;

/// <summary>
/// Describes the monitor that hosts Sati at shell startup and whether its physical
/// pixel dimensions need the compact layout. Physical pixels are deliberate: WPF's
/// layout units change with Windows display scaling and are not a resolution report.
/// </summary>
public readonly record struct DisplayLayoutProfile(int PixelWidth, int PixelHeight)
{
    /// <summary>1080p itself benefits from tighter presentation, but needs no warning.</summary>
    public bool UsesCompactMode =>
        PixelWidth <= DisplayLayoutService.RecommendedWidth ||
        PixelHeight <= DisplayLayoutService.RecommendedHeight;

    /// <summary>Only a format genuinely below 1080p receives the startup notice.</summary>
    public bool RequiresAdjustmentNotice =>
        PixelWidth < DisplayLayoutService.RecommendedWidth ||
        PixelHeight < DisplayLayoutService.RecommendedHeight;
}

/// <summary>
/// Owns the 1080p compact boundary and the stricter sub-1080p notice boundary. The
/// shell asks once, when its native window handle exists; presentation changes remain
/// in the shell and child view models rather than leaking monitor APIs into application logic.
/// </summary>
public sealed class DisplayLayoutService
{
    public const int RecommendedWidth = 1920;
    public const int RecommendedHeight = 1080;

    private const uint MonitorDefaultToNearest = 2;

    public DisplayLayoutProfile DetectFor(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                return FromPixelSize(
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top);
            }
        }

        // A native monitor lookup should succeed after SourceInitialized. This
        // fallback keeps startup usable if Windows refuses it and converts WPF's
        // device-independent primary-screen size back to physical pixels.
        var dpi = VisualTreeHelper.GetDpi(window);
        return FromPixelSize(
            (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX),
            (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY));
    }

    internal static DisplayLayoutProfile FromPixelSize(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        return new DisplayLayoutProfile(width, height);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
