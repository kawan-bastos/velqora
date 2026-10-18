[CmdletBinding()]
param([Parameter(Mandatory)][string]$Iscc, [string]$Payload)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Payload) { $Payload = Join-Path $root 'publish\Release' }
$Payload = (Resolve-Path -LiteralPath $Payload).Path
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{12F2AD76-5E8D-409F-B24B-974879DD2EF4}_is1'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Velqora Installer Test.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Velqora Installer Test.lnk'
$smokeData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Velqora Installer Test'
$sibling = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Velqora Installer Test sibling'
if ((Test-Path $registry) -or (Test-Path -LiteralPath $shortcut) -or (Test-Path -LiteralPath $desktop) -or (Test-Path -LiteralPath $smokeData) -or (Test-Path -LiteralPath $sibling)) { throw 'Teste requer usuário sem instalação/atalhos/dados Velqora Installer Test existentes; use uma VM limpa.' }
$target = Join-Path $root ('build-check\Installer smoke ' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $target | Out-Null
$presetFixture = Join-Path $target 'presets.cfg'
[System.IO.File]::WriteAllText($presetFixture, '')
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$canonicalSetup = Join-Path $Payload "Velqora-Setup-Windows-v$version.exe"
$compatibleSetup = Join-Path $Payload "Velqora-Setup-v$version.exe"
if ((Get-FileHash -LiteralPath $canonicalSetup -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $compatibleSetup -Algorithm SHA256).Hash) { throw 'O setup compatível com 3.2 precisa ser idêntico ao setup oficial.' }
$setupChecksums = [IO.File]::ReadAllText((Join-Path $Payload 'SHA256SUMS.txt'))
foreach ($setupName in @("Velqora-Setup-Windows-v$version.exe","Velqora-Setup-v$version.exe")) {
    $entry = '(?m)^' + (Get-FileHash -LiteralPath $canonicalSetup -Algorithm SHA256).Hash + '  ' + [regex]::Escape($setupName) + '\r?$'
    if ($setupChecksums -notmatch $entry) { throw "Checksum do setup ausente ou incorreto: $setupName" }
}
& $Iscc /DSmokeTest "/DSmokePresetFile=$presetFixture" "/DAppVersion=$version" "/DPayload=$Payload" "/DOutput=$target\package" (Join-Path $root 'installer\Velqora.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar fixture com identidade isolada.' }
$Installer = Join-Path $target "package\Velqora-Setup-Windows-v$version.exe"
# An older package uses the same test-only identity and must never reach file installation.
$olderVersion='3.2.0'
& $Iscc /Q /DSmokeTest "/DSmokePresetFile=$presetFixture" "/DAppVersion=$olderVersion" "/DPayload=$Payload" "/DOutput=$target\downgrade-package" (Join-Path $root 'installer\Velqora.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar fixture de downgrade.' }
$OlderInstaller = Join-Path $target "downgrade-package\Velqora-Setup-Windows-v$olderVersion.exe"

function Install-Test([switch]$Update) {
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=desktopicon', ('/DIR="' + $target + '"'), ('/LOG="' + $target + '\setup.log"'))
    if ($Update) { $arguments += '/UPDATE' }
    Write-Host "START: Instalação $(if ($Update) { 'de atualização' } else { 'limpa' })"
    $process = Start-Process -FilePath $Installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Write-Host "DONE: instalação (exit code $($process.ExitCode))"
    if ($process.ExitCode -ne 0) { throw "Instalador retornou $($process.ExitCode). Consulte $target\setup.log" }
}
function Assert-Test([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message }; Write-Host "PASS: $Message" }
function Assert-DowngradeBlocked([string]$InstallPath,[string]$Scenario,[switch]$Update) {
    $beforeFiles=@{}
    foreach($name in @('Velqora.exe','presets.json','presets.cfg','hotkeys.json')) {
        $path=Join-Path $InstallPath $name
        if(Test-Path -LiteralPath $path) { $beforeFiles[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    $wasRegistered=Test-Path $registry
    $downgradeLog=Join-Path $target ("downgrade-$Scenario.log")
    $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$InstallPath+'"'),('/LOG="'+$downgradeLog+'"'))
    if($Update) { $arguments+='/UPDATE' }
    $process=Start-Process -FilePath $OlderInstaller -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Assert-Test ($process.ExitCode -ne 0) "Downgrade silencioso bloqueado: $Scenario"
    Assert-Test ((Get-Content -LiteralPath $downgradeLog -Raw) -match 'Downgrade blocked:') "Incompatibilidade registrada no log: $Scenario"
    foreach($name in $beforeFiles.Keys) {
        Assert-Test ((Test-Path -LiteralPath (Join-Path $InstallPath $name)) -and ((Get-FileHash -LiteralPath (Join-Path $InstallPath $name) -Algorithm SHA256).Hash -eq $beforeFiles[$name])) "Downgrade preserva $name ($Scenario)"
    }
    Assert-Test ((Test-Path $registry) -eq $wasRegistered) "Registro da instalação preservado: $Scenario"
}
try {
    Install-Test
    Assert-Test (Test-Path -LiteralPath "$target\Velqora.exe") 'Instalação limpa em caminho com espaços'
    $stagedPreset = Join-Path $smokeData ('pending-imports\' + (Get-FileHash -LiteralPath $presetFixture -Algorithm SHA256).Hash.ToLowerInvariant() + '.cfg')
    Assert-Test (Test-Path -LiteralPath $stagedPreset) 'Arquivo de presets escolhido preparado para a primeira abertura'
    Assert-Test ((Get-FileHash -LiteralPath $stagedPreset -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $presetFixture -Algorithm SHA256).Hash) 'Arquivo de presets copiado sem alteração'
    Assert-Test (Test-Path $registry) 'Entrada em Aplicativos instalados'
    Assert-Test ((Get-ItemProperty -Path $registry).DisplayName -eq 'Velqora Installer Test') 'Nome do aplicativo sem versão duplicada'
    Assert-Test ((Get-ItemProperty -Path $registry).DisplayVersion -eq $version) 'Versão mantida no campo próprio'
    $registeredUninstaller = (Get-ItemProperty -Path $registry).UninstallString.Trim('"')
    Assert-Test (Test-Path -LiteralPath $registeredUninstaller) 'Desinstalador registrado e acessível'
    Set-Content -LiteralPath (Join-Path $target 'presets.json') -Value '{"formatVersion":3,"presets":[]}'
    Assert-DowngradeBlocked $target 'installed'
    Assert-DowngradeBlocked $target 'update' -Update
    Move-Item -LiteralPath (Join-Path $target 'Velqora.exe') -Destination (Join-Path $target 'Velqora.exe.test-preserved')
    try { Assert-DowngradeBlocked $target 'registered-version-fallback' -Update }
    finally { Move-Item -LiteralPath (Join-Path $target 'Velqora.exe.test-preserved') -Destination (Join-Path $target 'Velqora.exe') }

    Write-Host 'START: Bloqueio de segunda instalação'
    $repeat = Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART', ('/DIR="' + $target + '"'), ('/LOG="' + $target + '\repeat.log"')) -WindowStyle Hidden -Wait -PassThru
    Write-Host "DONE: bloqueio (exit code $($repeat.ExitCode))"
    Assert-Test ($repeat.ExitCode -ne 0) 'Segunda execução silenciosa bloqueada sem autorização de atualização'
    Assert-Test ((Test-Path $registry) -and (Test-Path -LiteralPath "$target\Velqora.exe")) 'Instalação existente preservada após bloqueio'
    Write-Host 'START: Desinstalação iniciada pelo Setup'
    $remove = Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART', ('/LOG="' + $target + '\smoke-uninstall.log"'), '/SMOKEUNINSTALL') -WindowStyle Hidden -Wait -PassThru
    Write-Host "DONE: desinstalação iniciada pelo Setup (exit code $($remove.ExitCode))"
    Assert-Test (($remove.ExitCode -ne 0) -and -not (Test-Path $registry)) 'Resposta Sim desinstala e encerra o Setup sem reinstalar'
    Assert-Test (Test-Path -LiteralPath $stagedPreset) 'Desinstalação preserva presets pendentes'
    Install-Test
    $shell = New-Object -ComObject WScript.Shell
    Assert-Test ($shell.CreateShortcut($shortcut).TargetPath -eq "$target\Velqora.exe") 'Atalho no Menu Iniciar'
    Assert-Test ($shell.CreateShortcut($desktop).TargetPath -eq "$target\Velqora.exe") 'Atalho opcional na Área de Trabalho'
    $names = @('presets.cfg','presets.cfg.bak','presets.json','hotkeys.json','settings.json')
    foreach ($name in $names) { Set-Content -LiteralPath (Join-Path $target $name) -Value "synthetic $name" }
    New-Item -ItemType Directory -Path "$target\backups" | Out-Null
    Set-Content -LiteralPath "$target\backups\synthetic.bak" -Value 'synthetic backup'
    Copy-Item -LiteralPath "$target\Velqora.exe" -Destination "$target\Macro do Butler.exe"
    $before = @{}; foreach ($name in $names) { $before[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name)).Hash }
    Install-Test -Update
    foreach ($name in $names) { Assert-Test ((Get-FileHash -LiteralPath (Join-Path $target $name)).Hash -eq $before[$name]) "Reinstalação preserva $name" }
    Assert-Test ((Get-FileHash -LiteralPath "$target\Velqora.exe").Hash -eq (Get-FileHash -LiteralPath "$target\Macro do Butler.exe").Hash) 'Nome legado recebe executável compatível'
    Assert-Test (@(Get-ChildItem -LiteralPath "$target\previous-versions" -Filter Velqora.exe -Recurse).Count -gt 0) 'Cópia anterior preservada para recuperação'
} finally {
    if (Test-Path -LiteralPath "$target\unins000.exe") {
        Write-Host 'START: Limpeza da instalação de teste'
        $uninstall = Start-Process -FilePath "$target\unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART', ('/LOG="' + $target + '\cleanup.log"')) -WindowStyle Hidden -Wait -PassThru
        Write-Host "DONE: limpeza (exit code $($uninstall.ExitCode))"
        if ($uninstall.ExitCode -ne 0) { throw "Falha ao desinstalar fixture: $target" }
    }
}
foreach ($name in $names) { Assert-Test ((Get-FileHash -LiteralPath (Join-Path $target $name)).Hash -eq $before[$name]) "Desinstalação preserva $name" }
Assert-Test (Test-Path -LiteralPath "$target\backups\synthetic.bak") 'Backups pessoais preservados'
Assert-Test (-not (Test-Path $registry)) 'Entrada do Windows removida'
Assert-Test (-not (Test-Path -LiteralPath $shortcut)) 'Atalho do Menu Iniciar removido'
Assert-Test (-not (Test-Path -LiteralPath $desktop)) 'Atalho da Área de Trabalho removido'
Write-Host "Fixtures preservadas para inspeção: $target"
Install-Test -Update
New-Item -ItemType Directory -Path $smokeData -Force | Out-Null
New-Item -ItemType Directory -Path $sibling -Force | Out-Null
Set-Content -LiteralPath (Join-Path $smokeData '.smoke-delete-opt-in') -Value 'synthetic opt-in'
Set-Content -LiteralPath (Join-Path $smokeData 'presets.json') -Value 'synthetic user data'
Set-Content -LiteralPath (Join-Path $sibling 'sentinel.txt') -Value 'must survive'
Write-Host 'START: Remoção explícita dos dados de teste'
$uninstall = Start-Process -FilePath "$target\unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART', ('/LOG="' + $target + '\remove-data.log"')) -WindowStyle Hidden -Wait -PassThru
Write-Host "DONE: remoção explícita (exit code $($uninstall.ExitCode))"
if ($uninstall.ExitCode -ne 0) { throw 'Falha no teste de remoção explícita.' }
Assert-Test (-not (Test-Path -LiteralPath $smokeData)) 'Remoção explícita atinge somente diretório de dados do teste'
Assert-Test (Test-Path -LiteralPath (Join-Path $sibling 'sentinel.txt')) 'Diretório vizinho preservado'
Remove-Item -LiteralPath (Join-Path $sibling 'sentinel.txt')
Remove-Item -LiteralPath $sibling

# A portable executable is detected even without a registered installation.
$portable=Join-Path $target 'portable-newer'
New-Item -ItemType Directory -Path $portable | Out-Null
Copy-Item -LiteralPath (Join-Path $Payload 'Velqora.exe') -Destination (Join-Path $portable 'Velqora.exe')
Set-Content -LiteralPath (Join-Path $portable 'presets.json') -Value '{"formatVersion":3,"presets":[]}'
Assert-DowngradeBlocked $portable 'portable' -Update
Assert-Test (!(Test-Path -LiteralPath (Join-Path $portable 'previous-versions'))) 'Downgrade interrompido antes de copiar ou substituir arquivos'
