# Claude Multi-Account

[Read in English](README.md)

Roda uma segunda instância totalmente isolada do [Claude Desktop](https://claude.ai/download)
(ex.: uma conta corporativa junto com a pessoal) com **login, cookies, histórico e
cache próprios**, e faz essa instância aparecer como um **app separado na barra de
tarefas do Windows** — ícone próprio, agrupamento próprio, pin independente — sem
instalar, copiar ou modificar um único arquivo da instalação oficial do Claude.

```
Claude (pessoal)     Claude Work (corporativo)
    [ícone A]               [ícone B]        <- grupos separados na barra de tarefas
```

## Por que isso não é trivial

O Claude Desktop no Windows é distribuído pela Microsoft Store como pacote
**MSIX**. Apps MSIX rodam com uma identidade de pacote fixa (`AppUserModelID`,
`Claude_<hash>!Claude` neste caso) — é esse ID que o Windows usa para decidir
quais janelas agrupar na barra de tarefas. Só existe **um** `claude.exe`
instalado, então, não importa quantos diretórios de usuário diferentes você use
para lançá-lo, todas as janelas continuam pertencendo à mesma identidade de
processo/pacote e por isso sempre agrupam juntas.

A abordagem óbvia seria abrir o pacote, alterar o bootstrap do Electron para
chamar `app.setAppUserModelId(...)` com um ID próprio, e reempacotar — é assim
que builds "Insiders"/"Canary"/"Dev" de outros apps (VS Code, Chrome, Edge)
resolvem isso. **Essa rota não é viável aqui**: todo o conteúdo do pacote da
Store (`claude.exe`, `app.asar`, os `.pak`) é protegido com
`FILE_ATTRIBUTE_ENCRYPTED` (visível via `cipher /c`, reportado como "Aplicativo
Protegido") — uma proteção anti-cópia da própria Store. Copiar ou criar hard
link para esses arquivos falha por design do NTFS/EFS, então não dá pra extrair,
alterar e reempacotar nada do Claude oficial.

## A solução: AppUserModelID por janela

O Windows expõe uma API oficial e pouco conhecida para isso —
[`SHGetPropertyStoreForWindow`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-shgetpropertystoreforwindow) —
que permite ler/gravar propriedades do Shell (incluindo `PKEY_AppUserModel_ID`)
**diretamente em uma janela específica**, de fora do processo dono dela, sem
precisar que esse processo coopere. Gravar um AUMID diferente na janela da
instância "Work" faz o Windows tratá-la como um app à parte na barra de tarefas
— imediatamente, sem reiniciar o Explorer — e isso não toca em nenhum arquivo do
pacote protegido.

O que o `Claude Work.exe` faz:

1. Localiza a instalação oficial do Claude via `Get-AppxPackage` (cacheia o
   caminho resolvido; refaz a busca automaticamente se o Claude atualizar de
   versão).
2. Inicia `claude.exe --user-data-dir=<perfil próprio> --disk-cache-dir=<cache próprio>`.
   Isso já garante login/cookies/histórico/cache isolados (o lock de instância
   única do Electron é por perfil, então a instância pessoal e a Work coexistem).
3. Localiza a(s) janela(s) dessa instância (casando o diretório de perfil na
   linha de comando dos processos `claude.exe`) e grava nelas:
   - `PKEY_AppUserModel_ID` → identidade própria na barra de tarefas;
   - `PKEY_AppUserModel_RelaunchCommand`/`RelaunchDisplayNameResource`/`RelaunchIconResource`
     → para fixar o ícone certo ao dar pin;
   - `WM_SETICON` → nosso próprio `.ico` (em `assets/`) em vez do ícone padrão do Claude.
4. Fica residente enquanto a instância roda, recarimbando novas janelas que o
   Electron abrir, e encerra sozinho quando a instância Work fecha.
5. Registra o AUMID em `HKCU\...\AppUserModelId\<aumid>` (nome + ícone), para
   que notificações do Windows também apareçam com identidade própria.

Testado manualmente: fechar e reabrir a instância Work preserva o agrupamento
separado — o carimbo é reaplicado a cada nova janela automaticamente.

## Requisitos

- Windows 10/11
- Claude Desktop instalado pela Microsoft Store
- Para compilar: o compilador `csc.exe` do .NET Framework 4, que já vem com o
  Windows (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) — sem SDK
  externo necessário

## Instalação

Baixe o instalador mais recente (`ClaudeMultiAccount-Setup.exe`) na
[página de Releases](https://github.com/GiovanniTrevisan/claude-multi-account/releases)
e execute. Ele instala por usuário (sem precisar de admin), cria os atalhos
automaticamente e registra um desinstalador de verdade em Configurações do
Windows → Aplicativos.

## Build a partir do código-fonte

```powershell
git clone https://github.com/GiovanniTrevisan/claude-multi-account.git
cd claude-multi-account
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
```

`-Install` já cria os atalhos (Menu Iniciar + Área de Trabalho) com o ícone
customizado. Sem essa flag, o build só gera `dist\Claude Work.exe`.

Para gerar o instalador (precisa do [Inno Setup](https://jrsoftware.org/isinfo.php)):

```powershell
.\build.ps1
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.0.0 installer\ClaudeWork.iss
```

Ao dar push numa tag `v*` (ex.: `v1.0.0`), o workflow
[`.github/workflows/release.yml`](.github/workflows/release.yml) builda o
instalador e o anexa automaticamente a um novo GitHub Release.

## Uso

Clique no atalho "Claude Work" criado (ou rode `dist\"Claude Work.exe"`
diretamente). Ele fica residente em segundo plano cuidando da identidade da
janela; feche o Claude Work normalmente e o processo do launcher encerra
sozinho.

Para desinstalar: use Configurações do Windows → Aplicativos se você usou o
instalador, ou rode `Claude Work.exe --uninstall` para remover os atalhos e a
entrada de registro manualmente (seu perfil isolado do Claude — login,
cookies, cache — é preservado de qualquer forma).

### Configuração

A identidade (nome do perfil, AppUserModelID, nome exibido) pode ser definida
de duas formas, com os argumentos de linha de comando tendo precedência sobre
as variáveis de ambiente, que por sua vez têm precedência sobre os padrões:

| Argumento           | Variável de ambiente    | Padrão                      | Efeito                                     |
|----------------------|---------------------------|------------------------------|---------------------------------------------|
| `--profile=<nome>`  | `CLAUDE_WORK_PROFILE`   | `Claude-Work`               | Nome do perfil (`%LOCALAPPDATA%\<nome>`)    |
| `--aumid=<id>`      | `CLAUDE_WORK_AUMID`     | `ClaudeMultiAccount.Work`   | AppUserModelID gravado na janela            |
| `--name=<nome>`     | `CLAUDE_WORK_NAME`      | `Claude Work`                | Nome exibido (mensagens de erro, registro)  |

A forma recomendada de configurar uma segunda (ou terceira) conta é usando os
argumentos de linha de comando junto com `--install-shortcuts`, por exemplo:

```powershell
"Claude Work.exe" --profile="Claude-Work2" --aumid="ClaudeMultiAccount.Work2" --name="Claude Work 2" --install-shortcuts
```

Isso grava a identidade diretamente no atalho (nos argumentos do seu alvo) e
no `RelaunchCommand` da janela, então clicar no atalho — ou em "Abrir" pela
barra de tarefas/Menu Iniciar — sempre reabre exatamente aquele perfil,
independentemente de quais variáveis de ambiente estejam definidas naquele
momento. As variáveis de ambiente continuam funcionando e são úteis para
execuções avulsas ou via script, mas sozinhas elas **não** persistem num
atalho: um atalho criado só com `CLAUDE_WORK_PROFILE` definida reabriria
silenciosamente o perfil padrão ao ser lançado pelo Explorer, já que variáveis
de ambiente do momento não fazem parte do alvo de um `.lnk`. Passar os valores
como argumentos para `--install-shortcuts` evita esse problema por completo.

Rodando `Claude Work.exe --install-shortcuts` novamente recria os atalhos —
útil após mudar a identidade.

Para uma terceira conta, basta rodar com outro par `--profile`/`--aumid` (ou
as variáveis de ambiente equivalentes) e `--install-shortcuts`.

## Estrutura do projeto

```
src/
  Program.cs                    ponto de entrada / orquestração
  AppConfig.cs                  configuração via variáveis de ambiente
  ClaudeInstallationLocator.cs  localiza o claude.exe (via Get-AppxPackage, cacheado)
  ClaudeInstanceLauncher.cs     inicia o claude.exe com o perfil isolado
  ClaudeProcessInspector.cs     consultas WMI sobre linhas de comando do claude.exe
  ClaudeWindowFinder.cs         descoberta de janelas via EnumWindows
  TaskbarIdentityStamper.cs     aplica AUMID + props de relaunch + ícone numa janela
  InstanceIdentityWatcher.cs    loop residente: carimba novas janelas, sai quando termina
  ShortcutInstaller.cs          cria atalhos .lnk + registra o AUMID
  NativeMessageBox.cs           wrapper pequeno de MessageBox
  Interop/                      declarações P/Invoke, interfaces COM, PROPVARIANT
                                 — isolado do resto do código
installer/
  ClaudeWork.iss                 script do Inno Setup (instalação por usuário + desinstalador)
.github/workflows/release.yml    builda e publica o instalador a cada tag `v*`
```

## Limitações conhecidas

- Depende de `Get-AppxPackage`/PowerShell para localizar o Claude — se a
  Anthropic renomear o pacote MSIX, a detecção precisa ser ajustada.
- O carimbo de ícone (`WM_SETICON`) é reaplicado a cada segundo enquanto a
  instância roda, para o caso do Electron redesenhar o ícone padrão por cima;
  isso é uma pequena carga de polling, não um hook de evento.
- Se a Anthropic passar a oferecer um instalador Win32 (fora da Store) do
  Claude Desktop, um patch direto no `app.asar` (sem essa proteção de
  conteúdo) também se tornaria viável — mas não é necessário: a abordagem
  atual não depende disso.

## Licença

MIT — veja [LICENSE](LICENSE).
