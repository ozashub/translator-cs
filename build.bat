@echo off
echo Building Translator...
echo.

if exist publish rmdir /s /q publish

echo [1/2] Building x64...
dotnet publish translator-cs.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o publish\win-x64 --nologo -v q
if errorlevel 1 goto :fail

echo [2/2] Building ARM64...
dotnet publish translator-cs.csproj -c Release -r win-arm64 --self-contained true -p:Platform=ARM64 -o publish\win-arm64 --nologo -v q
if errorlevel 1 goto :fail

echo.
echo Zipping...
powershell -c "Compress-Archive -Path publish\win-x64\* -DestinationPath publish\Translator-x64.zip -Force"
powershell -c "Compress-Archive -Path publish\win-arm64\* -DestinationPath publish\Translator-arm64.zip -Force"

echo.
echo Building installer (requires Inno Setup)...
where iscc >nul 2>&1
if errorlevel 1 (
    echo   Inno Setup not found - skipping installer.
    echo   Install from https://jrsoftware.org/isdl.php
    echo   then re-run build.bat to generate the installer.
) else (
    iscc installer.iss
    echo   Installer built: publish\TranslatorSetup-x64compatible.exe
)

echo.
echo Done. Output in publish\
echo   Translator-x64.zip
echo   Translator-arm64.zip
exit /b 0

:fail
echo Build failed.
exit /b 1
