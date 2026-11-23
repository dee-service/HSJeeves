# Builds Release and compiles the installer: dist\HSJeeves-Setup-<version>.exe
# Needs the DLLs in src\lib (docs\build.md) and Inno Setup 6.3+ (ISCC.exe).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Version comes from the app project (single source of truth, see docs\open-questions.md G3).
$csproj = [xml](Get-Content (Join-Path $root 'src\HSJeeves\HSJeeves.csproj'))
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in HSJeeves.csproj' }

$iscc = @(
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it first (winget install JRSoftware.InnoSetup).' }

dotnet build (Join-Path $root 'HSJeeves.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet test (Join-Path $root 'HSJeeves.sln') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }

& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'HSJeeves.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed' }
Write-Host "Done: dist\HSJeeves-Setup-$version.exe"
