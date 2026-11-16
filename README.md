# windots-launcher

Lançador e menu de comandos do [windots](https://github.com/higorhgon/windots), feito em WPF (.NET 9) para
Windows 11. O objetivo é funcionar como o menu do omarchy e, no fim, **substituir o Flow Launcher**.

> Estado: **fases 1 a 3 prontas** (motor, menu do omarchy, temas, atalhos de teclado, apps, calculadora, shell,
> processos e comandos do sistema). Arquivos, favoritos, URLs e a troca do Flow vêm nas próximas fases.

## O que ele é

- **Menu do omarchy.** O omarchy atual (branch `quattro`, `omacom/omarchy`) não usa mais o Walker: o menu é
  o plugin `omarchy.menu` do shell dele, dirigido por dados em JSONC. Este projeto lê o mesmo formato:
  ids com pontos (`style.theme` fica dentro de `style`), e por item `icon` (glifo Nerd Font),
  `label`, `action`, `target`, `provider` (lista dinâmica), `aliases`, `description`, `when`, `checked` e
  `alt`, além de rotas e voltar um nível. Uma adição do windots: `confirm` (o primeiro Enter pergunta, o
  segundo executa), para reiniciar, desligar e afins.
- **Lançador de apps e arquivos**, para trocar o Flow Launcher (hoje com Program, Explorer, Calculator,
  Shell, Sys, ProcessKiller, BrowserBookmark, Url, WebSearch e PluginsManager). Os apps já existem; o resto vem
  nas fases 3 e 4.
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

**Velocidade.** Abrir leva 16–60 ms com o processo residente (a primeira abertura depois de iniciar, uns 80 ms,
porque o `--background` renderiza o menu uma vez fora da tela). Iniciar `windots-launcher.exe toggle` só para
mandar o pedido custa um processo .NET inteiro (~300 ms), então o AutoHotkey escreve direto no pipe
`windots-launcher` (linha `verbo<TAB>rota`) e só abre o executável se o pipe não existir. O foco é pedido
com `SetForegroundWindow`; se o Windows recusar, a janela compartilha a fila de entrada da janela em primeiro
plano e pede de novo (o Windows só dá foco a quem recebeu a última entrada). `WINDOTS_LAUNCHER_TRACE=<arquivo>`
grava um log de diagnóstico.

Verbos (como o `omarchy menu`): `toggle [rota]` (padrão), `summon [rota]`, `close`, `refresh`, `quit`. Uma
rota é o id de um menu (`style.theme`) ou um apelido (`theme`, `keys`).
`windots-launcher.exe dump <rota> <arquivo>` grava as linhas de uma rota num arquivo, sem abrir janela (para
testes e scripts).

Teclas: `↑`/`↓` (ou `Ctrl+J`/`Ctrl+K`, `Ctrl+N`/`Ctrl+P`) escolhem, `PageUp`/`PageDown` pulam, `Enter`
executa, `Shift+Enter` executa a ação alternativa (o `alt` do item; no omarchy é o Alt), `Backspace` com o campo
vazio volta um nível, `Esc` limpa o filtro e depois fecha, o mouse também funciona. Perder o foco fecha a janela.
O Alt não serve para isso aqui: no windots ele faz o papel do Super, então o whkd fica com `Alt+Enter` (abre um
Terminal) e `Alt+setas`, e essas teclas nunca chegam à janela.

## Apps

No menu raiz, o que você digita procura nos apps instalados e nas linhas do menu, numa lista só, ordenada por
quão bem casa. A fonte é a mesma de "Todos os aplicativos" do Menu Iniciar (`shell:AppsFolder`, lida pelo
shell): os atalhos do Menu Iniciar e os apps da Store, com os nomes e os ícones do Windows (nesta máquina, 139
itens, o mesmo número do `Get-StartApps`). Um app abre com `explorer.exe shell:AppsFolder\<id>`.

- **Casamento:** cada palavra digitada precisa casar, pelo começo do nome, pelo começo de uma palavra, em
  qualquer ponto ou pelas iniciais (`vsc` acha Visual Studio Code). Nomes mais curtos vêm primeiro.
- **Uso:** apps que você abre muito sobem entre os que casam igual (`~\.local\state\windots\launcher-usage.json`).
- **Ícones:** carregados do shell, um por vez e fora da janela; a linha mostra um glifo até o ícone chegar.
- **Índice:** montado em segundo plano ao iniciar e de novo quando passam 10 minutos, então instalações novas
  aparecem sem reiniciar.
- `windots-launcher.exe dump apps:<texto> <arquivo>` grava o ranking de uma busca, sem janela.

## Calculadora, shell, processos e sistema

No menu raiz, o texto digitado também pode ser:

- **Uma conta** (`12*34+5`, `sqrt(144)+2^10`, `pi*2`, `0x1f+1`): aparece `= 413` no topo; Enter copia o resultado.
  Operadores `+ - * / % ^ ( )`, as constantes `pi`, `e` e `tau`, as funções `sqrt abs sin cos tan asin acos atan ln
  log log10 exp floor ceil round` e, com `;` separando os argumentos, `pow min max mod`. A vírgula entre dígitos é
  a vírgula decimal (`1,5+2`). Um número sozinho (`42`) ou um `e` solto não viram conta, para não atrapalhar a
  busca de apps.
- **`> comando`**: executa no PowerShell. Enter abre um Windows Terminal que mantém o comando aberto;
  `Shift+Enter` roda sem janela (como as ações do menu).
- **`kill nome`**: lista os processos que casam com o nome (agrupados, com a quantidade e a memória) e Enter, duas
  vezes (a primeira pede confirmação), encerra todos com os filhos. Só `kill`, sem nome, mostra os programas que têm
  janela. Nunca aparecem os processos que derrubariam o Windows (`csrss`, `winlogon`, `lsass`, `svchost`...)
  nem o próprio launcher.
- **O nome de um comando de qualquer submenu**: `shut`, `desligar`, `reboot`, `lixeira`, `tokyo`... acham
  System › Shut down, Restart, Empty Recycle Bin, o tema Tokyo Night etc., cada um com o menu a que pertence.
  Os que desligam, reiniciam ou apagam pedem confirmação (`confirm`).

O menu System tem Lock, Sleep, Hibernate, Log out, Restart, Shut down, Task Manager e a Lixeira (abrir e esvaziar),
com termos de busca em inglês e português (`searchTerms`).
`windots-launcher.exe dump q:<texto> <arquivo>` grava o que uma busca no menu raiz listaria, sem janela.

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
2. **Pronta.** Apps: Menu Iniciar e apps da Store, ícones, ranking por uso.
3. **Pronta.** Sistema (`sys`), calculadora, comandos (Shell) e processos.
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
do menu Setup, o `Shift+Enter` de um item do menu com `alt`, o `> comando` com Enter (abre o Windows Terminal), o
encerrar de um programa com permissão de administrador, o início automático no login e apps que a lista do
Windows mostra duas vezes com ids diferentes.

## Referências

- Menu: `shell/plugins/menu` e `config/omarchy/extensions/omarchy-menu.jsonc` no omarchy.
- Visual: `src/modules/launcher` (PowerToys Run) no PowerToys, que é MIT.
- Walker (GPL-3.0, Rust/GTK4) e Elephant ficam só como inspiração: não rodam no Windows.

## Licença

Ainda não escolhida.
