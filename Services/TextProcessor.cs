namespace Translator.Services;

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Translator.Models;

sealed class TextProcessor(OpenAiClient ai, AiDetector detector)
{
    [DllImport("user32.dll")]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")]
    static extern bool OpenClipboard(IntPtr hw);
    [DllImport("user32.dll")]
    static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32.dll")]
    static extern IntPtr SetClipboardData(uint fmt, IntPtr hMem);
    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public ushort vk;
        [FieldOffset(10)] public ushort scan;
        [FieldOffset(12)] public uint flags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public IntPtr extra;
    }

    const uint CF_UNICODETEXT = 13;
    const uint GMEM_MOVEABLE = 0x0002;
    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const ushort VK_CONTROL = 0x11;
    const ushort VK_A = 0x41;
    const ushort VK_C = 0x43;
    const ushort VK_V = 0x56;

    static readonly int InputStructSize = Marshal.SizeOf<INPUT>();
    readonly SemaphoreSlim _gate = new(1, 1);
    public bool AutoSelectAll { get; set; } = true;

    public async Task<ChatEntry?> ProcessAsync()
    {
        if (!_gate.Wait(0)) return null;

        try
        {
            if (AutoSelectAll)
            {
                SendCtrlCombo(VK_A);
                await Task.Delay(25);
            }
            SendCtrlCombo(VK_C);
            await Task.Delay(40);

            var content = TryReadClipboard()?.Trim();
            if (string.IsNullOrEmpty(content)) return null;

            var (text, ops) = OpParser.Parse(content);
            if (text == null) return null;

            var entry = new ChatEntry
            {
                Original = text,
                Operations = OpParser.Describe(ops),
            };

            string? cur = text;
            foreach (var op in ops)
            {
                cur = op.Kind switch
                {
                    OpKind.Improve =>
                        await ai.Chat(Prompts.Improve, cur!, 0.95),
                    OpKind.Answer =>
                        await ai.Chat(Prompts.Answer, cur!, 0.4),
                    OpKind.Deformalise =>
                        await ai.Chat(Prompts.Deformalise, cur!, 0.8),
                    OpKind.Translate when op.Lang == "English" =>
                        await ai.Chat(Prompts.TranslateToEnglish, cur!, 0.2),
                    OpKind.Translate =>
                        await ai.Chat(Prompts.TranslateTo(op.Lang!), cur!, 0.3),
                    OpKind.Prompt =>
                        await ai.Chat(Prompts.StructurePrompt, cur!, 0.4, Prompts.PromptModel),
                    OpKind.AiCheck =>
                        await detector.Check(cur!),
                    _ => cur,
                };

                if (cur == null)
                {
                    entry.ErrorMsg = (op.Kind == OpKind.AiCheck ? detector.LastError : ai.LastError) ?? "Unknown error";
                    break;
                }
            }

            if (cur == null)
            {
                entry.Failed = true;
                entry.Processing = false;
                return entry;
            }

            entry.Result = cur;
            entry.Processing = false;

            TryWriteClipboard(cur);
            if (AutoSelectAll)
            {
                SendCtrlCombo(VK_A);
                await Task.Delay(25);
            }
            SendCtrlCombo(VK_V);

            return entry;
        }
        finally { _gate.Release(); }
    }

    static void SendCtrlCombo(ushort key)
    {
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, vk = VK_CONTROL },
            new() { type = INPUT_KEYBOARD, vk = key },
            new() { type = INPUT_KEYBOARD, vk = key, flags = KEYEVENTF_KEYUP },
            new() { type = INPUT_KEYBOARD, vk = VK_CONTROL, flags = KEYEVENTF_KEYUP },
        };
        SendInput(4, inputs, InputStructSize);
    }

    static string? TryReadClipboard()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    var h = GetClipboardData(CF_UNICODETEXT);
                    if (h == IntPtr.Zero) return null;
                    var ptr = GlobalLock(h);
                    if (ptr == IntPtr.Zero) return null;
                    try { return Marshal.PtrToStringUni(ptr); }
                    finally { GlobalUnlock(h); }
                }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(10);
        }
        return null;
    }

    static void TryWriteClipboard(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    int bytes = (text.Length + 1) * 2;
                    var hMem = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
                    var ptr = GlobalLock(hMem);
                    Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                    Marshal.WriteInt16(ptr + text.Length * 2, 0);
                    GlobalUnlock(hMem);
                    SetClipboardData(CF_UNICODETEXT, hMem);
                    return;
                }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(10);
        }
    }
}
