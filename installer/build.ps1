<#
Builds the Stats Monitor installer.

  .\installer\build.ps1 [-Version 1.0.0]

Signing (optional): set SM_SIGN_PFX (path to the .pfx) and SM_SIGN_PASSWORD. When present, the executable and the
installer are signed with signtool (timestamped). Requires the .NET SDK, Inno Setup 6 (ISCC.exe) and the Windows SDK (signtool.exe).
#>
param([string]$Version = "1.0.0")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts\publish"
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish (Join-Path $root "src\StatsMonitor\StatsMonitor.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$signing = $env:SM_SIGN_PFX -and (Test-Path $env:SM_SIGN_PFX)
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if ($signing -and -not $signtool) { throw "signtool.exe not found (install the Windows SDK)." }
$signArgs = @("sign", "/f", $env:SM_SIGN_PFX, "/p", $env:SM_SIGN_PASSWORD, "/fd", "SHA256", "/tr", "http://timestamp.digicert.com", "/td", "SHA256")

if ($signing) { & $signtool.FullName @signArgs (Join-Path $publish "StatsMonitor.exe") }

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isinfo.php" }

$isccArgs = @("/DAppVersion=$Version", "/DSourceDir=$publish")
if ($signing) {
    $isccArgs += "/DSignTool"
    $isccArgs += "/Ssigntool=`"$($signtool.FullName)`" $($signArgs -join ' ') `$f"
}
& $iscc @isccArgs (Join-Path $PSScriptRoot "StatsMonitor.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Write-Host "Installer: $(Join-Path $root "artifacts\StatsMonitor-Setup-$Version.exe")"
