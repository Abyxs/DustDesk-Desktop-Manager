using System.Drawing;
using System.Runtime.InteropServices;

namespace DustDesk;

internal static class NativeGlass
{
    private const int AccentEnableTransparentGradient = 2;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int WmNclButtonDown = 0xA1;
    private const int HtCaption = 0x2;
    private const int WmSpawnWorker = 0x052C;
    private const int SmtoNormal = 0x0000;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsPopup = 0x80000000L;
    private const long WsChild = 0x40000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const int SwpFrameChanged = 0x0020;
    private const int SwpShowWindow = 0x0040;
    private const int SwpNoActivate = 0x0010;
    private const int SwpNoZOrder = 0x0004;
    private const int ShcneCreate = 0x00000002;
    private const int ShcneDelete = 0x00000004;
    private const int ShcneMkdir = 0x00000008;
    private const int ShcneRmdir = 0x00000010;
    private const int ShcneUpdateDir = 0x00001000;
    private const uint ShcnfPathW = 0x0005;
    private const uint ShcnfFlushNowait = 0x2000;

    public static void EnableAcrylic(IntPtr handle, Color tint)
    {
        if (Environment.OSVersion.Version.Major < 10)
        {
            return;
        }

        var accent = new AccentPolicy
        {
            AccentState = tint.A < 96 ? AccentEnableTransparentGradient : AccentEnableAcrylicBlurBehind,
            GradientColor = ToAbgr(tint)
        };

        var accentSize = Marshal.SizeOf<AccentPolicy>();
        var accentPtr = Marshal.AllocHGlobal(accentSize);

        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WcaAccentPolicy,
                SizeOfData = accentSize,
                Data = accentPtr
            };
            _ = SetWindowCompositionAttribute(handle, ref data);

            var dark = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            var corner = 2;
            _ = DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    public static void DisableAcrylic(IntPtr handle)
    {
        if (Environment.OSVersion.Version.Major < 10)
        {
            return;
        }

        var accent = new AccentPolicy { AccentState = 0 };
        var accentSize = Marshal.SizeOf<AccentPolicy>();
        var accentPtr = Marshal.AllocHGlobal(accentSize);

        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WcaAccentPolicy,
                SizeOfData = accentSize,
                Data = accentPtr
            };
            _ = SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    public static void ApplyDarkWindowFrame(IntPtr handle)
    {
        if (Environment.OSVersion.Version.Major < 10)
        {
            return;
        }

        var dark = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var corner = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
    }

    public static void BeginMove(IntPtr handle)
    {
        _ = ReleaseCapture();
        _ = SendMessage(handle, WmNclButtonDown, HtCaption, 0);
    }

    public static void BeginResize(IntPtr handle, int hitTest)
    {
        _ = ReleaseCapture();
        _ = SendMessage(handle, WmNclButtonDown, hitTest, 0);
    }

    public static void FocusInput(IntPtr windowHandle, IntPtr inputHandle)
    {
        if (windowHandle != IntPtr.Zero)
        {
            _ = SetForegroundWindow(windowHandle);
        }

        if (inputHandle != IntPtr.Zero)
        {
            _ = SetFocus(inputHandle);
        }
    }

    public static bool AttachToDesktop(IntPtr handle)
    {
        var host = FindDesktopHost();
        if (host == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowRect(handle, out var rect);
        _ = SetParent(handle, host);
        var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
        style &= ~WsPopup;
        style |= WsChild | WsVisible;
        _ = SetWindowLongPtr(handle, GwlStyle, new IntPtr(style));
        ApplyToolWindowStyle(handle);
        var location = new NativePoint { X = rect.Left, Y = rect.Top };
        _ = ScreenToClient(host, ref location);

        _ = SetWindowPos(
            handle,
            IntPtr.Zero,
            location.X,
            location.Y,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top),
            SwpFrameChanged | SwpShowWindow | SwpNoActivate);
        return true;
    }

    public static void DetachFromDesktop(IntPtr handle, Rectangle screenBounds)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = SetParent(handle, IntPtr.Zero);
        var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
        style &= ~WsChild;
        style |= WsPopup | WsVisible;
        _ = SetWindowLongPtr(handle, GwlStyle, new IntPtr(style));
        ApplyToolWindowStyle(handle);
        _ = SetWindowPos(
            handle,
            IntPtr.Zero,
            screenBounds.X,
            screenBounds.Y,
            Math.Max(1, screenBounds.Width),
            Math.Max(1, screenBounds.Height),
            SwpFrameChanged | SwpShowWindow);
    }

    public static void ApplyToolWindowStyle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        exStyle |= WsExToolWindow;
        exStyle &= ~WsExAppWindow;
        _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(exStyle));
        _ = SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoActivate | SwpNoZOrder);
    }

    public static void SetDesktopChildScreenBounds(IntPtr handle, Rectangle screenBounds)
    {
        var location = new NativePoint { X = screenBounds.X, Y = screenBounds.Y };
        var parent = GetParent(handle);
        if (parent != IntPtr.Zero)
        {
            _ = ScreenToClient(parent, ref location);
        }

        _ = SetWindowPos(
            handle,
            IntPtr.Zero,
            location.X,
            location.Y,
            Math.Max(1, screenBounds.Width),
            Math.Max(1, screenBounds.Height),
            SwpNoZOrder | SwpNoActivate);
    }

    public static Rectangle GetWindowScreenBounds(IntPtr handle, Rectangle fallback)
    {
        if (handle == IntPtr.Zero)
        {
            return fallback;
        }

        if (GetWindowRect(handle, out var rect))
        {
            return new Rectangle(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
        }

        var parent = GetParent(handle);
        if (parent == IntPtr.Zero)
        {
            return fallback;
        }

        var location = new NativePoint { X = fallback.X, Y = fallback.Y };
        return ClientToScreen(parent, ref location)
            ? new Rectangle(location.X, location.Y, Math.Max(1, fallback.Width), Math.Max(1, fallback.Height))
            : fallback;
    }

    public static void NotifyShellMoved(string source, string target, bool directory)
    {
        var flags = ShcnfPathW | ShcnfFlushNowait;
        SHChangeNotify(directory ? ShcneRmdir : ShcneDelete, flags, source, null);
        SHChangeNotify(directory ? ShcneMkdir : ShcneCreate, flags, target, null);
        NotifyShellDirectory(Path.GetDirectoryName(source));
        NotifyShellDirectory(Path.GetDirectoryName(target));

        var host = FindDesktopHost();
        if (host != IntPtr.Zero)
        {
            _ = InvalidateRect(host, IntPtr.Zero, true);
            _ = UpdateWindow(host);
        }
    }

    private static void NotifyShellDirectory(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            SHChangeNotify(ShcneUpdateDir, ShcnfPathW | ShcnfFlushNowait, directory, null);
        }
    }

    private static IntPtr FindDesktopHost()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            _ = SendMessageTimeout(progman, WmSpawnWorker, IntPtr.Zero, IntPtr.Zero, SmtoNormal, 1000, out _);
        }

        var result = IntPtr.Zero;
        EnumWindows((topHandle, _) =>
        {
            var shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView == IntPtr.Zero)
            {
                return true;
            }

            result = shellView;
            return false;
        }, IntPtr.Zero);

        return result != IntPtr.Zero ? result : progman;
    }

    private static int ToAbgr(Color color)
    {
        return color.A << 24 | color.B << 16 | color.G << 8 | color.R;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(IntPtr hwnd, int message, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfterHandle, int x, int y, int cx, int cy, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr windowHandle, IntPtr rect, bool erase);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateWindow(IntPtr windowHandle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string? item1, string? item2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr windowHandle, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr windowHandle, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowTitle);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, int flags, int timeout, out IntPtr result);

    private delegate bool EnumWindowsProc(IntPtr topHandle, IntPtr topParamHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private enum WindowCompositionAttribute
    {
        WcaAccentPolicy = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }
}
