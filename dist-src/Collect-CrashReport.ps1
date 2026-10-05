# Collect Crash Report — Elden Vins x More Map Variations (merged)
# Gathers the logs needed to diagnose a crash or bug into one zip on your Desktop, with your Windows user name, PC name
# and Steam ID removed from the text files. Nothing is uploaded: attach the zip to your bug report yourself.
# Run "Collect Crash Report.bat" (double-click) right after the problem happened.
param(
    [switch]$NoPrompt,          # don't ask questions (no description, no crash dump)
    [switch]$IncludeDump,       # include the newest crash dump without asking
    [string]$OutDir = [Environment]::GetFolderPath('Desktop')
)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $PSScriptRoot
$profileFile = Get-ChildItem $root -Filter *.me3 | Select-Object -First 1
$profileName = if ($profileFile) { $profileFile.BaseName } else { 'Elden Vins with more map variations' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path ([IO.Path]::GetTempPath()) "VinsMMV-report-$stamp"
New-Item -ItemType Directory -Force $work | Out-Null

# --- redaction of personal data in text files
$secrets = @($env:USERPROFILE, $env:USERNAME, $env:COMPUTERNAME) | Where-Object { $_ -and $_.Length -ge 3 } | Sort-Object Length -Descending
function Protect-Text([string]$t, [switch]$KeepNumbers) {
    foreach ($s in $secrets) { $t = $t -replace [regex]::Escape($s), '<redacted>' }
    $t = $t -replace '7656119\d{10}', '<steamid>'                                   # SteamID64
    if (-not $KeepNumbers) { $t = $t -replace '\b(\d{1,3}\.){3}\d{1,3}\b', '<ip>' }    # IPv4 addresses (system info keeps versions)
    return $t
}
function Add-TextFile([string]$src, [string]$destName) {
    if (-not (Test-Path $src)) { return }
    try {
        $text = Get-Content -LiteralPath $src -Raw -ErrorAction Stop
        Set-Content -LiteralPath (Join-Path $work $destName) -Value (Protect-Text $text) -Encoding UTF8
        "  + $destName"
    } catch { "  ! could not read $src ($($_.Exception.Message))" }
}

Write-Host "Collecting logs for '$profileName' ..."

# 1. Mod Engine 3 logs (newest 5 for this profile)
$me3Logs = Join-Path $env:LOCALAPPDATA "garyttierney\me3\data\logs\$profileName"
if (Test-Path $me3Logs) {
    Get-ChildItem $me3Logs -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 5 |
        ForEach-Object { Add-TextFile $_.FullName "me3-$($_.Name)" }
} else { Write-Host "  (no Mod Engine 3 logs found at %LOCALAPPDATA%\garyttierney\me3\data\logs\$profileName)" }

# 2. Logs the mod's DLLs write next to themselves
Add-TextFile (Join-Path $root 'mod\ServerRedirector\cl_server_redirector.log') 'cl_server_redirector.log'
Get-ChildItem (Join-Path $root 'mod\dll\logs') -Filter *.log -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending |
    Select-Object -First 3 | ForEach-Object { Add-TextFile $_.FullName "custom_drop_fxrs-$($_.Name)" }

# 3. Windows crash records for the game (last 14 days)
try {
    $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddDays(-14) } -ErrorAction Stop |
        Where-Object { $_.Message -match 'nightreign' -and ($_.ProviderName -in 'Application Error', 'Windows Error Reporting', 'Application Hang') } |
        Select-Object -First 20 | ForEach-Object { "[$($_.TimeCreated)] $($_.ProviderName) (id $($_.Id))`n$($_.Message)`n" }
    Set-Content (Join-Path $work 'windows-crash-events.txt') (Protect-Text (($events | Out-String))) -Encoding UTF8
    "  + windows-crash-events.txt ($(@($events).Count) event(s))"
} catch { "  ! could not read the Windows event log" }

# 4. System / install information
$gameExe = $null
$me3Newest = Get-ChildItem $me3Logs -Filter *.log -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($me3Newest) { $m = Select-String -Path $me3Newest.FullName -Pattern 'exe: "([^"]+nightreign\.exe)"' | Select-Object -First 1; if ($m) { $gameExe = $m.Matches[0].Groups[1].Value -replace '\\\\', '\' -replace '/', '\' } }
$info = [ordered]@{
    'Report created'      = (Get-Date).ToString('u')
    'Mod folder files'    = (Get-ChildItem $root -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
    'Mod version'         = (Get-Content (Join-Path $root 'VERSION.txt') -ErrorAction SilentlyContinue | Select-Object -First 1)
    'nighter.dll present' = (Test-Path (Join-Path $root 'mod\dll\nighter.dll'))
    'Game exe version'    = if ($gameExe -and (Test-Path $gameExe)) { (Get-Item $gameExe).VersionInfo.FileVersion } else { 'unknown' }
    'Mod Engine 3'        = (& { try { (Get-Command me3 -ErrorAction Stop | ForEach-Object { & $_.Source --version }) } catch { 'me3 not on PATH' } })
    'Windows'             = (Get-CimInstance Win32_OperatingSystem | ForEach-Object { "$($_.Caption) $($_.Version)" })
    'CPU'                 = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
    'RAM (GB)'            = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
    'GPU'                 = ((Get-CimInstance Win32_VideoController | ForEach-Object { "$($_.Name) (driver $($_.DriverVersion))" }) -join '; ')
}
$infoText = ($info.GetEnumerator() | ForEach-Object { '{0}: {1}' -f $_.Key, $_.Value }) -join "`r`n"
$infoText += "`r`n`r`nProfile ($($profileFile.Name)):`r`n" + (Get-Content $profileFile.FullName -Raw -ErrorAction SilentlyContinue)
Set-Content (Join-Path $work 'system-info.txt') (Protect-Text $infoText -KeepNumbers) -Encoding UTF8
"  + system-info.txt"

# 5. What happened (optional)
if (-not $NoPrompt) {
    Write-Host ''
    $desc = Read-Host 'Describe briefly what happened (what you were doing, which map/boss/weapon; Enter to skip)'
    if ($desc) { Set-Content (Join-Path $work 'what-happened.txt') (Protect-Text $desc) -Encoding UTF8 }
}

# 6. Crash dump (opt-in: dumps contain game memory, which can include personal data)
$dump = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'CrashDumps') -Filter 'nightreign*.dmp' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($dump) {
    $take = $IncludeDump
    if (-not $take -and -not $NoPrompt) {
        $ans = Read-Host "Include the newest crash dump ($($dump.LastWriteTime), $([math]::Round($dump.Length/1MB)) MB)? It helps a lot but can contain personal data from memory. (y/N)"
        $take = $ans -match '^[yY]'
    }
    if ($take) { Copy-Item $dump.FullName (Join-Path $work $dump.Name); "  + $($dump.Name)" }
}

# 7. Zip
$zip = Join-Path $OutDir "VinsMMV-crash-report-$stamp.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($work, $zip)
Remove-Item $work -Recurse -Force
Write-Host ''
Write-Host "Report saved: $zip"
Write-Host 'Attach it to a post in the mod page''s Bugs tab (include what you were doing). Nothing was uploaded.'
if (-not $NoPrompt) { Start-Process explorer.exe "/select,`"$zip`"" ; Read-Host 'Press Enter to close' | Out-Null }
