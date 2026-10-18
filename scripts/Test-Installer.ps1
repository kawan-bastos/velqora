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
& $Iscc /DSmokeTest "/DSmokePresetFile=$presetFixture" "/DAppVersion=$version" "/DPayload=$Payload" "/DOutput=$target\package" (Join-Path $root 'installer\Velqora.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar fixture com identidade isolada.' }
$Installer = Join-Path $target "package\Velqora-Setup-Windows-v$version.exe"
function Install-Test([switch]$Update) {
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=desktopicon', ('/DIR="' + $target + '"'), ('/LOG="' + $target + '\setup.log"'))
    if ($Update) { $arguments += '/UPDATE' }
    Write-Host "START: Instalação $(if ($Update) { 'de atualização' } else { 'limpa' })"
    $process = Start-Process -FilePath $Installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Write-Host "DONE: instalação (exit code $($process.ExitCode))"
    if ($process.ExitCode -ne 0) { throw "Instalador retornou $($process.ExitCode). Consulte $target\setup.log" }
}
function Assert-Test([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message }; Write-Host "PASS: $Message" }
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
