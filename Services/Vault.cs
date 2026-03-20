namespace Translator.Services;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

[SupportedOSPlatform("windows")]
static class Vault
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIAL
    {
        public uint Flags, Type;
        public string TargetName, Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredReadW(string target, uint type, uint flags, out IntPtr cred);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredWriteW(ref CREDENTIAL cred, uint flags);
    [DllImport("advapi32.dll")]
    static extern void CredFree(IntPtr buf);

    const uint CRED_GENERIC = 1;
    const uint CRED_PERSIST = 2;
    const string RegPath = @"Software\Translator";
    const string CredTarget = "translator_api_key";

    public static string? GetApiKey()
    {
        var k = ReadCred(CredTarget);
        if (k != null && k.StartsWith("sk-") && k.Length > 20) return k;
        return null;
    }

    public static void SetApiKey(string key)
    {
        WriteCred(CredTarget, "api_key", key);
    }

    static string? ReadCred(string target)
    {
        if (!CredReadW(target, CRED_GENERIC, 0, out var ptr) || ptr == IntPtr.Zero)
            return null;
        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (c.CredentialBlobSize == 0 || c.CredentialBlob == IntPtr.Zero)
                return null;
            return Marshal.PtrToStringUni(c.CredentialBlob, (int)(c.CredentialBlobSize / 2));
        }
        finally { CredFree(ptr); }
    }

    static void WriteCred(string target, string user, string password)
    {
        var blob = Encoding.Unicode.GetBytes(password);
        var pin = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var c = new CREDENTIAL
            {
                Type = CRED_GENERIC,
                TargetName = target,
                UserName = user,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = pin.AddrOfPinnedObject(),
                Persist = CRED_PERSIST,
            };
            CredWriteW(ref c, 0);
        }
        finally { pin.Free(); }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "Translator";

    public static bool GetStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public static void SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enabled)
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exe != null) key.SetValue(AppName, $"\"{exe}\"");
            }
            else
                key.DeleteValue(AppName, false);
        }
        catch { }
    }

    public static bool GetAutoSelectAll()
    {
        var val = RegGet("AutoSelectAll");
        return val == null || val == "1";
    }

    public static void SetAutoSelectAll(bool enabled) => RegSet("AutoSelectAll", enabled ? "1" : "0");

    public static string? GetHotkey() => RegGet("HotkeyScan");

    public static void SetHotkey(string scanData)
    {
        RegSet("HotkeyScan", scanData);
    }

    static string? RegGet(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            return key?.GetValue(name) as string;
        }
        catch { return null; }
    }

    static void RegSet(string name, string val)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath);
            key.SetValue(name, val);
        }
        catch { }
    }
}
