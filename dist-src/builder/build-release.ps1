# Cuts a builder release: out\dist\VinsMMV-Merge-Builder-<Version>.zip (version = <Version> in src\NRMerge\NRMerge.csproj).
#   powershell -ExecutionPolicy Bypass -File dist-src\builder\build-release.ps1 [-SkipTests] [-AllowDirty] [-SkipAudit]
# 1. checks the upstream checkouts: Smithbox at cbd477a8 with patches\smithbox-hklib-nightreign-cmsg.patch applied,
#    DSLuaDecompiler at c27340ab with patches\dslua-net10.patch applied (git apply --check --reverse);
# 2. runs the tests; 3. publishes the self-contained single-file win-x64 NRMerge.exe (lua502.dll and libzstd.dll embedded);
# 4. stages exe + Res\ + data\ (manifests, Smithbox/Andre metadata, merge rules, delivery templates) + Build Merged Mod.bat +
#    README.txt + LICENSE.txt + licenses\ + source\ (git archive of HEAD) + SOURCES.txt;
# 5. runs "NRMerge.exe selftest" from the staged folder with no .NET on PATH and DOTNET_ROOT unset;
# 6. zips the staged folder (top-level folder = release name) and runs audit.ps1 on the zip.
param(
    [switch]$SkipTests,
    [switch]$AllowDirty,
    [switch]$SkipAudit,
    [string]$Repo = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path   # $PSScriptRoot is empty in param defaults on some hosts
if (-not $Repo) { $Repo = (Resolve-Path (Join-Path $here '..\..')).Path }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
function Step($t) { Write-Host "== $t" }
function Run($exe, [string[]]$a) { & $exe @a; if ($LASTEXITCODE -ne 0) { throw "$exe $($a -join ' ') failed ($LASTEXITCODE)" } }

$dotnet = Join-Path $Repo 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$csproj = Join-Path $Repo 'src\NRMerge\NRMerge.csproj'
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "no <Version> in $csproj" }
$name = "VinsMMV-Merge-Builder-$version"
$dist = Join-Path $Repo 'out\dist'
$stage = Join-Path $dist "stage\$name"
$zip = Join-Path $dist "$name.zip"
$publish = Join-Path $Repo 'out\builder-publish'
Step "release $name"

# --- 1. upstream checkouts + patches
$smithbox = Join-Path $Repo 'src\Smithbox'; $dslua = Join-Path $Repo 'src\DSLuaDecompiler'
foreach ($c in @(@($smithbox, 'cbd477a8fd6d436b3e011c8548e1de8fd8876918', 'smithbox-hklib-nightreign-cmsg.patch'),
                 @($dslua, 'c27340ab1b898a584748dc7e43a516cc1f1684f6', 'dslua-net10.patch'))) {
    $head = (git -C $c[0] rev-parse HEAD).Trim()
    if ($head -ne $c[1]) { throw "$($c[0]) is at $head, expected $($c[1])" }
    git -C $c[0] apply --check --reverse (Join-Path $Repo "patches\$($c[2])")
    if ($LASTEXITCODE -ne 0) { throw "patches\$($c[2]) is not applied in $($c[0])" }
    Write-Host "   $($c[0]) @ $($head.Substring(0,8)) + $($c[2]) applied"
}
$dirty = git -C $Repo status --porcelain --untracked-files=no --ignore-submodules=dirty -- src tests patches merge data dist-src
if ($dirty -and -not $AllowDirty) { throw "uncommitted changes (source\ is taken from HEAD; commit first or pass -AllowDirty):`n$($dirty -join "`n")" }
$commit = (git -C $Repo rev-parse HEAD).Trim()

# --- 2. tests
if (-not $SkipTests) { Step 'tests'; Run $dotnet @('test', (Join-Path $Repo 'tests\NRMerge.Tests'), '--nologo', '-v', 'q') }

# --- 3. publish
Step 'publish (self-contained single file, win-x64)'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
Run $dotnet @('publish', $csproj, '-c', 'Release', '-r', 'win-x64', '--self-contained', '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=embedded', '--nologo', '-o', $publish)
$unexpected = Get-ChildItem $publish -Recurse -File | Where-Object { $_.Name -ne 'NRMerge.exe' -and $_.DirectoryName -ne (Join-Path $publish 'Res') }
if ($unexpected) { throw "publish left files outside the single-file bundle: $($unexpected.FullName -join ', ')" }

# --- 4. stage
Step "stage $stage"
if (Test-Path (Split-Path $stage)) { Remove-Item (Split-Path $stage) -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
function Put($src, $rel) { $d = Join-Path $stage $rel; New-Item -ItemType Directory -Force (Split-Path $d) | Out-Null; Copy-Item -LiteralPath $src $d }
Put (Join-Path $publish 'NRMerge.exe') 'NRMerge.exe'
Get-ChildItem (Join-Path $publish 'Res') -File | ForEach-Object { Put $_.FullName "Res\$($_.Name)" }
Put (Join-Path $here 'Build Merged Mod.bat') 'Build Merged Mod.bat'
Put (Join-Path $here 'README.txt') 'README.txt'
Put (Join-Path $here 'licenses\GPL-3.0.txt') 'LICENSE.txt'
Get-ChildItem (Join-Path $here 'licenses') -File | ForEach-Object { Put $_.FullName "licenses\$($_.Name)" }
foreach ($f in 'mod-manifest.json', 'vanilla-manifest.tsv', 'fetch.json') { Put (Join-Path $Repo "data\$f") "data\$f" }
$assets = Join-Path $smithbox 'src\Smithbox.Data\Assets'
foreach ($g in 'NR', 'ER') {
    foreach ($d in 'Defs', 'Param Meta') {
        Get-ChildItem -LiteralPath (Join-Path $assets "PARAM\$g\$d") -File | ForEach-Object { Put $_.FullName "data\smithbox\PARAM\$g\$d\$($_.Name)" }
    }
    Put (Join-Path $assets "PARAM\$g\Param Type Info.json") "data\smithbox\PARAM\$g\Param Type Info.json"
}
Put (Join-Path $assets 'TAE\TAE.Template.NR.xml') 'data\smithbox\TAE\TAE.Template.NR.xml'
foreach ($f in 'EldenRingDictionary.txt', 'EldenRingNightreignDictionary.txt') {
    Put (Join-Path $smithbox "src\Andre\Andre.Formats\Resources\$f") "data\andre\$f"
}
foreach ($f in git -C $Repo ls-files merge) { Put (Join-Path $Repo $f) ("data\" + $f.Replace('/', '\')) }
foreach ($f in 'README.txt', 'Collect Crash Report.bat', 'Collect-CrashReport.ps1') { Put (Join-Path $Repo "dist-src\$f") "data\templates\$f" }

# source\: our complete source at HEAD (GPLv3 corresponding source; upstream checkouts are pinned in SOURCES.txt)
$srcZip = Join-Path $dist "stage\source-$version.zip"
Run git @('-C', $Repo, 'archive', '--format=zip', '-o', $srcZip, 'HEAD', '--', '.gitignore', 'src/NRMerge', 'src/LuaNorm', 'tests',
    'patches', 'merge', 'data', 'dist-src/builder', 'dist-src/README.txt', 'dist-src/Collect Crash Report.bat',
    'dist-src/Collect-CrashReport.ps1', 'docs/MAINTENANCE.md', 'docs/BUILDER_BRIEF.md')
[System.IO.Compression.ZipFile]::ExtractToDirectory($srcZip, (Join-Path $stage 'source'))
Remove-Item $srcZip
$sources = @"
VinsMMV Merge Builder ${version}: corresponding source
====================================================
source\ holds this repository's files at commit $commit (NRMerge, LuaNorm, tests, patches, merge rules, manifests,
release scripts). NRMerge.exe is built from them together with these upstream checkouts:

  Smithbox         https://github.com/vawser/Smithbox                 commit cbd477a8fd6d436b3e011c8548e1de8fd8876918
                   clone to src\Smithbox, then: git apply patches\smithbox-hklib-nightreign-cmsg.patch
                   (projects used: Andre.SoulsFormats, Andre.Formats, Andre.IO, Andre.Core, Havok.HKLib,
                   Havok.HKLib.Reflection, Havok.HKLib.Serialization, Havok.Shared)
  DSLuaDecompiler  https://github.com/katalash/DSLuaDecompiler        commit c27340ab1b898a584748dc7e43a516cc1f1684f6
                   clone to src\DSLuaDecompiler, then: git apply patches\dslua-net10.patch
                   (projects used: LuaCompiler incl. LuaNative\lua502.dll, LuaDecompilerCore)
  NuGet packages   restored by dotnet (versions in the .csproj files and licenses\THIRD-PARTY-NOTICES.txt)

Build with the .NET 10 SDK ($((& $dotnet --version).Trim()) was used):
  powershell -ExecutionPolicy Bypass -File dist-src\builder\build-release.ps1
or just the exe:
  dotnet publish src\NRMerge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

Files downloaded at build time (not part of this release, see data\fetch.json): Smithbox's Nightreign 1.03.4
regulation.bin, El-Fonz0/EldenRingNightreignHKS c0000.hks @ 197b182e, AinTunez/DarkScript3 nr-common.emedf.json @ 4b570f51.
"@
[IO.File]::WriteAllText((Join-Path $stage 'SOURCES.txt'), $sources.Replace("`r`n", "`n").Replace("`n", "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

# --- 5. selftest of the staged exe without any .NET on PATH
Step 'selftest (DOTNET_ROOT unset, no dotnet on PATH)'
$psi = New-Object System.Diagnostics.ProcessStartInfo (Join-Path $stage 'NRMerge.exe'), 'selftest'
$psi.UseShellExecute = $false; $psi.WorkingDirectory = $stage; $psi.RedirectStandardOutput = $true
foreach ($v in 'DOTNET_ROOT', 'DOTNET_ROOT(x86)', 'DOTNET_ROOT_X64', 'DOTNET_HOST_PATH', 'MSBuildExtensionsPath', 'MSBUILD_EXE_PATH') { $psi.EnvironmentVariables.Remove($v) }
$psi.EnvironmentVariables['PATH'] = "$env:SystemRoot\system32;$env:SystemRoot"
$p = [System.Diagnostics.Process]::Start($psi); $o = $p.StandardOutput.ReadToEnd(); $p.WaitForExit()
Write-Host $o
if ($p.ExitCode -ne 0) { throw "selftest failed ($($p.ExitCode))" }

# --- 6. zip + audit
Step "zip $zip"
if (Test-Path $zip) { Remove-Item $zip }
# entries written one by one: Windows PowerShell's ZipFile.CreateFromDirectory stores backslash separators (invalid in zip)
$za = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in Get-ChildItem $stage -Recurse -File | Sort-Object FullName) {
        $rel = $name + '/' + $f.FullName.Substring($stage.Length + 1).Replace([char]92, [char]47)
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $za.Dispose() }
Write-Host ('   {0:N1} MB' -f ((Get-Item $zip).Length / 1MB))
if (-not $SkipAudit) {
    Step 'audit'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'audit.ps1') -Zip $zip -Repo $Repo
    if ($LASTEXITCODE -ne 0) { throw "audit failed ($LASTEXITCODE)" }
}
Step "done: $zip"
