# windots-launcher

Lançador e menu de comandos do [windots](https://github.com/higorhgon/windots), feito em WPF (.NET 9) para
Windows 11. O objetivo é funcionar como o menu do omarchy e, no fim, **substituir o Flow Launcher**.

> Estado: repositório recém-criado, ainda sem código. Só existe o plano abaixo.

## O que ele é

- **Menu do omarchy.** O omarchy atual (branch `quattro`, `omacom/omarchy`) não usa mais o Walker: o menu é
  o plugin `omarchy.menu` do shell dele, dirigido por dados em JSONC. Este projeto lê o mesmo formato:
  ids com pontos (`appearance.theme` fica dentro de `appearance`), e por item `icon` (glifo Nerd Font),
  `label`, `action`, `target`, `provider` (lista dinâmica: temas, fontes), `aliases`, `description`,
  `when` (esconde a linha), `checked` (mostra ✓) e `alt` (outra ação com Alt segurado), além de rotas
  (`windots menu summon appearance.theme`) e voltar um nível.
- **Lançador de apps e arquivos**, para trocar o Flow Launcher (hoje com Program, Explorer, Calculator,
  Shell, Sys, ProcessKiller, BrowserBookmark, Url, WebSearch e PluginsManager).
- **Visual do PowerToys Run** (caixa de busca arredondada, lista com ícones, seleção na cor de destaque),
  pintado pela paleta do tema atual do windots (`~\.local\state\windots\theme.name`, paletas em
  `windots/default/themes`).

## Fases

1. Motor e menu: janela, JSONC do omarchy, rotas, filtro, executar ação, voltar; menu raiz e menu de temas.
2. Apps: Menu Iniciar e apps da Store, ranking por uso.
3. Sistema (`sys`), calculadora, comandos (Shell) e processos.
4. Arquivos, favoritos do navegador, URLs e busca na web.
5. Desinstalar o Flow Launcher e passar o `Alt+Space` para este app.

Até a fase 3 estar pronta o Flow Launcher continua instalado (é mais leve que o PowerToys inteiro).

## Atalho

`Win+Space`, registrado no `windots-hotkeys.ahk` do windots (AutoHotkey v2). O whkd não pega combinações
com a tecla Windows, e o Windows usa `Win+Space` para trocar o layout do teclado, o que não atrapalha
enquanto houver um layout só (pt-BR).

## Referências

- Menu: `shell/plugins/menu` e `config/omarchy/extensions/omarchy-menu.jsonc` no omarchy.
- Visual: `src/modules/launcher` (PowerToys Run) no PowerToys, que é MIT.
- Walker (GPL-3.0, Rust/GTK4) e Elephant ficam só como inspiração: não rodam no Windows.

## Licença

Ainda não escolhida.
