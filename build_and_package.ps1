# Build + Package NovelGenerator (UWP, Release x86+x64+ARM bundle)
# Usage: powershell -ExecutionPolicy Bypass -File build_and_package.ps1
#
# What it does:
#   1. Auto-locates MSBuild via vswhere (works on VS 2017/2019/2022/preview).
#   2. Restores NuGet packages.
#   3. Runs one MSBuild command that compiles + signs + bundles all 3 platforms.
#      Output: bin\AppPackages\NovelGenerator_<ver>_Test\NovelGenerator_<ver>_x86_x64_arm.appxbundle
#
# Requires: NovelGenerator_TemporaryKey.pfx must remain next to the csproj.

$ErrorActionPreference = "Stop"
$root        = Split-Path -Parent $MyInvocation.MyCommand.Path
$projPath    = Join-Path $root "NovelGenerator.csproj"
$configuration = "Release"
$platform      = "x86"          # entry platform; bundle covers x86|x64|arm via AppxBundlePlatforms
$appxOutDir  = Join-Path $root "bin\AppPackages"

# ----- 1. Locate MSBuild ---------------------------------------------------
function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        $vswhere = Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe"
    }
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found. Install Visual Studio 2017+ (Community is fine)."
    }
    $msbuild = & $vswhere -latest -prerelease -products * `
        -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\amd64\MSBuild.exe" `
        | Select-Object -First 1
    if (-not $msbuild) {
        $msbuild = & $vswhere -latest -prerelease -products * `
            -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe" `
            | Select-Object -First 1
    }
    if (-not $msbuild -or -not (Test-Path $msbuild)) {
        throw "MSBuild.exe not found via vswhere."
    }
    return $msbuild
}

$msbuild = Find-MSBuild
Write-Host ""
Write-Host "MSBuild: $msbuild" -ForegroundColor DarkGray

# ----- 2. Restore ----------------------------------------------------------
Write-Host ""
Write-Host "=== [1/2] Restore (NuGet) ===" -ForegroundColor Cyan
& $msbuild "$projPath" /t:Restore /v:minimal /nologo /p:Configuration=$configuration /p:Platform=$platform
if ($LASTEXITCODE -ne 0) { Write-Host "Restore failed (exit $LASTEXITCODE)" -ForegroundColor Red; exit 1 }

# ----- 3. Build + Bundle + Sign (one shot) ---------------------------------
Write-Host ""
Write-Host "=== [2/2] Build + Bundle + Sign (x86|x64|arm) ===" -ForegroundColor Cyan
$msbuildArgs = @(
    "`"$projPath`"",
    "/p:Configuration=$configuration",
    "/p:Platform=$platform",
    "/p:AppxBundle=Always",
    "/p:AppxBundlePlatforms=`"x86|x64|arm`"",
    "/p:UapAppxPackageBuildMode=SideloadOnly",
    "/p:AppxPackageSigningEnabled=true",
    "/m:1",
    "/v:minimal",
    "/nologo",
    "/t:Build"
)
$maxAttempts = 3
$attempt = 0
$bundleBefore = Get-ChildItem -Path $appxOutDir -Recurse -Filter "*.appxbundle" -ErrorAction SilentlyContinue
while ($attempt -lt $maxAttempts) {
    $attempt++
    & $msbuild @msbuildArgs
    if ($LASTEXITCODE -eq 0) { break }
    Write-Host "Build attempt $attempt failed (exit $LASTEXITCODE)" -ForegroundColor Yellow
    if ($attempt -lt $maxAttempts) {
        Write-Host "  retrying in 3s..." -ForegroundColor Yellow
        Start-Sleep -Seconds 3
    }
}
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed after $maxAttempts attempts" -ForegroundColor Red
    exit 1
}

# ----- 4. Show result ------------------------------------------------------
$bundle = Get-ChildItem -Path $appxOutDir -Recurse -Filter "*.appxbundle" -ErrorAction SilentlyContinue `
    | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $bundle) {
    Write-Host "Build reported success but no .appxbundle was produced under $appxOutDir" -ForegroundColor Yellow
    exit 1
}

$sizeMb = [math]::Round($bundle.Length / 1MB, 2)
Write-Host ""
Write-Host "Done: $($bundle.FullName) ($sizeMb MB)" -ForegroundColor Green
Write-Host ""
Write-Host "Install:" -ForegroundColor Yellow
Write-Host "  1. Enable Developer Mode (Settings > Privacy & Security > For developers)."
Write-Host "  2. Trust the test certificate (one-time):"
Write-Host "       certutil -addstore -f TrustedPeople `"$root\NovelGenerator_TemporaryKey.pfx`""
Write-Host "  3. Double-click the .appxbundle file."
Write-Host ""
