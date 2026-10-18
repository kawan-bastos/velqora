# Validação do Velqora 4.0.1

Verificações da correção da logo em 18/10/2026:

- Build Release com 0 avisos e 0 erros; produto 4.0.1 e arquivo 4.0.1.0.
- O teste reproduziu o carregamento vazio usando os caminhos relativos da versão 4.0.0.
- Após a correção, 319 verificações WPF passaram, incluindo resolução do recurso a partir de `src/UI/MainWindow.xaml`, dimensões das imagens e presença dos pixels dourados na renderização das logos da barra lateral e da seção Sobre.
- Imagens renderizadas conferidas em `build-check/branding-logo-0.png` e `build-check/branding-logo-1.png`.
- Setup gerado em `publish/Release/`, com alias compatível com o atualizador 3.2 e checksums SHA-256.
- Testes do instalador passaram com identidade isolada: instalação, atualização, desinstalação, bloqueio de downgrade e preservação dos dados sintéticos.
- Atualizador compilado na versão 4.0.1: 32 verificações passaram; SHA-256 dos 9 arquivos listados no pacote conferido.

## Histórico: validação da 4.0.0

Verificações executadas em Windows em 18/10/2026. Os testes usam dados sintéticos e uma identidade isolada para o instalador. A geração do pacote não instala nem modifica o aplicativo real.

| Verificação | Resultado |
|---|---|
| Build WPF após reorganizar janela e tema | Release normal e cópia limpa; 0 avisos e 0 erros |
| Trajetórias e persistência | 170 verificações, incluindo backups distintos no mesmo milissegundo, preservação dos bytes, restauração e retenção |
| Conversão automática de força 3.2 → 4.0 | 92 verificações: backup exato, dados básicos/avançados, tempos, flags, jogos vazios, retry, corrupção, campos desconhecidos e idempotência |
| Edição temporária de presets | 28 verificações |
| Migração de diretórios e canal de atualização | 30 verificações |
| Atalhos globais | 8 verificações |
| Atualizador HTTP local e rollback | 32 verificações, incluindo seleção dos nomes atual e legado do setup, preferência pelo pacote oficial e canais instalado/portátil |
| Interface WPF e tooltips | 312 verificações; renderização em 1100 e 885 px |
| Setup com identidade de teste | Instalação limpa, atualização, duas formas de desinstalar, atalhos, backups e isolamento dos dados passaram |
| Downgrade em setups novos | Bloqueio silencioso testado em instalação registrada, `/UPDATE`, versão recuperada do registro e pasta portátil; executável e presets preservados |
| Nome em Aplicativos instalados | Sem versão no nome; `DisplayVersion` mantém 4.0.0 |
| Pacote oficial | `publish/Release/`; checksums SHA-256 conferidos; sem presets, preferências, PDBs ou certificados |
| Compatibilidade com atualizador 3.2 | Alias `Velqora-Setup-v4.0.0.exe` idêntico ao setup oficial; ambos incluídos em `SHA256SUMS.txt` |
| Propriedades de versão | Produto 4.0.0; arquivo 4.0.0.0 |
| Assinatura digital | Não assinado; situação registrada no pacote |

Os testes do atualizador usam apenas um servidor em 127.0.0.1. A suíte do instalador registra temporariamente **Velqora Installer Test**, preserva a instalação real e remove sua própria entrada e atalhos ao terminar. Fixtures e logs ficam em `build-check/` para inspeção.

O movimento físico no jogo, o aceite interativo do UAC, combinações de DPI/alto contraste e uma atualização baixada de uma release pública continuam sendo verificações manuais. Não são inferidos dos testes sintéticos.

## Histórico: validação da 3.2.0

Resultados anteriores, preservados para referência.

Executado em Windows, com Inno Setup 6.7.3 verificado por assinatura Authenticode e SHA-256. Os testes automatizados usam arquivos sintéticos e uma identidade de instalador separada da instalação Velqora real. Nenhum preset real foi alterado.

| Verificação | Resultado |
|---|---|
| Build Release com `-warnaserror` no projeto e em cópia limpa | Passou: 0 avisos, 0 erros |
| Instalação do Inno Setup a partir de fonte oficial | Assinatura válida, editor Pyrsys B.V.; hash fixado no script |
| Compilação do Setup a partir do build normal e limpo | Passou |
| Migração, conflitos, retry após arquivo bloqueado e idempotência | 30 verificações passaram; arquivos sintéticos |
| Instalação em caminho com espaços, registro no Windows e dois atalhos | Passou na variante isolada |
| Seleção de arquivo de presets no Setup | Passou na variante isolada: `.cfg` escolhido foi copiado para `pending-imports` com SHA-256 idêntico |
| Segunda execução do Setup | Passou na variante isolada: execução silenciosa comum recusada; resposta Sim de teste desinstalou e encerrou sem reinstalar, preservando dados; modo de atualização reinstalou |
| Reinstalação da mesma versão e transição de `Macro do Butler.exe` | Passou; dados sintéticos e cópia anterior verificadas |
| Desinstalação padrão | Passou; presets, configurações e backups sintéticos preservados; atalhos e registro removidos |
| Remoção explícita de dados | Passou na variante isolada; pasta vizinha preservada |
| Atualizador portátil, hashes, cancelamento e rollback | 24 verificações passaram no harness local existente, adaptado à exigência de SHA-256 |
| Propriedades de versão do executável | Velqora, versão 3.2.0.0 / produto 3.2.0 |
| Assinatura dos artefatos desta entrega | `Get-AuthenticodeSignature`: `NotSigned` para executável e Setup |
| Conteúdo do pacote | `Velqora.exe`, `Velqora.exe.config` e o aviso legal `LICENSE.txt` |

A seleção do pacote Setup na consulta de releases e a execução completa da atualização instalada ainda exigem uma release de teste controlada. O parser de `SHA256SUMS.txt` e a detecção do canal instalado passaram com dados sintéticos. Ainda exigem teste interativo em uma VM Windows com .NET Framework 4.8: navegação visual das perguntas Sim/Não e do seletor de arquivo, importação na primeira abertura com dados reais de versões anteriores, interface de desinstalação e confirmação da remoção, bloqueio por aplicativo aberto, rejeição e aceitação do UAC para controlar outro processo elevado, F1/F2/F12, movimento do mouse, downgrade com confirmação, recuperação manual da versão anterior e aparência final em DPI/alto contraste. Esses resultados não são inferidos dos testes sintéticos.

Assinatura e publicação dependem de um certificado legítimo e dos secrets configurados pelo responsável. O pipeline falha se a assinatura for solicitada e a verificação falhar. Nenhum build assinado ou release pública foi produzido nesta tarefa.

