# Organização do projeto

A raiz mantém a entrada WPF e os arquivos necessários para compilar. Código, documentação, testes e distribuição ficam em pastas próprias.

| Pasta ou arquivo | Responsabilidade |
|---|---|
| `App.xaml` / `App.xaml.cs` | Recursos globais, instância única e inicialização |
| `MacroDoButler.csproj` | Projeto WPF; nome legado mantido por compatibilidade |
| `Directory.Build.props` | Versão única do aplicativo e do instalador |
| `src/UI/` | Janela principal, navegação, presets e editor avançado |
| `src/UI/Themes/Controls.xaml` | Tema e estilos compartilhados |
| `src/Models/` | Contratos dos presets, forças, trajetórias e configurações |
| `src/Services/` | Persistência, migrações, backups, entrada, atalhos e atualização |
| `src/Native/` | Chamadas nativas do Windows |
| `src/Properties/` | Referência de metadados legados; os atuais são gerados pelo SDK |
| `assets/` | Ícones, imagens e sons incorporados |
| `tests/` | Testes C# com dados sintéticos, incluindo o atualizador |
| `scripts/` | Build, empacotamento, assinatura e verificações WPF/instalador |
| `installer/Velqora.iss` | Setup e desinstalador do Windows |
| `docs/` | Guias, validação e notas das versões |
| `.github/workflows/` | Validação e empacotamento no GitHub Actions |

## Pastas geradas localmente

| Pasta | Conteúdo |
|---|---|
| `publish/Release/` | **Pacote oficial atual**: Setup, executáveis, licença, notas e hashes |
| `publish/Antigo/` | Releases anteriores, preservadas antes de substituir a oficial |
| `dist/portable/` | Executável e configuração produzidos por `build.ps1` |
| `dist/staging/` | Payloads e preparação de pacotes; não distribuir |
| `dist/archive/` | Saídas antigas e dados legados que já estavam misturados em `dist` |
| `build-check/` | Builds de teste, capturas, fixtures e cópias de recuperação; não distribuir |
| `bin/` / `obj/` | Arquivos intermediários gerados pelo .NET |
| `.tools/` | Ferramentas locais verificadas, como o compilador Inno Setup |

Essas pastas são ignoradas pelo Git. Os testes não dependem de código guardado em `build-check`: todas as fontes necessárias estão em `src`, `tests` e `scripts`. Builds antigos e cópias de recuperação foram preservados; não remova pastas de recuperação sem conferir seu conteúdo.

## Onde alterar cada funcionalidade

- Janela e configurações: `src/UI/MainWindow.xaml` e seu `.xaml.cs`.
- Presets básicos: `src/UI/PresetEditor.cs`, `src/Models/PresetEditDraft.cs`.
- Editor avançado e desenho: `src/UI/TrajectoryEditor.*`, `TrajectoryDrawingEditor.*`.
- Regras de força e arredondamento: `src/Models/PresetForceUnits.cs`.
- Conversão automática dos saves: `src/Services/PresetForceMigration.cs`.
- Arquivo de presets: `src/Services/PresetService.cs`.
- Empacotamento: `scripts/build-installer.ps1` e `installer/Velqora.iss`.
- Notas do release: `docs/releases/v<versão>.md`.

Os dados reais do usuário ficam fora do projeto, em `%LOCALAPPDATA%/Velqora`. Use **Configurações → Dados e segurança → Abrir local do arquivo** para acessá-los.
