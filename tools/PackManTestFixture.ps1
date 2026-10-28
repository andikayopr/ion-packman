<#
.SYNOPSIS
  Sandbox "mesin ATM" untuk uji manual ION PackMan Tahap 1c. JALANKAN HANYA DI VM / MESIN DEV, sebagai Administrator.

.DESCRIPTION
  Semua berada di C:\PackManTest dan HKLM\SOFTWARE\WOW6432Node\PackManTest. Tidak menyentuh D:\ION, D:\APCONTAINER,
  maupun key Registry aplikasi asli. Service yang dipakai adalah service Windows bawaan yang aman dihentikan di VM
  (default: Spooler) sebagai pengganti service WIND.

  -Action Setup       buat sandbox (target, INI aplikasi, Registry, config PackMan, folder build sumber)
  -Action LargeBuild  ganti folder build sumber dengan ~400 file x 1 MB (untuk uji kill-saat-copy)
  -Action LockFile    tahan a.dll (dibaca boleh, diganti tidak) sampai Enter: simulasi file terkunci
  -Action State       cetak kondisi mesin (hash file, INI, Registry, service) untuk dibandingkan sebelum/sesudah
  -Action Cleanup     hapus semuanya (sandbox + Registry uji); service dikembalikan ke Automatic/Running
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Setup', 'ConfigOnly', 'LargeBuild', 'LockFile', 'State', 'Cleanup')][string]$Action,
    [string]$Root = 'C:\PackManTest',
    [string]$ServiceName = 'Spooler',
    [string]$RegKey = 'SOFTWARE\WOW6432Node\PackManTest\User\WebUI'
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Test-Admin {
    $p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Write-Text($path, $text) {
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, $text, $utf8)
}

$target  = Join-Path $Root 'APCONTAINER\WIND'
$ini     = Join-Path $Root 'APCONTAINER\Config\WebUIUXCNFG.ini'
$release = Join-Path $Root 'release'
$machine = Join-Path $Root 'machine.ini'
$cache   = Join-Path $Root 'Cache'
$src     = Join-Path $Root 'buildsrc\wind-2.1.0'
$hklmKey = "HKLM:\$RegKey"
$pmKey   = "HKLM\$RegKey"

function Write-PackManConfig {
    Write-Text $machine "[Meta]`r`nSchemaVersion=1.0`r`n`r`n[Machine]`r`nMachineName=$env:COMPUTERNAME`r`nEnvironment=Development`r`nProfile=`r`n`r`n[InstallLocations]`r`nTEST=$Root`r`n`r`n[PackMan]`r`nLocalCacheDirectory=$cache`r`n"
    Write-Text (Join-Path $release 'Config\allowlist.ini') "[Meta]`r`nSchemaVersion=1.0`r`n`r`n[BaseDirectories]`r`nTEST=$Root`r`n`r`n[RegistryKeys]`r`nWebUI=$pmKey`r`n`r`n[IniFiles]`r`nWebUIUXCNFG=$ini`r`n`r`n[Services]`r`nSvc=$ServiceName`r`n"
    Write-Text (Join-Path $release 'Components\wind\wind-2.1.0.ini') "[Meta]`r`nSchemaVersion=1.0`r`n`r`n[Component]`r`nId=wind`r`nDisplayName=WIND (uji)`r`nReleaseVersion=2.1.0`r`nBuildReference=`r`n`r`n[Installation]`r`nMode=in-place`r`nTargetDirectory=$target`r`n`r`n[WindowsService]`r`nServiceName=$ServiceName`r`n`r`n[LocalBuild]`r`nFolderName=`r`nFileCount=`r`nImportedAt=`r`nChecksumFile=`r`n"
    Write-Text (Join-Path $release 'Profiles\WINDTEST.ini') "[Meta]`r`nSchemaVersion=1.0`r`n`r`n[Profile]`r`nId=WINDTEST`r`nDisplayName=Wind Test`r`nVersion=1.0.0`r`nDeploymentMode=in-place`r`n`r`n[Features]`r`nWindWebUI=true`r`nJnlLog=true`r`n`r`n[Component:wind]`r`nVersion=2.1.0`r`nDependsOn=`r`n`r`n[Registry:WebUISupport]`r`nKey=$pmKey`r`nValueName=Support`r`nType=DWORD`r`nValue=1`r`nFeature=WindWebUI`r`n`r`n[Registry:WebUIDisplayFlag]`r`nKey=$pmKey`r`nValueName=DisplayFlag`r`nType=DWORD`r`nValue=1`r`nFeature=WindWebUI`r`n`r`n[Ini:WebUISupport]`r`nFile=$ini`r`nSection=WebUI`r`nKey=Support`r`nValue=1`r`nFeature=WindWebUI`r`n`r`n[Ini:WebUIJnlLog]`r`nFile=$ini`r`nSection=Debug`r`nKey=JNLLog`r`nValue=1`r`nFeature=JnlLog`r`n"
}

switch ($Action) {

'Setup' {
    if (-not (Test-Admin)) { throw 'Jalankan sebagai Administrator (perlu menulis HKLM).' }
    if (-not (Get-Service $ServiceName -ErrorAction SilentlyContinue)) { throw "Service '$ServiceName' tidak ada." }
    if ((Get-Service $ServiceName).StartType -eq 'Disabled') { Set-Service $ServiceName -StartupType Automatic }
    if ((Get-Service $ServiceName).Status -ne 'Running') { Start-Service $ServiceName }

    # --- "terpasang" di mesin: file lama + file yang TIDAK dideklarasikan build (harus tetap utuh)
    Write-Text (Join-Path $target 'a.dll') 'versi-lama-a'
    Write-Text (Join-Path $target 'keep.txt') 'file-tak-dideklarasikan'
    # INI aplikasi: sengaja ada komentar, key lain, dan line ending CRLF, untuk membuktikan edit surgical
    Write-Text $ini "; INI aplikasi (jangan dirusak)`r`n[WebUI]`r`nSupport=0`r`nOther=keep`r`n`r`n[Debug]`r`nJNLLog=0`r`n"
    New-Item -Force -Path $hklmKey | Out-Null
    New-ItemProperty -Path $hklmKey -Name 'Support' -Value 0 -PropertyType DWord -Force | Out-Null   # nilai lama ada
    Remove-ItemProperty -Path $hklmKey -Name 'DisplayFlag' -ErrorAction SilentlyContinue             # nilai lama TIDAK ada

    # --- build sumber (yang nanti diimpor lewat UI)
    if (Test-Path (Split-Path $src)) { Remove-Item -Recurse -Force (Split-Path $src) }
    Write-Text (Join-Path $src 'a.dll') 'versi-baru-a'
    Write-Text (Join-Path $src 'sub\b.dll') 'versi-baru-b'

    # --- config PackMan (semua INI)
    Write-PackManConfig

    Write-Host "Sandbox siap di $Root"
    Write-Host "Jalankan PackMan:"
    Write-Host "  IonPackMan.App.exe --release `"$release`" --machine `"$machine`""
    Write-Host "Folder build untuk diimpor: $src"
}

'ConfigOnly' { Write-PackManConfig; Write-Host "Config ditulis ke $Root (tanpa Registry/service/file target)." }

'LargeBuild' {
    if (-not (Test-Path (Split-Path $src))) { throw 'Jalankan -Action Setup dulu.' }
    Remove-Item -Recurse -Force $src
    New-Item -ItemType Directory -Force -Path $src | Out-Null
    $buf = New-Object byte[] (1MB)
    (New-Object Random 42).NextBytes($buf)
    1..400 | ForEach-Object { [IO.File]::WriteAllBytes((Join-Path $src ("f{0:D3}.bin" -f $_)), $buf) }
    [IO.File]::WriteAllText((Join-Path $src 'a.dll'), 'versi-baru-a', $utf8)
    Write-Host "Build besar siap: 401 file di $src. Impor ulang lewat UI (hapus Cache\Builds\wind\2.1.0 jika sudah pernah diimpor)."
}

'State' {
    Write-Host '=== FILE (hash) ==='
    if (Test-Path $target) {
        Get-ChildItem $target -Recurse -Force | Sort-Object FullName | ForEach-Object {
            if ($_.PSIsContainer) { "D  $($_.FullName)" }
            else { 'F  {0}  {1}' -f $_.FullName, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 12) }
        }
    }
    Write-Host '=== INI aplikasi ==='
    if (Test-Path $ini) { [IO.File]::ReadAllText($ini) -replace "`r", '\r' }
    Write-Host '=== REGISTRY ==='
    if (Test-Path $hklmKey) { Get-ItemProperty $hklmKey | Select-Object Support, DisplayFlag | Format-List | Out-String }
    else { '(key tidak ada)' }
    Write-Host '=== SERVICE ==='
    Get-Service $ServiceName | Select-Object Name, Status, StartType | Format-Table | Out-String
    Write-Host '=== CACHE ==='
    foreach ($d in 'Journal', 'History', 'Backups', 'Builds') {
        $p = Join-Path $cache $d
        '{0,-8}: {1}' -f $d, $(if (Test-Path $p) { (Get-ChildItem $p | Select-Object -ExpandProperty Name) -join ', ' } else { '-' })
    }
}

'LockFile' {
    # Menahan a.dll (boleh dibaca, tidak boleh diganti) sampai Enter ditekan: mensimulasikan file terkunci proses lain.
    $f = [IO.File]::Open((Join-Path $target 'a.dll'), 'Open', 'Read', 'Read')
    try {
        Write-Host "a.dll DIKUNCI. Jalankan eksekusi di PackMan; tekan Enter di sini untuk melepas kunci."
        [void](Read-Host)
    } finally { $f.Dispose(); Write-Host 'Kunci dilepas.' }
}

'Cleanup' {
    if (-not (Test-Admin)) { throw 'Jalankan sebagai Administrator.' }
    Set-Service $ServiceName -StartupType Automatic
    if ((Get-Service $ServiceName).Status -ne 'Running') { Start-Service $ServiceName }
    if (Test-Path 'HKLM:\SOFTWARE\WOW6432Node\PackManTest') { Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\WOW6432Node\PackManTest' }
    if (Test-Path $Root) { Remove-Item -Recurse -Force $Root }
    Write-Host 'Sandbox dihapus; service dikembalikan Automatic/Running.'
}
}
