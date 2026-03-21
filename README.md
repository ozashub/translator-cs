<p align="center">
  <img src="Assets/icon.svg" width="128" height="128" alt="Translator">
</p>

# Translator

A Windows desktop app that rewrites selected text using OpenAI. Press a global hotkey in any application and it selects the text, sends it through the API, and pastes the result back in place. Built with WinUI 3 and .NET 8.

## What it does

Type text in any application, append an optional suffix, press your hotkey. The text gets replaced with the processed result. The app runs in the system tray and works globally across all windows.

**Operations:**

| Suffix | What happens |
|--------|-------------|
| *(none)* | Rewrite to sound more natural and pass AI detection |
| `-r` | Answer the question |
| `-df` | Make it casual, like texting a friend |
| `--aicheck` | Check text against ZeroGPT AI detector, returns % AI |
| `--prompt` | Turn messy notes into a structured AI prompt |
| `-en` | Translate to English |
| `-es`, `-fr`, `-de`, `-it`, `-pt` | Spanish, French, German, Italian, Portuguese |
| `-ru`, `-ja`, `-ko`, `-zh` | Russian, Japanese, Korean, Chinese |
| `-nl`, `-sv`, `-no`, `-jam` | Dutch, Swedish, Norwegian, Jamaican Patois |

Suffixes chain. `some formal text -df-en` will deformalise first, then translate the result to English.

## Downloads

Grab the latest from [Releases](https://github.com/ozashub/translator-cs/releases):

- **TranslatorSetup-x64.exe** - installer with desktop and start menu shortcuts
- **Translator-x64.zip** - portable, extract and run
- **Translator-arm64.zip** - portable for ARM64 machines

Requires [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime).

## Building from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Windows 10 or later.

```
build.bat
```

Builds x64 and ARM64 zips. If [Inno Setup](https://jrsoftware.org/isdl.php) is installed, also builds the installer.

## First launch

The app will ask for two things:

1. **OpenAI API key** - get one from [platform.openai.com/api-keys](https://platform.openai.com/api-keys). Stored securely in Windows Credential Manager.
2. **Hotkey** - press any key combination. Uses scancodes internally so it works regardless of keyboard layout.

Both are saved and loaded automatically on next launch.

## Features

- Chat history with copy, expand/collapse, and right-click context menu
- Settings dialog with inline hotkey capture, API key, start with Windows toggle
- Auto-update from GitHub releases with download progress
- System tray - minimize hides to tray, X closes the app
- Scancode-based hotkey that works on any keyboard layout

## License

[AGPL-3.0](LICENSE)
