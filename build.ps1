<#
  Builds dist\Pulse-Setup-<version>.exe

  Needs:  .NET 8 SDK        winget install Microsoft.DotNet.SDK.8
          Inno Setup 6      winget install JRSoftware.InnoSetup

  Run:    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.0.0
#>
param([string]$Version = "1.0.0")

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # makes Invoke-WebRequest much faster on PS 5.1
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Set-Location $PSScriptRoot

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

# ── 1. PresentMon (FPS) ─────────────────────────────────────────────
$headers = @{ "User-Agent" = "pulse-build" }
if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN" }

if (-not (Test-Path "Tools\PresentMon.exe")) {
    Step "Downloading PresentMon (console build)"
    $release = Invoke-RestMethod "https://api.github.com/repos/GameTechDev/PresentMon/releases/latest" -Headers $headers
    $asset = $release.assets | Where-Object { $_.name -match '^PresentMon-[\d\.]+-x64\.exe$' } | Select-Object -First 1
    if (-not $asset) {
        throw "Couldn't find the PresentMon console exe in the latest release. Download it manually and save it as Tools\PresentMon.exe"
    }
    Invoke-WebRequest $asset.browser_download_url -OutFile "Tools\PresentMon.exe" -Headers $headers
    Write-Host "    $($asset.name)"
}

if (-not (Test-Path "Tools\PresentMon-LICENSE.txt")) {
    Step "Downloading PresentMon license"
    $lic = Invoke-RestMethod "https://api.github.com/repos/GameTechDev/PresentMon/license" -Headers $headers
    [IO.File]::WriteAllBytes("$PSScriptRoot\Tools\PresentMon-LICENSE.txt", [Convert]::FromBase64String($lic.content))
}

# ── 2. Publish (self-contained: end users don't need .NET) ─────────
Step "Publishing Pulse $Version"
if (Test-Path "publish") { Remove-Item "publish" -Recurse -Force }
dotnet publish Pulse.csproj -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# ── 3. Installer ──────────────────────────────────────────────────
Step "Building installer"
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { throw "Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$Version" "installer\Pulse.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

$out = Resolve-Path "dist\Pulse-Setup-$Version.exe"
Write-Host "`nDone!  $out" -ForegroundColor Green
