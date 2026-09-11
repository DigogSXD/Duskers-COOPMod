# 🛰️ Duskers CO-OP Mod

[![Release](https://img.shields.io/badge/Release-v1.0.0-green.svg)](https://github.com/DigogSXD/Duskers-COOPMod/releases)
[![Game Version](https://img.shields.io/badge/Duskers-v1.205%2B-blue.svg)](https://store.steampowered.com/app/254320/Duskers/)
[![Framework](https://img.shields.io/badge/BepInEx-5.4.21.0%20(x86)-orange.svg)](https://github.com/BepInEx/BepInEx/releases)
[![License](https://img.shields.io/badge/License-MIT-lightgrey.svg)](LICENSE)

Transforme o clássico roguelike espacial de sobrevivência **Duskers** em uma experiência cooperativa nativa e tática para múltiplos jogadores!

Opere a ponte de comando de drones com seus amigos em tempo real: compartilhem o mesmo terminal de comandos CRT, coordenem a exploração de naves abandonadas, abram e tranquem eclusas, e enfrentem perigos cósmicos juntos.

---

## 🌟 Funcionalidades Principais (Features)

* 💻 **Terminal CRT Sincronizado em Tempo Real:** Comandos executados por qualquer operador aparecem instantaneamente na tela de todos os outros com o prefixo `[Operator X] > comando`.
* 🤖 **Controle Total da Frota ("Todo mundo controla tudo"):** Todos os jogadores têm acesso completo a todos os drones (`1`, `2`, `3`, `4`), geradores, sensores e portas da nave.
* 👥 **Suporte a Múltiplos Jogadores:** Jogue em 2, 3, 4 ou mais operadores na mesma sessão sem limites artificiais.
* 📋 **Entrada em 1 Clique (Códigos de Sessão):** O Host gera um código amigável (ex: `DSK-1AE0-3323-6C1E`). O amigo só precisa copiar para a área de transferência (<kbd>Ctrl</kbd>+<kbd>C</kbd>) e pressionar **[J]** no menu!
* 💾 **Sistema Seguro de 3 Slots de Save:** O jogo ganha um seletor de Saves no menu principal. O **Slot 1** mantém o seu progresso original intacto, enquanto os **Slots 2 e 3** podem ser usados para suas campanhas cooperativas.
* 🔌 **Porta de Rede Customizável:** Mude a porta do servidor para qualquer valor (1024-65535) direto no menu (`P[o]rt: XXXX`), digitando no teclado, colando da área de transferência ou pelo terminal (`coop port <porta>`). O código da sessão codifica automaticamente a sua porta personalizada!
* 🔄 **Reconexão Rápida Sem Queda de Sessão:** Se a conexão de alguém oscilar, o Host continua a missão sem travar. O cliente pode se reconectar com uma única tecla (**[R]** no menu ou digitando `coop reconnect` no terminal).
* 🕹️ **Interface Retro CRT Nativa:** Menus `[M]ultiplayer`, `Sa[v]e Slots` e `P[o]rt Settings` integrados visualmente ao estilo original em fósforo verde do Duskers.

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
   👉 [**Download DuskersCoopMod_v1.0.0.zip**](https://github.com/DigogSXD/Duskers-COOPMod/raw/main/releases/DuskersCoopMod_v1.0.0.zip)  
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

## 🎮 Como Jogar

Inicie o Duskers normalmente pela Steam. No Menu Principal, você verá as opções **`[M]ultiplayer`** e **`Sa[v]e Slots`**.

### Se você for o HOST (Quem cria a sala):
1. No menu principal, pressione **`M`** para entrar no menu de Multiplayer.
2. (Opcional) Escolha o slot de save que quer usar (ex: Slot 2 para co-op).
3. Pressione **`H`** para iniciar a sessão (**Host Session**).
4. Pressione **`C`** para copiar o **Código da Sessão** (ex: `DSK-1AE0-3323-6C1E`) e envie para os seus amigos pelo Discord/WhatsApp.
5. Quando os amigos entrarem, seus nomes aparecerão na lista de operadores da ponte.
6. Pressione **`P`** (**Play Game**) para iniciar a missão!

### Se você for o CLIENT (Quem entra na sala):
1. Copie o código que o Host te enviou para a sua área de transferência (<kbd>Ctrl</kbd>+<kbd>C</kbd> no Discord).
2. Abra o Duskers e pressione **`M`** para abrir o menu Multiplayer.
3. Pressione **`J`** (**Join Session**). O jogo detecta o código automaticamente e conecta na mesma hora!
4. Pressione **`P`** (**Play Game**) para entrar no terminal da nave!

---

## 🌐 Conexão Online (Jogando pela Internet)

Se você e seus amigos estiverem em casas diferentes:
* **Opção Recomendada (Zero dor de cabeça):** Usem uma VPN virtual para jogos como **Radmin VPN** ou **ZeroTier / Hamachi**. Basta que ambos estejam na mesma rede virtual do Radmin e o código de sessão cuidará de todo o resto automaticamente!
* **Port Forwarding:** Se o Host tiver IP público, basta abrir a porta TCP `7777` no roteador.

---

## ⌨️ Comandos do Console In-Game (Terminal CRT)

Você pode interagir e testar a conexão a qualquer momento durante a missão digitando no próprio terminal:

| Comando | Descrição |
| :--- | :--- |
| `coop status` | Exibe o status da sessão, modo atual, porta e operadores conectados |
| `coop port <número>` | Muda ou consulta a porta TCP usada para hospedar (1024 a 65535) |
| `coop reconnect` | Reconecta instantaneamente ao Host anterior se sua internet oscilar |
| `coop host [porta]` | Inicia um servidor cooperativo diretamente pelo terminal (usa porta configurada ou especificada) |
| `coop connect <código ou IP>` | Conecta a uma sessão remota via código ou IP direto |
| `coop disconnect` | Sai da sessão cooperativa atual |
| `coop help` | Exibe o manual de ajuda dos comandos cooperativos |

---

## 📁 Estrutura do Projeto

```text
DuskersCoopMod/
├── src/
│   └── DuskersCoopMod/
│       ├── Network/             # TCP socket client/server, serialização de pacotes JSON
│       ├── Patches/             # Injeções Harmony no Console CRT, Menu e Saves
│       ├── Save/                # Gerenciador de múltiplos Slots de Save (Slot 1, 2, 3)
│       ├── UI/                  # Telas de menu integradas ao visual CRT nativo
│       ├── CoopPlugin.cs        # Plugin BepInEx principal
│       └── DuskersCoopMod.csproj
├── releases/                    # Pacotes compilados e configurados prontos para uso
│   ├── DuskersCoopMod_v1.0.0.zip
│   └── BepInEx/
└── tools/
    └── coop_terminal_client.py  # Cliente CLI em Python para testes e monitoramento
```

---

## 🛠️ Compilando a partir do Código-Fonte

Requisitos:
* [.NET SDK 6.0+](https://dotnet.microsoft.com/download)
* Copiar as DLLs de referência de `Duskers/Duskers_Data/Managed/` e `BepInEx/core/`:
  * `UnityEngine.dll`, `UnityEngine.UI.dll`, `Assembly-CSharp.dll`, `BepInEx.dll`, `0Harmony.dll`

Para compilar:
```powershell
dotnet build src\DuskersCoopMod -c Release
```
O arquivo `DuskersCoopMod.dll` será gerado na pasta `bin/Release/net35/`.

---

## 📄 Licença

Este projeto é disponibilizado sob a licença **MIT**. Duskers é uma marca registrada de Misfits Attic. Este é um projeto sem fins lucrativos criado por fãs.
