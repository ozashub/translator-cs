namespace Translator.Services;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

sealed class GlobalKeyboard : IDisposable
{
    delegate IntPtr LLKeyProc(int code, IntPtr wp, IntPtr lp);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int id, LLKeyProc proc, IntPtr hMod, uint tid);
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wp, IntPtr lp);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetKeyNameTextW(int lParam, char[] buf, int size);
    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint tid, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    const int VK_SHIFT = 0x10, VK_CTRL = 0x11, VK_ALT = 0x12;
    const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_ESC = 0x1B;
    const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLL
    {
        public uint vk, scan, flags, time;
        public IntPtr extra;
    }

    static readonly HashSet<int> ModVKs =
    [
        VK_SHIFT, VK_CTRL, VK_ALT, VK_LWIN, VK_RWIN,
        0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5
    ];

    IntPtr _hook;
    LLKeyProc? _proc;
    uint _threadId;
    Combo? _hotkey;
    bool _capturing;
    TaskCompletionSource<Combo?>? _tcs;

    public Combo? CurrentHotkey => _hotkey;
    public event Action? HotkeyPressed;

    public record Combo(bool Ctrl, bool Shift, bool Alt, bool Win, uint Scan, string Name)
    {
        public override string ToString()
        {
            var parts = new List<string>(4);
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            if (Win) parts.Add("Win");
            parts.Add(Name);
            return string.Join(" + ", parts);
        }

        public string Serialize() =>
            $"{(Ctrl ? 1 : 0)}|{(Shift ? 1 : 0)}|{(Alt ? 1 : 0)}|{(Win ? 1 : 0)}|{Scan}|{Name}";

        public static Combo? Deserialize(string s)
        {
            var p = s.Split('|');
            if (p.Length != 6 || !uint.TryParse(p[4], out var sc)) return null;
            return new(p[0] == "1", p[1] == "1", p[2] == "1", p[3] == "1", sc, p[5]);
        }
    }

    public GlobalKeyboard()
    {
        var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            _proc = OnKey;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            _threadId = GetCurrentThreadId();
            ready.Set();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        });
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();
    }

    public void SetHotkey(Combo? c) => _hotkey = c;

    public Task<Combo?> CaptureAsync()
    {
        _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _capturing = true;
        return _tcs.Task;
    }

    IntPtr OnKey(int code, IntPtr wp, IntPtr lp)
    {
        if (code < 0)
            return CallNextHookEx(_hook, code, wp, lp);

        int msg = (int)wp;
        if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN)
            return CallNextHookEx(_hook, code, wp, lp);

        var kb = Marshal.PtrToStructure<KBDLL>(lp);
        int vk = (int)kb.vk;

        if (ModVKs.Contains(vk))
            return CallNextHookEx(_hook, code, wp, lp);

        bool ctrl = Held(VK_CTRL), shift = Held(VK_SHIFT);
        bool alt = Held(VK_ALT), win = Held(VK_LWIN) || Held(VK_RWIN);
        bool ext = (kb.flags & 1) != 0;
        string name = ScanName(kb.scan, ext);

        if (_capturing)
        {
            if (vk == VK_ESC)
            {
                _capturing = false;
                _tcs?.TrySetResult(null);
                return (IntPtr)1;
            }

            var combo = new Combo(ctrl, shift, alt, win, kb.scan, name);
            _capturing = false;
            _tcs?.TrySetResult(combo);
            return (IntPtr)1;
        }

        if (_hotkey is { } hk
            && kb.scan == hk.Scan
            && ctrl == hk.Ctrl && shift == hk.Shift
            && alt == hk.Alt && win == hk.Win)
        {
            HotkeyPressed?.Invoke();
            return (IntPtr)1;
        }

        return CallNextHookEx(_hook, code, wp, lp);
    }

    static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    static string ScanName(uint sc, bool ext)
    {
        int lp = (int)(sc << 16);
        if (ext) lp |= 1 << 24;
        var buf = new char[64];
        int len = GetKeyNameTextW(lp, buf, buf.Length);
        return len > 0 ? new string(buf, 0, len) : $"Key{sc:X2}";
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        if (_threadId != 0)
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
