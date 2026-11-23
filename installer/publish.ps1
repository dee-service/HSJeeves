# Adds the installer built by build.ps1 to a downloads folder and keeps only the newest N versions
# (default 3, enough to roll back). The folder is what the Jeeves website serves at /downloads/
# (HS-Snake repo: docker-compose.yml mounts ./downloads into the www container).
#
#   .\installer\publish.ps1 -Dest dist\downloads -Notes "First release"            # local folder only
#   .\installer\publish.ps1 -Dest dist\downloads -Remote user@host:/srv/hs-snake/downloads
#
# -Remote: one ssh call removes pruned versions and lists the server folder, one scp call uploads only files that are
# missing there or differ in size, plus versions.json (needs OpenSSH; with key login there is no password prompt at all).
# The folder is the single source of truth: versions.json is rebuilt from what is in it.
param(
  [string]$Dest = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\downloads'),
  [string]$Notes = '',
  [int]$Keep = 3,
  [string]$Remote = '',
  # Private key file for ssh and scp. With it there is no password prompt (the key must be in authorized_keys on the server).
  [string]$IdentityFile = ''
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

  # Call 1 (ssh, one password prompt): delete pruned versions, then list what is already there with sizes.
  # Pruned files go first so the folder never holds more than -Keep versions; the manifest follows within seconds.
  $rm = if ($removed) { 'rm -f ' + (($removed | ForEach-Object { "'$path/$_'" }) -join ' ') + '; ' } else { '' }
  # -n: stdin from NUL. Windows OpenSSH can otherwise keep waiting on the console stdin after the remote command ended.
  $opt = @(); if ($IdentityFile) { $opt = @("-i", $IdentityFile, "-o", "IdentitiesOnly=yes") }
  $listing = ssh -n -T @opt -o ConnectTimeout=15 $hostPart "${rm}find '$path' -maxdepth 1 -type f -printf '%f %s\n'"
  if ($LASTEXITCODE) { throw "ssh failed (does $path exist on the server?)" }
  $remoteSize = @{}
  foreach ($line in $listing) { if ($line -match '^(.+) (\d+)$') { $remoteSize[$Matches[1]] = [int64]$Matches[2] } }

  # Upload only what is missing or has a different size. versions.json is tiny and always sent, last.
  $send = @()
  foreach ($e in $entries) {
    foreach ($suffix in '', '.sha256') {
      $name = $e.file + $suffix
      $local = Get-Item (Join-Path $Dest $name)
      if (-not $remoteSize.ContainsKey($name) -or $remoteSize[$name] -ne $local.Length) { $send += $local.FullName } else { Write-Host "Already on server: $name" }
    }
  }
  $send += (Join-Path $Dest 'versions.json')

  # Call 2 (scp, second prompt). scp copies in argument order: installers first, manifest last,
  # so the page never lists a file that is not there yet.
  scp @opt @send "${Remote}/"; if ($LASTEXITCODE) { throw 'scp failed' }
  Write-Host "Published to $Remote ($($send.Count - 1) file(s) uploaded + versions.json)"
}
