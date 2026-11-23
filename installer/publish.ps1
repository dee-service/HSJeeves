# Adds the installer built by build.ps1 to a downloads folder and keeps only the newest N versions
# (default 3, enough to roll back). The folder is what the Jeeves website serves at /downloads/
# (HS-Snake repo: docker-compose.yml mounts ./downloads into the www container).
#
#   .\installer\publish.ps1 -Dest dist\downloads -Notes "First release"            # local folder only
#   .\installer\publish.ps1 -Dest dist\downloads -Remote user@host:/srv/hs-snake/downloads
#
# -Remote then copies the files with scp and removes pruned versions over ssh (needs OpenSSH, key login).
# The folder is the single source of truth: versions.json is rebuilt from what is in it.
param(
  [string]$Dest = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\downloads'),
  [string]$Notes = '',
  [int]$Keep = 3,
  [string]$Remote = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$csproj = [xml](Get-Content (Join-Path $root 'src\HSJeeves\HSJeeves.csproj'))
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$name = "HSJeeves-Setup-$version.exe"
$setup = Join-Path $root "dist\$name"
if (-not (Test-Path $setup)) { throw "$setup not found. Run installer\build.ps1 first." }
if ($Keep -lt 1) { throw '-Keep must be at least 1' }

New-Item -ItemType Directory -Force $Dest | Out-Null
Copy-Item $setup (Join-Path $Dest $name) -Force
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
"$hash  $name" | Set-Content -Encoding ascii (Join-Path $Dest "$name.sha256")

# Notes are kept per version in a small side file, so they survive rebuilding the manifest.
if ($Notes) { Set-Content -Encoding utf8 (Join-Path $Dest "$name.notes.txt") $Notes }

# Newest first by version number.
$files = Get-ChildItem $Dest -Filter 'HSJeeves-Setup-*.exe' |
  ForEach-Object { [pscustomobject]@{ File = $_; Version = [version]($_.BaseName -replace '^HSJeeves-Setup-', '') } } |
  Sort-Object Version -Descending

$keepFiles = @($files | Select-Object -First $Keep)
$removed = @()
foreach ($old in ($files | Select-Object -Skip $Keep)) {
  foreach ($suffix in '', '.sha256', '.notes.txt') {
    $p = $old.File.FullName + $suffix
    if (Test-Path $p) { Remove-Item $p -Force; $removed += (Split-Path $p -Leaf) }
  }
}

$entries = @()
foreach ($k in $keepFiles) {
  $f = $k.File
  $sha = (Get-Content ($f.FullName + '.sha256') -TotalCount 1).Split(' ')[0]
  $notesFile = $f.FullName + '.notes.txt'
  $entries += [ordered]@{
    version = $k.Version.ToString()
    file    = $f.Name
    sha256  = $sha
    size    = $f.Length
    date    = $f.LastWriteTimeUtc.ToString('yyyy-MM-dd')
    notes   = $(if (Test-Path $notesFile) { (Get-Content $notesFile -Raw).Trim() } else { '' })
  }
}
$manifest = [ordered]@{ latest = $entries[0].version; versions = $entries }
($manifest | ConvertTo-Json -Depth 5) | Set-Content -Encoding utf8 (Join-Path $Dest 'versions.json')
Write-Host "Kept: $($entries.version -join ', ')   Removed: $(if ($removed) { $removed -join ', ' } else { 'none' })"

if ($Remote) {
  if ($Remote -notmatch '^([^:]+):(.+)$') { throw '-Remote must look like user@host:/path' }
  $hostPart = $Matches[1]; $path = $Matches[2]
  # One scp call = one password prompt. scp copies in argument order: installers first, manifest last,
  # so the page never lists a file that is not there yet.
  $send = @()
  foreach ($e in $entries) { $send += (Join-Path $Dest $e.file); $send += (Join-Path $Dest ($e.file + '.sha256')) }
  $send += (Join-Path $Dest 'versions.json')
  scp @send "${Remote}/"; if ($LASTEXITCODE) { throw 'scp failed' }
  # Pruned versions are deleted in one ssh call (a second prompt, only when something was pruned).
  if ($removed) {
    $rm = ($removed | ForEach-Object { "'$path/$_'" }) -join ' '
    ssh $hostPart "rm -f $rm"; if ($LASTEXITCODE) { throw 'ssh rm failed' }
  }
  Write-Host "Published to $Remote"
}
