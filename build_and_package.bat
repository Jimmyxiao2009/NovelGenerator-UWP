@echo off
setlocal ENABLEDELAYEDEXPANSION
chcp 65001 >nul

REM ============================================================
REM  NovelGenerator: one-step Release x86+x64+ARM .appxbundle
REM  Auto-locates MSBuild via vswhere — no hard-coded VS path.
REM ============================================================

set "ROOT=%~dp0"
set "PROJ=%ROOT%NovelGenerator.csproj"
set "CONFIG=Release"
set "PLATFORM=x86"
set "OUTDIR=%ROOT%bin\AppPackages"

REM --- locate vswhere ---
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [ERROR] vswhere.exe not found. Install Visual Studio 2017+ ^(Community is fine^).
    exit /b 1
)

REM --- locate MSBuild (prefer amd64) ---
set "MSBUILD="
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\amd64\MSBuild.exe"`) do (
    set "MSBUILD=%%i"
    goto :got_msbuild
)
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"`) do (
    set "MSBUILD=%%i"
    goto :got_msbuild
)
:got_msbuild
if not defined MSBUILD (
    echo [ERROR] MSBuild.exe not found via vswhere.
    exit /b 1
)
echo MSBuild: %MSBUILD%

REM --- 1. restore ---
echo.
echo === [1/2] Restore (NuGet) ===
"%MSBUILD%" "%PROJ%" /t:Restore /v:minimal /nologo /p:Configuration=%CONFIG% /p:Platform=%PLATFORM%
if errorlevel 1 (
    echo Restore failed.
    exit /b 1
)

REM --- 2. build + bundle + sign in one shot ---
echo.
echo === [2/2] Build + Bundle + Sign (x86^|x64^|arm) ===
set MAX=3
set N=0
:RETRY
set /a N+=1
"%MSBUILD%" "%PROJ%" ^
    /p:Configuration=%CONFIG% ^
    /p:Platform=%PLATFORM% ^
    /p:AppxBundle=Always ^
    /p:AppxBundlePlatforms="x86|x64|arm" ^
    /p:UapAppxPackageBuildMode=SideloadOnly ^
    /p:AppxPackageSigningEnabled=true ^
    /m:1 ^
    /v:minimal /nologo /t:Build
if not errorlevel 1 goto :BUILDDONE
echo Build attempt %N% failed.
if %N% LSS %MAX% (
    echo   retrying in 3s...
    timeout /t 3 /nobreak >nul
    goto :RETRY
)
echo Build failed after %MAX% attempts.
exit /b 1
:BUILDDONE
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

REM --- 3. show result ---
echo.
set "BUNDLE="
for /f "delims=" %%f in ('dir /b /s /o-d "%OUTDIR%\*.appxbundle" 2^>nul') do (
    set "BUNDLE=%%f"
    goto :found_bundle
)
:found_bundle
if not defined BUNDLE (
    echo [WARN] No .appxbundle produced under %OUTDIR%
    exit /b 1
)

echo ✓ Done: %BUNDLE%
powershell -NoProfile -Command "$f=Get-Item '%BUNDLE%'; '{0} ({1:N2} MB)' -f $f.Name, ($f.Length/1MB)"
echo.
echo Install:
echo   1. Enable Developer Mode (Settings - Privacy ^& Security - For developers).
echo   2. Trust the test cert (one-time):
echo        certutil -addstore -f TrustedPeople "%ROOT%NovelGenerator_TemporaryKey.pfx"
echo   3. Double-click the .appxbundle file.
echo.
endlocal
