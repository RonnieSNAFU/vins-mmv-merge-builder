# Audits a builder release zip: it must contain no file from either mod, the games or the pinned third-party downloads.
#   powershell -ExecutionPolicy Bypass -File dist-src\builder\audit.ps1 -Zip out\dist\VinsMMV-Merge-Builder-<v>.zip
# 1. lists every entry (path, size, SHA-256) into <zip>.audit.txt and the console;
# 2. fails on forbidden extensions (game/mod formats, DLLs, profiles, Oodle, nested archives), except our own build output
#    (NRMerge.exe; lua502.dll is allowed too but is embedded in the exe, not a zip entry);
# 3. fails if any entry's SHA-256 equals any file in the corpus: both mod downloads, the vanilla extracts of both games,
#    the game folders themselves, the pinned downloads (caches + the dev copies they equal) and the installed merged mod\.
#    Only corpus files whose size equals some entry's size are hashed (equal SHA-256 implies equal size), so the ~25 GB
#    corpus costs seconds, not minutes. Empty entries are listed but cannot match anything meaningful and are skipped.
# 4. prints the zip size. Exit code 0 = clean, 1 = findings, 2 = usage/IO error.
param(
    [Parameter(Mandatory = $true)][string]$Zip,
    [string]$Repo = '',
    [string]$NightreignRoot = '',   # default: $env:NRMERGE_NIGHTREIGN_ROOT, else <SteamLibrary>\steamapps\common\ELDEN RING NIGHTREIGN
    [string]$EldenRingGame = '',    # default: $env:NRMERGE_ELDENRING_GAME, else <SteamLibrary>\steamapps\common\ELDEN RING\Game
    [string[]]$ExtraCorpus = @(),
    [switch]$AllowMissingCorpus   # a missing corpus root is a finding unless this is given (the audit would be incomplete)
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path   # $PSScriptRoot is empty in param defaults on some hosts
if (-not $Repo) { $Repo = (Resolve-Path (Join-Path $here '..\..')).Path }
$steamCommon = 'C:\Program Files (x86)\Steam\steamapps\common'
if (-not $NightreignRoot) { $NightreignRoot = if ($env:NRMERGE_NIGHTREIGN_ROOT) { $env:NRMERGE_NIGHTREIGN_ROOT } else { Join-Path $steamCommon 'ELDEN RING NIGHTREIGN' } }
if (-not $EldenRingGame) { $EldenRingGame = if ($env:NRMERGE_ELDENRING_GAME) { $env:NRMERGE_ELDENRING_GAME } else { Join-Path $steamCommon 'ELDEN RING\Game' } }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$Zip = (Resolve-Path $Zip).Path
$corpus = @(
    (Join-Path $NightreignRoot 'ELDEN VINS NIGHTREIGN'),
    (Join-Path $NightreignRoot 'More Map Variations 2.1.8-hotfix3 & Weapons Mod'),
    (Join-Path $NightreignRoot 'Elden Vins with more map variations\mod'),
    (Join-Path $NightreignRoot 'Game'),
    $EldenRingGame,
    (Join-Path $Repo 'vanilla'),
    (Join-Path $Repo 'vanilla_er'),
    (Join-Path $Repo 'out\cache'),
    (Join-Path $env:LOCALAPPDATA 'VinsMMV-Merge-Builder\cache'),
    (Join-Path $Repo 'src\Smithbox\src\Smithbox.Data\Assets\PARAM\NR\Regulations'),
    (Join-Path $Repo 'src\Smithbox\src\Smithbox.Data\Assets\PARAM\ER\Regulations'),
    (Join-Path $Repo 'tools\darkscript'),
    (Join-Path $Repo 'src\hks')
) + $ExtraCorpus

$forbiddenExt = '.dcx', '.bin', '.hks', '.lua', '.luabnd', '.dll', '.me3', '.exe', '.bnd', '.bhd', '.bdt', '.hkx', '.tae',
    '.anibnd', '.behbnd', '.chrbnd', '.partsbnd', '.ffxbnd', '.gfx', '.fxr', '.flver', '.param', '.emevd', '.msb', '.fmg',
    '.sl2', '.cl_save', '.zip', '.7z', '.rar', '.pdb'
$allowed = 'NRMerge.exe', 'lua502.dll'

# --- 1. entries
$sha = [System.Security.Cryptography.SHA256]::Create()
$entries = @()
$z = [System.IO.Compression.ZipFile]::OpenRead($Zip)
try {
    foreach ($e in $z.Entries) {
        if ($e.FullName.EndsWith('/')) { continue }   # directory entry
        $s = $e.Open()
        try { $h = -join ($sha.ComputeHash($s) | ForEach-Object { $_.ToString('x2') }) } finally { $s.Dispose() }
        $entries += [pscustomobject]@{ Path = $e.FullName; Size = $e.Length; Sha = $h }
    }
} finally { $z.Dispose() }

$report = New-Object System.Collections.Generic.List[string]
$report.Add("audit of $Zip")
$report.Add("entries: $($entries.Count)")
foreach ($e in $entries | Sort-Object Path) { $report.Add(('{0,12}  {1}  {2}' -f $e.Size, $e.Sha, $e.Path)) }

# --- 2. extensions
$findings = New-Object System.Collections.Generic.List[string]
foreach ($e in $entries) {
    $name = [IO.Path]::GetFileName($e.Path.Replace([char]92, [char]47))
    $ext = [IO.Path]::GetExtension($name).ToLowerInvariant()
    if ($e.Path.Contains([char]92)) { $findings.Add("backslash in entry name (not portable): $($e.Path)") }
    if ($allowed -contains $name) { continue }
    if ($forbiddenExt -contains $ext -or $name -like 'oo2core*') { $findings.Add("forbidden file type: $($e.Path)") }
}

# --- 3. hashes against the corpus (size prefilter)
$bySize = @{}
foreach ($e in $entries) { if ($e.Size -gt 0) { if (-not $bySize.ContainsKey($e.Size)) { $bySize[$e.Size] = @() }; $bySize[$e.Size] += $e } }
$scanned = 0; $hashed = 0; $missingRoots = @()
foreach ($root in $corpus) {
    if (-not (Test-Path -LiteralPath $root)) { $missingRoots += $root; continue }
    foreach ($f in Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue) {
        $scanned++
        if (-not $bySize.ContainsKey($f.Length)) { continue }
        $hashed++
        $fs = [IO.File]::OpenRead($f.FullName)
        try { $h = -join ($sha.ComputeHash($fs) | ForEach-Object { $_.ToString('x2') }) } finally { $fs.Dispose() }
        foreach ($e in $bySize[$f.Length]) { if ($e.Sha -eq $h) { $findings.Add("third-party content: $($e.Path) == $($f.FullName)") } }
    }
}
$report.Add("corpus: $scanned files scanned, $hashed hashed (size match), roots: $($corpus.Count - $missingRoots.Count) present")
foreach ($m in $missingRoots) { $report.Add("corpus root missing (skipped): $m"); if (-not $AllowMissingCorpus) { $findings.Add("corpus root missing: $m") } }
$empty = @($entries | Where-Object { $_.Size -eq 0 })
if ($empty.Count -gt 0) { $report.Add("empty entries (not hash-checked): " + (($empty | ForEach-Object Path) -join ', ')) }

# --- 4. size and verdict
$zipSize = (Get-Item $Zip).Length
$report.Add(('zip size: {0:N0} bytes ({1:N1} MB)' -f $zipSize, ($zipSize / 1MB)))
foreach ($f in $findings) { $report.Add("FINDING  $f") }
$report.Add($(if ($findings.Count -eq 0) { 'AUDIT OK: no forbidden file types, no file equal to any mod, game or fetched file' } else { "AUDIT FAILED: $($findings.Count) finding(s)" }))

$out = "$Zip.audit.txt"
[IO.File]::WriteAllLines($out, $report, (New-Object System.Text.UTF8Encoding($false)))
$report | Select-Object -Skip ($entries.Count + 2) | ForEach-Object { Write-Host $_ }
Write-Host "entry list: $out"
if ($missingRoots.Count -gt 0) { Write-Host "WARNING: $($missingRoots.Count) corpus root(s) missing, see the list above" }
exit $(if ($findings.Count -eq 0) { 0 } else { 1 })
