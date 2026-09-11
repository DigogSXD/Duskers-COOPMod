# 🛰️ Duskers CO-OP Mod

[![Release](https://img.shields.io/badge/Release-v1.1.0-green.svg)](https://github.com/DigogSXD/Duskers-COOPMod/releases)
[![Game Version](https://img.shields.io/badge/Duskers-v1.205%2B-blue.svg)](https://store.steampowered.com/app/254320/Duskers/)
[![Framework](https://img.shields.io/badge/BepInEx-5.4.21.0%20(x86)-orange.svg)](https://github.com/BepInEx/BepInEx/releases)
[![License](https://img.shields.io/badge/License-MIT-lightgrey.svg)](LICENSE)

Transforme o clássico roguelike espacial de sobrevivência **Duskers** em uma experiência cooperativa nativa e tática para múltiplos jogadores!

Opere a ponte de comando de drones com seus amigos em tempo real: compartilhem o mesmo terminal de comandos CRT, coordenem a exploração de naves abandonadas, abram e tranquem eclusas, e enfrentem perigos cósmicos juntos.

---

## 🌟 Funcionalidades Principais (Features)

* 🎮 **Integração Oficial Steam P2P (Zero Configuração):** Convide amigos direto pela lista de amigos da Steam com **Shift+Tab** ou apertando **[I]**. Não precisa de portas, IP público, Hamachi nem Radmin VPN!
* 🚀 **Tela de Carregamento Retrô CRT:** Ao aceitar o convite pela Steam, o jogo exibe uma tela autêntica em fósforo verde com telemetria SDR, barra de progresso dinâmica e autenticação de link antes de entrar na ponte de comando.
* 💻 **Terminal CRT Sincronizado em Tempo Real:** Comandos executados por qualquer operador aparecem instantaneamente na tela de todos os outros com o prefixo `[Operator X] > comando`.
* 🤖 **Controle Total da Frota ("Todo mundo controla tudo"):** Todos os jogadores têm acesso completo a todos os drones (`1`, `2`, `3`, `4`), geradores, sensores e portas da nave.
* 👥 **Suporte a Múltiplos Jogadores:** Jogue em 2, 3, 4 ou mais operadores na mesma sessão sem limites artificiais.
* 💾 **Sistema Seguro de 3 Slots de Save:** O jogo ganha um seletor de Saves no menu principal. O **Slot 1** mantém o seu progresso original intacto, enquanto os **Slots 2 e 3** podem ser usados para suas campanhas cooperativas.
* 🔄 **Reconexão Rápida Sem Queda de Sessão:** Se a conexão de alguém oscilar, o Host continua a missão sem travar. O cliente pode se reconectar com uma única tecla (**[R]** no menu ou digitando `coop reconnect` no terminal).
* 🕹️ **Interface Retro CRT Nativa:** Menus `[M]ultiplayer`, `Sa[v]e Slots` e telas de status integradas visualmente ao estilo original em fósforo verde do Duskers.

---

## 🚀 Tutorial de Instalação Sem Complicações (Passo a Passo)

Instalar o mod leva menos de 2 minutos. Siga os passos abaixo:

### Passo 1: Instalar o BepInEx (Carregador de Mods)
> ⚠️ **IMPORTANTE:** O Duskers é um jogo **32-bit (x86)**. É obrigatório usar a versão **x86** do BepInEx.

1. Baixe o **BepInEx 5.4.21.0 (x86)** pelo link oficial do GitHub:  
   👉 [**Download BepInEx_x86_5.4.21.0.zip**](https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x86_5.4.21.0.zip)
2. Abra a pasta onde o seu Duskers está instalado na Steam:
   * No Steam: clique com o botão direito em **Duskers** → **Gerenciar** → **Navegar pelos arquivos locais**.
   * (Caminho padrão: `C:\Program Files (x86)\Steam\steamapps\common\Duskers`)
3. Extraia o conteúdo do zip do BepInEx diretamente na pasta do jogo.  
   *(A pasta do jogo agora deve conter a pasta `BepInEx/`, o arquivo `doorstop_config.ini` e `winhttp.dll`).*

---

### Passo 2: Instalar o Duskers CO-OP Mod
1. Baixe o pacote pronto do mod:  
   👉 [**Download DuskersCoopMod_v1.1.0.zip**](https://github.com/DigogSXD/Duskers-COOPMod/raw/main/releases/DuskersCoopMod_v1.1.0.zip)  
   *(ou baixe diretamente os arquivos na pasta `releases/` deste repositório).*
2. Extraia o conteúdo para a pasta do Duskers, substituindo os arquivos se solicitado.  
   Isso instalará:
   * `Duskers/BepInEx/plugins/DuskersCoopMod.dll`
   * `Duskers/BepInEx/config/BepInEx.cfg` *(já com o patch essencial anti-tela preta aplicado!)*

---

### 💡 Dica Técnica (Se você já usava BepInEx antes):
Para evitar o erro de tela preta com a Unity 5.3 do Duskers, abra seu arquivo `BepInEx/config/BepInEx.cfg` e certifique-se de configurar o entrypoint:
```ini
[Preloader.Entrypoint]
Assembly = Assembly-CSharp.dll
Type = BootScreen
Method = Awake
```

---

## 🎮 Como Jogar com a Steam (Modo Recomendado)

Inicie o Duskers normalmente pela Steam. No Menu Principal, você verá as opções **`[M]ultiplayer`** e **`Sa[v]e Slots`**.

### Se você for o HOST (Quem cria a sala):
1. No menu principal, pressione **`M`** para entrar no menu de Multiplayer.
2. (Opcional) Escolha o slot de save que quer usar (ex: Slot 2 para co-op).
3. Pressione **`H`** para iniciar a sessão (**Host Session - Steam Lobby**).
4. Pressione **`I`** (ou abra o **Shift+Tab**) e clique em **Convidar Amigos** na sua lista da Steam.
5. Quando os amigos aceitarem, seus nomes aparecerão na lista de operadores da ponte.
6. Pressione **`P`** (**Play Game**) para iniciar a missão!

### Se você for o CONVIDADO:
1. Abra a conversa com o seu amigo na Steam ou a notificação do **Shift+Tab**.
2. Clique em **"Entrar no Jogo"** (ou aceite o convite).
3. Uma tela retrô de conexão (*"Establishing Comms Link"*) será exibida com telemetria ao vivo.
4. Assim que o link for autenticado, você entrará na ponte de comando para operar os drones junto com o Host!

---

## 🟢 Como Jogar Usando GreenLuma

O mod possui suporte nativo ao **GreenLuma**:

1. **Configuração no GreenLuma:**
   * Adicione o AppID do Duskers (**`254320`**) na sua pasta `AppList` do GreenLuma (ex: arquivo `254320.txt`).
   * O mod já inclui automaticamente o arquivo `steam_appid.txt` com `254320` na pasta raiz do Duskers.

2. **Se você for o HOST:**
   * Inicie o Duskers, vá em **`[M]` (Multiplayer)** e depois **`[H]` (Host Session)**.
   * O mod cria automaticamente um lobby público Steam e inicia o servidor local simultaneamente (Dual-Stack).
   * Pressione **`[C]` (Copy Bridge Steam ID)** para copiar seu Steam ID (ex: `76561198...`) e envie para seu amigo no Discord. Se preferir, pode convidar pelo Shift+Tab (**`[I]`**).

3. **Se você for o CONVIDADO:**
   * **Opção 1 (1-Clique pela Lista):** Vá em **`[M]`** e aperte **`[J]` (Join Session)**. Se o Host estiver no Duskers, o nome dele aparecerá na lista `--- STEAM FRIENDS PLAYING DUSKERS ---`. Basta apertar o número indicado (ex: **`[1]`**) para conectar direto!
   * **Opção 2 (Via Steam ID copiado):** Copie o Steam ID que o Host enviou, abra o jogo, vá em **`[J]oin Session`** e pressione **`[P]` (Fast Join from Clipboard)**. O mod detecta o Steam ID e conecta via Steam P2P com a tela de loading CRT!
   * **Opção 3 (Radmin VPN / LAN):** Se houver qualquer instabilidade na Steam, ambos podem usar Radmin VPN e conectar via **`[I] Join with Direct IP + Port`**.

---

## ⌨️ Comandos do Console In-Game (Terminal CRT)

Você pode interagir e testar a conexão a qualquer momento durante a missão digitando no próprio terminal:

| Comando | Descrição |
| :--- | :--- |
| `coop status` | Exibe o status da sessão, modo atual, lobby Steam e operadores conectados |
| `coop reconnect` | Reconecta instantaneamente ao Host se sua conexão oscilar |
| `coop host` | Inicia um servidor cooperativo ou lobby Steam |
| `coop disconnect` | Sai da sessão cooperativa atual |
| `coop help` | Exibe o manual de ajuda dos comandos cooperativos |

---

## 📁 Estrutura do Projeto

```text
DuskersCoopMod/
├── src/
│   └── DuskersCoopMod/
│       ├── Network/             # Steamworks P2P (SDR), sockets TCP de fallback e pacotes JSON
│       ├── Patches/             # Injeções Harmony no Console CRT, Menu e Saves
│       ├── Save/                # Gerenciador de múltiplos Slots de Save (Slot 1, 2, 3)
│       ├── UI/                  # Telas de menu integradas ao visual CRT nativo e Loading Screen
│       ├── CoopPlugin.cs        # Plugin BepInEx principal
│       └── DuskersCoopMod.csproj
├── releases/                    # Pacotes compilados e configurados prontos para uso
│   ├── DuskersCoopMod_v1.1.0.zip
│   └── BepInEx/
└── tools/
    └── coop_terminal_client.py  # Cliente CLI em Python para testes e monitoramento
```

---

## 🛠️ Compilando a partir do Código-Fonte

Requisitos:
* [.NET SDK 6.0+](https://dotnet.microsoft.com/download)
* Copiar as DLLs de referência de `Duskers/Duskers_Data/Managed/` e `BepInEx/core/`:
  * `UnityEngine.dll`, `UnityEngine.UI.dll`, `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`, `BepInEx.dll`, `0Harmony.dll`

Para compilar:
```powershell
dotnet build src\DuskersCoopMod -c Release
```
O arquivo `DuskersCoopMod.dll` será gerado na pasta `bin/Release/net35/` e implantado automaticamente na pasta do jogo.

---

## 📄 Licença

Este projeto é disponibilizado sob a licença **MIT**. Duskers é uma marca registrada de Misfits Attic. Este é um projeto sem fins lucrativos criado por fãs.
