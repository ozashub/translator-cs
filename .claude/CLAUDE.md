# Translator C# - Project Rules

## Release Protocol

Every `git push` that changes functionality MUST be followed by:

1. Bump `<Version>` in `translator-cs.csproj` (semver: patch for fixes, minor for features, major for breaking)
2. Update `AppVersion` in `installer.iss` to match
3. Build: `dotnet publish` for x64, then `vpk pack` to create Velopack release
4. Create GitHub release with `gh release create vX.Y.Z` uploading assets **in this order**:
   - Setup exe first (Translator-win-Setup.exe)
   - Portable zip second (Translator-win-Portable.zip)
   - Then update files (RELEASES, .nupkg, .json) — these go last so they're below the fold
5. Velopack handles auto-update versioning automatically

If the change is cosmetic-only (README, comments, CI config), skip the release.

## Build Commands

```
dotnet publish translator-cs.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o publish/win-x64 --nologo -v q
vpk pack --packId Translator --packVersion X.Y.Z --packDir publish/win-x64 --mainExe Translator.exe --outputDir publish/releases
```

vpk CLI requires: `dotnet tool install -g vpk --version 0.0.942`

## Key Paths

- Version: `translator-cs.csproj` `<Version>` tag
- Installer version: `installer.iss` `AppVersion`
- Auto-updater: Velopack (checks GitHub releases, downloads .nupkg)
- GitHub repo: ozashub/translator-cs
- gh CLI: /c/Program Files/GitHub CLI or PATH
- vpk CLI: ~/.dotnet/tools/vpk
- Inno Setup: C:\InnoSetup\ISCC.exe (optional, Velopack generates its own Setup.exe)
