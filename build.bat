@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul || (
    echo [ERROR] .NET SDK not found. Install .NET SDK 10: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

rem A running Zapret.exe locks dist\Zapret.exe and the build would fail (or you'd keep the old one).
tasklist /FI "IMAGENAME eq Zapret.exe" 2>nul | find /I "Zapret.exe" >nul && (
    echo [ERROR] Zapret.exe is running. Close it via the tray icon, then run build.bat again.
    pause
    exit /b 1
)

rem Single output location: the compiled exe goes straight into dist\ next to the engine (bin\ lists\ lua\).
rem Default = small build (needs .NET 10 Desktop Runtime). "build.bat portable" = self-contained, no prerequisites.
set "VARIANT=--self-contained false"
if /i "%~1"=="portable" set "VARIANT=--self-contained true -p:IncludeNativeLibrariesForSelfExtract=true"

echo Building dist\Zapret.exe ...
dotnet publish src\Zapret.App -c Release -r win-x64 %VARIANT% -p:PublishSingleFile=true -o dist
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

rem Copy the DPI engine next to the exe so dist\ is a complete, runnable folder.
echo Copying engine (bin\ lua\ lists\) ...
xcopy /E /I /Y /Q bin   dist\bin   >nul
xcopy /E /I /Y /Q lua   dist\lua   >nul
xcopy /E /I /Y /Q lists dist\lists >nul

echo.
echo Done: dist\Zapret.exe
for %%F in ("dist\Zapret.exe") do echo Size: %%~zF bytes
echo dist\ is ready to run - launch dist\Zapret.exe (as administrator).
pause
