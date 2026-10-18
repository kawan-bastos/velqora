$ErrorActionPreference = "Stop"

$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectDirectory "MacroDoButler.csproj"
$destination = Join-Path $projectDirectory "dist\portable"

dotnet build $projectFile --configuration Release -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar Velqora em Release.' }

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item `
    -LiteralPath (Join-Path $projectDirectory "bin\Release\net48\Velqora.exe") `
    -Destination (Join-Path $destination "Velqora.exe") `
    -Force

$configuration = Join-Path $projectDirectory "bin\Release\net48\Velqora.exe.config"
if (Test-Path -LiteralPath $configuration) {
    Copy-Item `
        -LiteralPath $configuration `
        -Destination (Join-Path $destination "Velqora.exe.config") `
        -Force
}
else { throw 'Arquivo Velqora.exe.config não foi gerado; distribuição cancelada.' }

Write-Host "Executável criado em: $destination\Velqora.exe"
