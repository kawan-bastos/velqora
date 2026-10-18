# Velqora

Aplicativo desktop para Windows, feito em WPF com .NET Framework 4.8. O macro aplica movimentos relativos do mouse usando perfis configuráveis e atalhos globais.

Versão atual: **3.3.0**. Veja o [mapa das pastas](docs/PROJECT-STRUCTURE.md), o [índice da documentação](docs/README.md) e as [notas da versão](docs/releases/v3.3.0.md).

## Começar

Na janela principal, escolha um jogo e um preset. Os controles exibem os valores do perfil ativo. Clique em **Salvar** para criar ou atualizar o preset; **Excluir** remove o perfil selecionado após confirmação.

Os perfis mantêm valores separados para arma primária e secundária. Use o botão de alternância ou `F2` para escolher qual conjunto está ativo. Os ícones `?` ao lado dos controles mostram explicações ao passar o mouse.

## Configurações

Abra **Configurações** na barra lateral. A página reúne atalhos globais, aparência, dados e segurança, atualizações e informações do aplicativo. Em **Dados e segurança**, o botão **Abrir local do arquivo** abre a pasta dos presets, atalhos e backups no Explorador.

### Intervalo por arma

Cada preset mantém o intervalo das armas primária e secundária. O intervalo aceito é de 2 a 50 ms; presets novos começam em 2 ms. Editar um preset permite testar os ajustes antes de salvar.

### Atalhos globais

| Padrão | Ação |
|---|---|
| `F1` | Ligar ou desligar o macro |
| `F2` | Alternar entre arma primária e secundária |
| `F12` | Fechar o aplicativo |
| Desativado | Mostrar ou minimizar a janela |

Os atalhos continuam ativos quando a janela está minimizada ou outro aplicativo está em primeiro plano; não dependem do Logitech G HUB. Em **Atalhos**, clique em **Alterar**, pressione a combinação desejada e confirme com **Enter**. `Esc` cancela a captura. `Backspace` ou `Delete` desativa o atalho opcional de mostrar a janela. Os três atalhos principais são obrigatórios. Se uma combinação já estiver em uso por outra ação, o aplicativo oferece substituir o atalho anterior; também avisa quando o Windows não consegue registrar uma combinação.

As preferências ficam em `%LOCALAPPDATA%\Velqora\hotkeys.json`. Falhas de permissão são informadas; o aplicativo não passa a gravar na pasta de instalação. Se um arquivo existente estiver inválido, os padrões são carregados e o arquivo é preservado.

## Ajustes do macro

- **Força vertical:** valor inteiro de 0 a 100; uma unidade equivale a 100 px/s.
- **Correção lateral:** valor inteiro de -500 a 500; uma unidade equivale a 10 px/s; valores negativos movem para a esquerda e positivos para a direita.
- **Exigir botão direito pressionado:** quando ativado, o macro só atua enquanto os botões esquerdo e direito estiverem pressionados.

Os valores das armas e a opção do botão direito pertencem ao preset. Os intervalos também são salvos por arma no preset.

## Presets, importação e backups

O arquivo ativo é `%LOCALAPPDATA%\Velqora\presets.json`. Ele guarda jogos, presets, valores e datas; a gravação usa um arquivo temporário antes de substituir o original. A pasta `backups\` mantém até 10 cópias recentes.

Na primeira inicialização sem `presets.json`, o programa pode migrar `presets.cfg` nos formatos legados de cinco ou sete campos. Os registros migram para o jogo **Não categorizado**, e uma cópia chamada `presets.cfg.migration.bak` é criada antes da conversão. O CFG original é preservado.

**Exportar presets** salva uma cópia completa em JSON. **Importar presets** aceita JSON e CFG legado, valida os registros e permite substituir ou ignorar itens duplicados por jogo e nome. A importação cria um backup antes de aplicar as alterações. **Restaurar backup** valida a cópia selecionada e também salva o estado atual antes de restaurar.

## Compilar no Windows

Requisitos: Windows e Visual Studio Build Tools com suporte ao .NET Framework 4.8, Visual Studio 2022 ou o SDK do .NET com suporte ao projeto.

No PowerShell, na pasta do projeto, execute:

```powershell
.\build.ps1
```

O executável e o arquivo de configuração são copiados para `dist\portable\`. Também é possível compilar diretamente:

```powershell
dotnet build MacroDoButler.csproj -c Release
```

## Atualizações

Para instalações feitas pelo Setup, a seção **Atualizações** verifica, baixa e valida o novo instalador. Após confirmar, o aplicativo fecha e abre o Setup no modo de atualização para concluir a troca no mesmo local. Ao abrir manualmente `Velqora-Setup-Windows-vX.Y.Z.exe` quando o aplicativo já está instalado, o Setup pergunta se deseja desinstalar. **Não** encerra o Setup sem alterações; **Sim** desinstala e encerra, preservando presets e configurações. Para instalar novamente, abra o Setup outra vez. O identificador fixo permite detectar a instalação do mesmo usuário. Downgrade no modo de atualização exige confirmação e é bloqueado no modo silencioso. A interface mostra a versão instalada e oferece o botão para abrir a pasta de dados, sem exibir o caminho completo na página.

A versão portátil mantém o atualizador de executável, com download condicionado a SHA-256 válido. Assets `Velqora.exe` e `Velqora.sha256` continuam sendo gerados por compatibilidade. Uma instalação feita pelo Setup baixa `Velqora-Setup-Windows-vX.Y.Z.exe`, confere a entrada correspondente em `SHA256SUMS.txt` e executa o instalador somente depois de fechar o aplicativo. A opção de abrir após a atualização é oferecida no fim do Setup. A consulta automática verifica novidades, mas não instala nem ativa o macro.

Antes de substituir arquivos, o Setup copia executáveis e configurações anteriores para `<pasta de instalação>\previous-versions\<data-hora>`, verificando SHA-256. Essa pasta permanece até remoção manual, inclusive após desinstalação. Se a instalação falhar, consulte a mensagem do Setup e reinstale o pacote anterior. Para recuperação manual, feche o aplicativo e copie o executável e seu `.config` do mesmo backup para a pasta de instalação. Não restaure os dados pessoais automaticamente: versões antigas podem não entender formatos novos. Não há confirmação automática de saúde após a primeira abertura.

## Estrutura do projeto

- `src/UI/MainWindow.xaml` e `src/UI/MainWindow.xaml.cs`: interface, presets e integração do macro.
- `src/UI/Themes/Controls.xaml`: estilos e controles visuais do tema.
- `src/Models/Preset.cs`: modelo dos presets.
- `src/Services/PresetService.cs`: leitura, migração e gravação dos presets.
- `src/Services/PresetOrganizationService.cs`: organização e validação dos presets.
- `src/Services/PresetBackupService.cs` e `PresetTransferService.cs`: backups e importação/exportação.
- `src/Services/HotkeyService.cs`, `HotkeyParser.cs` e `HotkeyStorageService.cs`: registro, validação e persistência dos atalhos globais.
- `src/Services/MacroSettingsStorageService.cs`: persistência do intervalo geral.
- `src/Services/UpdateService.cs`, `UpdateDownloader.cs`, `UpdateInstaller.cs` e `UpdateBackupService.cs`: consulta, download validado, substituição com rollback e cópia de segurança.


## Instalar e desinstalar

O instalador usa **Inno Setup 6.7.3**: permite distribuição Win32 tradicional, hooks/entrada nativa, diretório selecionável, atualização e desinstalação sem exigir o modelo de pacote MSIX. WiX/MSI seria útil para implantação corporativa, mas acrescentaria complexidade desnecessária aqui. Usamos instalação **por usuário** para que o instalador e os dados pertençam à mesma conta; instalação para todos os usuários não foi habilitada.

Requisitos de execução: Windows 10/11 e .NET Framework 4.8 ou superior. O Setup funciona offline; se o Framework faltar, mostra uma mensagem e encerra sem baixar ou executar dependências. Use o instalador offline oficial da Microsoft para preparar essas máquinas.

1. Baixe `Velqora-Setup-Windows-vX.Y.Z.exe` nas releases oficiais e confira `SHA256SUMS.txt`.
2. Escolha o destino (padrão `%LOCALAPPDATA%\Programs\Velqora`) e, opcionalmente, o atalho na Área de Trabalho. O Menu Iniciar recebe um atalho.
3. Responda se já utilizou o Velqora anteriormente. Se responder **Sim**, escolha se quer importar presets agora. Ao responder **Sim** novamente, clique em **Procurar**, selecione um arquivo `presets.json` ou `presets.cfg` e clique em **Abrir**. Qualquer resposta **Não** continua a instalação normalmente. Na primeira abertura, o aplicativo importa o arquivo escolhido, mantém presets de mesmo nome já existentes e preserva o arquivo original.
4. Ao terminar, a opção **Abrir o Velqora** vem desmarcada. Se marcá-la, o Setup abre o aplicativo pelo Shell do Windows para que o UAC possa solicitar a elevação necessária. O macro inicia desligado; F1 ativa/desativa, F2 alterna a arma e F12 fecha. Logitech G HUB não é necessário.

Para remover o aplicativo, use **Configurações do Windows → Aplicativos → Velqora → Desinstalar**. Os dados pessoais são mantidos. Uma caixa **desmarcada por padrão** permite solicitar sua remoção; uma segunda confirmação mostra `%LOCALAPPDATA%\Velqora`. Cancelar essa confirmação mantém os dados. Desinstalação silenciosa sempre preserva os dados. Links/junções nesse diretório impedem a exclusão automática. Nenhuma cópia na pasta antiga é apagada.

O executável principal usa manifesto `requireAdministrator`: ao abrir o Velqora pelo instalador, Menu Iniciar, Área de Trabalho ou diretamente, o Windows solicita elevação pelo UAC. É necessário confirmar a solicitação para continuar; cancelá-la encerra a abertura. Isso evita exigir que a pessoa escolha manualmente **Executar como administrador** no menu de contexto. O aplicativo não tenta contornar o UAC nem relança em loop. Usar credenciais de outra conta também muda a pasta de dados. O instalador continua sendo instalado no escopo do usuário e não altera UAC, Defender, SmartScreen ou políticas.

## Dados e transição do nome antigo

Os dados ativos ficam em `%LOCALAPPDATA%\Velqora`: `presets.json`, `presets.cfg`, `presets.cfg.bak`, `hotkeys.json`, `settings.json` (quando existente), `macro-settings.json`, `update-preferences.json` e `backups\`. Jogos, categorias e organização continuam dentro dos arquivos copiados. Não são enviados ao GitHub.

Na primeira abertura, `UserDataService` procura preferências em `%LOCALAPPDATA%\MacroDoButler` e arquivos junto do executável. Instalações anteriores que já gravaram `legacy-data-source.txt` continuam compatíveis. Cria snapshots em `migration-backups\`, verifica a cópia por SHA-256 e só então copia os arquivos ausentes para o novo local. Destinos existentes prevalecem; conflitos permanecem recuperáveis nos snapshots. Só conclui a inicialização após toda a migração; uma falha impede abrir com dados parcialmente migrados e pode ser repetida. `.migration-complete` evita reimportação e duplicação nas próximas aberturas. Os originais nunca são removidos. A conversão de CFG para JSON mantém o CFG e sua cópia anterior. O arquivo escolhido no Setup é copiado para `pending-imports\`, importado na abertura seguinte e removido dessa área somente após sucesso.

Se já abriu a versão nova e depois encontrou outra pasta antiga, use **Importar presets** ou transfira os arquivos manualmente com o programa fechado; o marcador impede reimportar silenciosamente uma fonte diferente. Para reinstalar, basta manter essa pasta de dados. Para removê-la manualmente, feche o aplicativo, use **Configurações → Dados e segurança → Abrir local do arquivo**, faça uma cópia se desejar e exclua **somente** `%LOCALAPPDATA%\Velqora`. Isso também remove snapshots e backups internos, mas não os originais da instalação antiga.

`Velqora.exe` é o nome principal. O namespace `MacroDoButler`, o nome do projeto, a URL do repositório e o mutex antigo permanecem por compatibilidade. Ao instalar na mesma pasta que contenha `Macro do Butler.exe` ou `Macro.do.Butler.exe`, o Setup preserva uma cópia anterior e atualiza também esse nome com o novo binário: atalhos externos existentes continuam abrindo o Velqora. Não procura nem modifica atalhos arbitrários em outros diretórios. Se escolher outro destino, a versão portátil antiga permanecerá lá; atualize seus atalhos e evite alternar entre versões. Instaladores antigos de terceiros, com outro identificador, não são removidos automaticamente.

## Distribuição comercial e repositório público

O instalador e a interface do cliente não mostram link para o repositório. O atualizador, porém, consulta atualmente a API pública e baixa arquivos das releases desse repositório; o endereço continua recuperável do executável e do tráfego de rede. Retirar textos da interface não o torna secreto. Enquanto o repositório for público, qualquer pessoa poderá ver o código e baixar os arquivos publicados. Torná-lo privado no futuro não elimina cópias, clones ou forks já feitos. Para uma distribuição privada de verdade, planeje um serviço de atualizações e arquivos de download sob seu controle, sem credenciais embutidas no aplicativo.

O projeto usa a licença MIT atual. Ela permite que terceiros usem, copiem e redistribuam o código sob as condições da própria licença. Vender instalação, suporte ou conveniência é possível, mas o repositório público não pode funcionar como barreira contra cópias gratuitas. Revise a estratégia de licença e as contribuições antes de publicar uma edição comercial fechada; mudar a visibilidade ou a licença depois não revoga automaticamente as cópias já distribuídas.

## Gerar a distribuição

A versão central é `Directory.Build.props` (atualmente 3.3.0). Ela alimenta propriedades do executável, UI, instalador, nomes dos artefatos e notas automáticas. A tag deve corresponder exatamente a `vMAJOR.MINOR.PATCH`.

```powershell
# Baixa o compilador oficial, confere SHA-256 e assinatura antes de executá-lo:
.\scripts\Get-InnoSetup.ps1
.\scripts\build-installer.ps1 -Iscc '.tools\Inno Setup 6\ISCC.exe'
# Ou use um ISCC 6.7.3 já instalado:
.\scripts\build-installer.ps1 -Iscc 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

A pasta oficial é **`publish/Release/`**. Ela contém o Setup 3.3.0, executáveis, configurações, licença, notas da versão e checksums. Cada build completo arquiva a release anterior em `publish/Antigo/Release-<data>-<id>/`, preservando todos os arquivos antigos. Os arquivos intermediários ficam em `dist/staging/`; o build portátil simples fica em `dist/portable/`.

Somente `publish/Release/` deve ser usada para distribuir a versão atual. O pacote usa uma lista explícita de arquivos e não inclui presets, backups, logs, PDBs ou certificados. Os binários e históricos locais não são enviados ao Git. O build local gera o pacote; somente o push de uma tag de versão dispara a publicação no GitHub.

O processo é repetível por script; o compilador Inno está fixado e validado. Não se promete identidade binária de instaladores produzidos em horários/ambientes diferentes, especialmente após timestamp de assinatura. A preparação requer internet; a instalação final não requer download.

O workflow compila em Release, testa migração/instalação, assina quando configurado, calcula hashes finais e preserva o artefato `Velqora-Windows`. Push de uma tag correspondente publica uma release; builds de branch/PR não publicam. Releases existentes não são sobrescritas. Criar/enviar a tag é uma ação de publicação e exige autorização do responsável. O envio de commits para `main` valida e empacota o projeto, sem publicar uma release. Tags de versão são uma ação separada de publicação.

## Assinatura digital

Sem certificado configurado, o build é permitido e `SIGNING-STATUS.txt` informa **NÃO ASSINADO**. Os arquivos gerados nesta implementação não têm assinatura de editor. Checksum verifica integridade em relação à lista publicada; não substitui a autenticação do editor.

Com certificado legítimo já disponível no repositório `CurrentUser\My` do Windows, e SignTool instalado pelo Windows SDK:

```powershell
$env:VELQORA_SIGN_THUMBPRINT = '<thumbprint do seu certificado legítimo>'
# Opcional se o SDK não estiver no caminho padrão:
$env:VELQORA_SIGNTOOL = '<caminho completo do signtool.exe>'
.\scripts\build-installer.ps1 -Iscc '.tools\Inno Setup 6\ISCC.exe' -Sign
```

O script assina primeiro o executável, empacota esse binário e depois assina o instalador, usando SHA-256 e timestamp RFC 3161 da DigiCert. Executa `signtool verify /pa /all /tw` e Authenticode; qualquer falha interrompe o build. A senha/chave privada não é passada na linha de comando. O thumbprint não é um segredo. O processo não gera certificados.

No GitHub, configure **Settings → Secrets and variables → Actions** com `VELQORA_PFX_BASE64` e `VELQORA_PFX_PASSWORD`, somente se sua autoridade certificadora permitir esse tipo de exportação/uso. O workflow lê ambos por variáveis de ambiente apenas em tags, importa o certificado em memória para o armazenamento do runner e remove a chave ao terminar. Um secret faltante com o outro presente causa falha, sem voltar silenciosamente para um build não assinado. Não salve valores no YAML, em arquivos, comentários ou logs. Restrinja quem pode alterar workflows e criar tags. Certificados em token/HSM ou serviços de assinatura exigem o provider oficial e runner apropriado; não exporte chaves contra as regras do emissor. O script local aceita o certificado via armazenamento Windows/provider compatível com SignTool.

Para conferir os arquivos recebidos:

```powershell
Get-FileHash .\Velqora-Setup-Windows-v3.3.0.exe -Algorithm SHA256
Get-AuthenticodeSignature .\Velqora-Setup-Windows-v3.3.0.exe
signtool verify /pa /all /tw .\Velqora.exe
signtool verify /pa /all /tw .\Velqora-Setup-Windows-v3.3.0.exe
```

Confira também a guia **Assinaturas Digitais** nas propriedades. Assinatura válida não garante que o SmartScreen deixe de avisar imediatamente: reputação e políticas do Windows também influenciam. Não desative proteções para instalar.

Referências técnicas: [Inno Setup e distribuição oficial](https://jrsoftware.org/isdl.php), [modo de instalação sem administrador](https://jrsoftware.org/ishelp/topic_admininstallmode.htm), [SignTool da Microsoft](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool).

## Validação

Consulte [docs/VALIDATION.md](docs/VALIDATION.md) para resultados executados e casos que precisam de validação interativa/VM.


### Trajetórias avançadas

A seção **Avançada** permite editar uma trajetória temporal por preset e por arma, com prévia animada interna, interpolação e fase final explícita. Os presets básicos continuam compatíveis. Consulte o [guia de configuração, arquitetura e testes](docs/advanced-trajectory.md).

### Migração para 3.3

Na primeira abertura, presets da 3.2 são convertidos automaticamente para as unidades inteiras. A força vertical usa 100 px/s por unidade; a lateral usa 10 px/s. O arredondamento pode ajustar ligeiramente a força. Nomes, jogos, armas, pontos e tempos são preservados. Um backup completo fica em `migration-backups/`, e `forceUnitsVersion: 1` registra a conversão para não repeti-la. Importações e restaurações também seguem a regra de inteiros. Detalhes e testes estão no [guia de trajetórias](docs/advanced-trajectory.md).
