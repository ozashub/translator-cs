namespace Translator.Services;

using System;
using System.Runtime.InteropServices;

sealed class TrayIcon : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NID
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    delegate IntPtr WndProcDel(IntPtr hw, uint msg, IntPtr wp, IntPtr lp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS
    {
        public uint style;
        public WndProcDel lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground, lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x, y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hw);
    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProcW(IntPtr hw, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIconW(uint msg, ref NID data);
    [DllImport("user32.dll")]
    static extern IntPtr LoadIconW(IntPtr inst, IntPtr name);
    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")]
    static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int res,
        IntPtr hw, IntPtr rect);
    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hw);

    const uint WM_APP_TRAY = 0x8001;
    const uint NIM_ADD = 0, NIM_DELETE = 2, NIM_MODIFY = 1;
    const uint NIF_ICON = 2, NIF_TIP = 4, NIF_MSG = 1;
    const uint MF_STRING = 0, MF_SEP = 0x800, MF_GRAY = 1;
    const uint TPM_RET = 0x100;
    const int HWND_MSG = -3;
    const uint WM_RBUTTONUP = 0x0205, WM_LBUTTONDBLCLK = 0x0203;

    IntPtr _hwnd;
    NID _nid;
    WndProcDel? _wndProc;
    string _hotkey = "";
    bool _alive;

    public event Action? ShowRequested;
    public event Action? QuitRequested;

    public void Create(string hotkey)
    {
        _hotkey = hotkey;
        _wndProc = WndProc;

        var wc = new WNDCLASS
        {
            lpfnWndProc = _wndProc,
            lpszClassName = "TranslatorTray_" + Environment.ProcessId,
            hInstance = Marshal.GetHINSTANCE(typeof(TrayIcon).Module),
        };
        RegisterClassW(ref wc);

        _hwnd = CreateWindowExW(0, wc.lpszClassName, "", 0,
            0, 0, 0, 0, (IntPtr)HWND_MSG, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        _nid = new NID
        {
            cbSize = Marshal.SizeOf<NID>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_TIP | NIF_MSG,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = LoadIconW(IntPtr.Zero, (IntPtr)32512),
            szTip = $"Translator  \u2502  {hotkey}",
            szInfo = "",
            szInfoTitle = "",
        };

        Shell_NotifyIconW(NIM_ADD, ref _nid);
        _alive = true;
    }

    public void UpdateTip(string hotkey)
    {
        if (!_alive) return;
        _hotkey = hotkey;
        _nid.szTip = $"Translator  \u2502  {hotkey}";
        _nid.uFlags = NIF_TIP;
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    IntPtr WndProc(IntPtr hw, uint msg, IntPtr wp, IntPtr lp)
    {
        if (msg == WM_APP_TRAY)
        {
            uint ev = (uint)(lp.ToInt64() & 0xFFFF);
            if (ev == WM_RBUTTONUP) ShowMenu();
            else if (ev == WM_LBUTTONDBLCLK) ShowRequested?.Invoke();
            return IntPtr.Zero;
        }
        return DefWindowProcW(hw, msg, wp, lp);
    }

    void ShowMenu()
    {
        var m = CreatePopupMenu();
        AppendMenuW(m, MF_STRING | MF_GRAY, 1, $"Hotkey: {_hotkey}");
        AppendMenuW(m, MF_SEP, 0, null);
        AppendMenuW(m, MF_STRING, 2, "Show");
        AppendMenuW(m, MF_STRING, 3, "Quit");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        int cmd = TrackPopupMenu(m, TPM_RET, pt.x, pt.y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(m);

        if (cmd == 2) ShowRequested?.Invoke();
        else if (cmd == 3) QuitRequested?.Invoke();
    }

    public void Dispose()
    {
        if (!_alive) return;
        _alive = false;
        Shell_NotifyIconW(NIM_DELETE, ref _nid);
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
    }
}
