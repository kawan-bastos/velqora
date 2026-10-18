# Contribuindo

1. Crie um fork do repositório.
2. Crie uma branch para a alteração.
3. Mantenha compatibilidade com presets existentes.
4. Compile e teste no Windows antes de enviar.
5. Abra um pull request explicando a mudança e como ela foi verificada.

Não envie `presets.cfg`, binários compilados, pastas `bin/obj` ou configurações pessoais.

O [mapa das pastas](docs/PROJECT-STRUCTURE.md) indica onde alterar cada funcionalidade. Mantenha código de teste em `tests/`, documentação em `docs/` e saídas geradas fora do controle de versão. A distribuição atual é produzida por `scripts/build-installer.ps1` em `publish/Release/`.
