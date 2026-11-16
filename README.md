# windots-launcher

Lançador e menu de comandos do [windots](https://github.com/higorhgon/windots), feito em WPF (.NET 9) para
Windows 11. O objetivo é funcionar como o menu do omarchy e, no fim, **substituir o Flow Launcher**.

> Estado: **fase 1 pronta** (motor, menu do omarchy, temas e atalhos de teclado). Apps, sistema, arquivos e a
> troca do Flow vêm nas próximas fases.

## O que ele é

- **Menu do omarchy.** O omarchy atual (branch `quattro`, `omacom/omarchy`) não usa mais o Walker: o menu é
  o plugin `omarchy.menu` do shell dele, dirigido por dados em JSONC. Este projeto lê o mesmo formato:
  ids com pontos (`style.theme` fica dentro de `style`), e por item `icon` (glifo Nerd Font),
  `label`, `action`, `target`, `provider` (lista dinâmica), `aliases`, `description`, `when`, `checked` e
  `alt`, além de rotas e voltar um nível. Uma adição do windots: `confirm` (o primeiro Enter pergunta, o
  segundo executa), para reiniciar, desligar e afins.
- **Lançador de apps e arquivos**, para trocar o Flow Launcher (hoje com Program, Explorer, Calculator,
  Shell, Sys, ProcessKiller, BrowserBookmark, Url, WebSearch e PluginsManager). Ainda não existe.
- **Visual do PowerToys Run** (caixa de busca arredondada, lista com ícones, seleção na cor de destaque),
  pintado pela paleta do tema atual do windots (`~\.local\state\windots\theme.name`, paletas em
  `windots\default\themes`, as mesmas do omarchy).

## Como usar

Instalar (compila com `dotnet publish` em `~\.local\share\windots-launcher`; precisa do SDK do .NET 9):

```powershell
windots install launcher
```

Abrir:

| Como | O que abre |
|---|---|
| `Win+Space` | menu raiz (liga/desliga) |
| `Alt+K` | atalhos de teclado (era o fzf) |
| `Alt+Ctrl+Shift+Space` | temas (era o fzf) |
| `windots menu [verbo] [rota]` | qualquer rota: `windots menu summon style.theme` |

Há um processo residente: o primeiro uso inicia o app, os seguintes só mandam o pedido por um pipe, então o
menu abre na hora. `windots-launcher.exe --background` só inicia o processo (é o que o login faz, depois do
`windots refresh autostart`).

Verbos (como o `omarchy menu`): `toggle [rota]` (padrão), `summon [rota]`, `close`, `refresh`, `quit`. Uma
rota é o id de um menu (`style.theme`) ou um apelido (`theme`, `keys`).
`windots-launcher.exe dump <rota> <arquivo>` grava as linhas de uma rota num arquivo, sem abrir janela (para
testes e scripts).

Teclas: `↑`/`↓` (ou `Ctrl+J`/`Ctrl+K`, `Ctrl+N`/`Ctrl+P`) escolhem, `PageUp`/`PageDown` pulam, `Enter`
executa, `Alt+Enter` executa o `alt` do item, `Backspace` com o campo vazio (ou `Alt+Left`) volta um nível,
`Esc` limpa o filtro e depois fecha, o mouse também funciona. Perder o foco fecha a janela.

## O menu

`menus/default.jsonc` é o menu padrão (Style, Keybindings, Windows settings, Setup, System). Para uma máquina
só, crie `~\.config\windots\menu.jsonc` com o mesmo formato: ids novos aparecem, ids que já existem têm só os
campos citados sobrescritos (como a extensão do omarchy). Comentários e vírgula final são aceitos.

- `action`: comando PowerShell, executado como o whkd faz (`pwsh -NoProfile -Command ...`, sem janela, com
  `~\windots\bin` no PATH).
- `when`, `checked`, `disabled`: comandos do `cmd.exe`; código de saída 0 significa "sim".
- `provider`: `themes` (os 23 temas do windots, com as cores, o modo e o ✓ no atual) e `keybindings` (whkd,
  Flow Launcher e AutoHotkey, as 84 linhas que o `windots-menu-keybindings` listava; Enter roda o comando
  do whkd).

## Fases

1. **Pronta.** Motor e menu: janela, JSONC do omarchy, rotas, filtro, executar ação, voltar; menu raiz,
   temas e atalhos de teclado.
2. Apps: Menu Iniciar e apps da Store, ranking por uso.
3. Sistema (`sys`), calculadora, comandos (Shell) e processos.
4. Arquivos, favoritos do navegador, URLs e busca na web.
5. Desinstalar o Flow Launcher e passar o `Alt+Space` para este app.

Até a fase 3 estar pronta o Flow Launcher continua instalado (é mais leve que o PowerToys inteiro).

## Desenvolvimento

```powershell
cd src\WindotsLauncher
dotnet build -c Release          # bin\Release\net9.0-windows\windots-launcher.exe
```

Um launcher aberto trava o executável: `windots-launcher.exe quit` antes de compilar de novo.

Não testado ainda: segundo monitor e escala de tela diferente de 100%, tema claro, a ação `Update windots`
do menu Setup, `Alt+Enter` (`alt`) e o início automático no login.

## Referências

- Menu: `shell/plugins/menu` e `config/omarchy/extensions/omarchy-menu.jsonc` no omarchy.
- Visual: `src/modules/launcher` (PowerToys Run) no PowerToys, que é MIT.
- Walker (GPL-3.0, Rust/GTK4) e Elephant ficam só como inspiração: não rodam no Windows.

## Licença

Ainda não escolhida.
