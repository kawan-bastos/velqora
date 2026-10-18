[CmdletBinding()]
param([string]$Iscc = $env:VELQORA_ISCC, [switch]$Sign)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versão inválida em Directory.Build.props.' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") { throw 'A tag não corresponde à versão centralizada.' }
if (-not $Iscc) {
    $bundled = Join-Path $root '.tools\Inno Setup 6\ISCC.exe'
    $Iscc = if (Test-Path -LiteralPath $bundled) { $bundled } else { "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
}
if (-not (Test-Path -LiteralPath $Iscc)) { throw 'Instale Inno Setup 6.7.3 ou execute scripts\Get-InnoSetup.ps1.' }
function Assert-ProjectPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $boundary = [IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw "Destino fora do projeto: $resolved" }
    return $resolved
}
dotnet build (Join-Path $root 'MacroDoButler.csproj') --configuration Release -warnaserror -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar Velqora em Release.' }
$compiled = Join-Path $root 'bin\Release\net48'
$staging = Join-Path $root ('dist\staging\v' + $version + '-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $staging 'payload'
$package = Join-Path $staging 'package'
New-Item -ItemType Directory -Path $payload,$package | Out-Null
# Explicit allowlist: never package user data, old builds or the installation directory.
foreach ($name in @('Velqora.exe', 'Velqora.exe.config')) {
    $source = Join-Path $compiled $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Arquivo compilado ausente: $name" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $payload $name)
}
if ($Sign) { & (Join-Path $PSScriptRoot 'Sign-Artifact.ps1') -Path (Join-Path $payload 'Velqora.exe') }
& $Iscc "/DAppVersion=$version" "/DPayload=$payload" "/DOutput=$package" (Join-Path $root 'installer\Velqora.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }
$installer = Join-Path $package "Velqora-Setup-Windows-v$version.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'O instalador não foi criado.' }
if ($Sign) { & (Join-Path $PSScriptRoot 'Sign-Artifact.ps1') -Path $installer }
# Version 3.2 looks up this exact name. Copy only after signing so both names
# contain the same bytes and both are included in SHA256SUMS.txt.
Copy-Item -LiteralPath $installer -Destination (Join-Path $package "Velqora-Setup-v$version.exe")
foreach ($name in @('Velqora.exe','Velqora.exe.config')) {
    Copy-Item -LiteralPath (Join-Path $payload $name) -Destination (Join-Path $package $name)
}
Copy-Item -LiteralPath (Join-Path $payload 'Velqora.exe') -Destination (Join-Path $package "Velqora-v$version.exe")
Copy-Item -LiteralPath (Join-Path $payload 'Velqora.exe.config') -Destination (Join-Path $package "Velqora-v$version.exe.config")
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $package 'LICENSE.txt')
$status = if ($Sign) { 'ASSINADO: executável e instalador verificados por SignTool /pa /all /tw e Authenticode.' } else { 'NÃO ASSINADO: nenhum certificado configurado. Não existe identidade de editor verificada.' }
"Velqora $version`r`n$status" | Set-Content -LiteralPath (Join-Path $package 'SIGNING-STATUS.txt') -Encoding utf8
$notesPath = Join-Path $root "docs\releases\v$version.md"
$notes = if (Test-Path -LiteralPath $notesPath) { Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8 } else { "Velqora $version para Windows" }
"$notes`r`n`r`n$status`r`n`r`nInstale usando Velqora-Setup-Windows-v$version.exe. Dados pessoais ficam em %LOCALAPPDATA%\Velqora." | Set-Content -LiteralPath (Join-Path $package 'RELEASE-NOTES.md') -Encoding utf8
$files = Get-ChildItem -LiteralPath $package -File | Sort-Object Name
$hashes = foreach ($file in $files) { '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name }
$hashes | Set-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -Encoding ascii
(Get-FileHash -LiteralPath (Join-Path $package 'Velqora.exe')).Hash.ToLowerInvariant() | Set-Content -LiteralPath (Join-Path $package 'Velqora.sha256') -Encoding ascii
# Promote only a complete package. Preserve every previous file, including legacy local data.
$release = Assert-ProjectPath (Join-Path $root 'publish\Release')
$package = Assert-ProjectPath $package
if (Test-Path -LiteralPath $release) {
    $archive = Assert-ProjectPath (Join-Path $root ('publish\Antigo\Release-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)))
    New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
    Move-Item -LiteralPath $release -Destination $archive
    Write-Host "Release anterior preservada: $archive"
}
New-Item -ItemType Directory -Path (Split-Path -Parent $release) -Force | Out-Null
Move-Item -LiteralPath $package -Destination $release
if ($env:GITHUB_OUTPUT) { "release_dir=$release" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
Write-Host "Distribuição oficial: $release"
Write-Host $status
