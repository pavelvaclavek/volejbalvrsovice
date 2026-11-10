<#
.SYNOPSIS
    Sestavi aplikaci a zabali vystup do ZIPu pro xcopy nasazeni.

.DESCRIPTION
    1. dotnet build -c Release (lze preskocit prepinacem -NoBuild)
    2. z src\VolejbalVrsovice.App\bin\<Configuration>\net48 vybere soubory do ZIPu:
         - bez slozky Data\ (uzivatelska data se nikdy nebali),
         - bez *.pdb a *.xml,
         - ze slozky runtimes\ jen win-x64\native\e_sqlcipher.dll,
         - NAVOD.txt z korene repozitare, pokud existuje.
    3. vytvori artifacts\VolejbalVrsovice_vX.Y.Z.zip (soubory jsou v koreni ZIPu),
    4. vypise velikost a SELZE, pokud ZIP prekroci limit (vychozi 5 MB).

    Skript je zamerne jen v ASCII, aby fungoval ve Windows PowerShellu 5.1 i v PowerShellu 7.

.EXAMPLE
    .\build\pack.ps1

.EXAMPLE
    .\build\pack.ps1 -Version 1.2.3
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    # Verze X.Y.Z (bez "v"). Predava se do buildu jako -p:Version. Bez ni se pouzije verze z .exe.
    [string]$Version = '',

    # Kam ulozit ZIP. Vychozi: <repozitar>\artifacts
    [string]$OutputDirectory = '',

    # Nezpoustet build, zabalit existujici vystup.
    [switch]$NoBuild,

    [double]$MaxSizeMB = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'VolejbalVrsovice.sln'
$binDir = Join-Path $repoRoot "src\VolejbalVrsovice.App\bin\$Configuration\net48"
$exeName = 'VolejbalVrsovice.exe'

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts'
}

function Test-IncludeInPackage {
    param([string]$RelativePath)

    $path = $RelativePath.Replace('/', '\')

    if ($path -match '^Data\\') { return $false }               # uzivatelska data
    if ($path -match '\.(pdb|xml)$') { return $false }          # symboly a XML dokumentace
    if ($path -match '^runtimes\\') {                           # nativni knihovny: jen x64 SQLCipher
        return ($path -ieq 'runtimes\win-x64\native\e_sqlcipher.dll')
    }

    return $true
}

# --- 1. build ---------------------------------------------------------------------------------
if (-not $NoBuild) {
    $buildArgs = @('build', $solution, '-c', $Configuration, '--nologo')
    if ($Version) {
        $buildArgs += "-p:Version=$Version"
    }

    Write-Host "dotnet $($buildArgs -join ' ')"
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Build selhal (navratovy kod $LASTEXITCODE)."
    }
}

$exePath = Join-Path $binDir $exeName
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Vystup buildu nenalezen: $exePath"
}

if (-not $Version) {
    $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
    $Version = ($productVersion -split '\+')[0]
}

if ($Version -notmatch '^\d+\.\d+\.\d+') {
    throw "Neplatna verze '$Version' (ocekava se X.Y.Z)."
}

# --- 2. vyber souboru -------------------------------------------------------------------------
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ('VolejbalVrsovice_pack_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null

try {
    $binRoot = (Resolve-Path -LiteralPath $binDir).Path.TrimEnd('\') + '\'
    $fileCount = 0

    foreach ($file in Get-ChildItem -LiteralPath $binDir -Recurse -File) {
        $relative = $file.FullName.Substring($binRoot.Length)
        if (-not (Test-IncludeInPackage $relative)) {
            continue
        }

        $target = Join-Path $stage $relative
        $targetDir = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir | Out-Null
        }

        Copy-Item -LiteralPath $file.FullName -Destination $target
        $fileCount++
    }

    $guide = Join-Path $repoRoot 'NAVOD.txt'
    if (Test-Path -LiteralPath $guide) {
        Copy-Item -LiteralPath $guide -Destination (Join-Path $stage 'NAVOD.txt')
        $fileCount++
    }

    # --- 3. ZIP -------------------------------------------------------------------------------
    if (-not (Test-Path -LiteralPath $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    }

    $zipPath = Join-Path $OutputDirectory "VolejbalVrsovice_v$Version.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stage, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}

# --- 4. kontrola velikosti --------------------------------------------------------------------
$sizeBytes = (Get-Item -LiteralPath $zipPath).Length
$maxBytes = [long]($MaxSizeMB * 1MB)
$sizeText = '{0:N2} MB ({1:N0} B)' -f ($sizeBytes / 1MB), $sizeBytes

Write-Host ''
Write-Host "ZIP:      $zipPath"
Write-Host "Souboru:  $fileCount"
Write-Host "Velikost: $sizeText, limit $MaxSizeMB MB"

if ($sizeBytes -gt $maxBytes) {
    throw "ZIP je prilis velky: $sizeText, limit je $MaxSizeMB MB."
}

Write-Host 'OK - ZIP se vejde do limitu.'

# Cesta k ZIPu jako vystup skriptu (pro navazujici kroky, napr. GitHub Actions).
Write-Output $zipPath
