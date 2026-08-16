# Plano de Ação — Servidores CS2 na AWS (Summit)

> Objetivo: quando o veto termina, a Summit.Api sobe uma EC2 efêmera com CS2 +
> MatchZy, os jogadores conectam pelo botão da sala, e ao fim da partida o
> resultado volta pra API e a instância se auto-destrói.
> Ponto de integração no código: criação da sala em
> `Summit.Api/CompetitionEndpoints.cs` (veto completo) + `LifecycleWorker`.

## Fase 0 — Preparar a conta (30 min, no console AWS)
1. Região: **sa-east-1 (São Paulo)** — ping decente no BR. Fixar em tudo.
2. **Billing alarm** primeiro de tudo: CloudWatch → alarme de custo (ex. US$ 20/mês). Evita susto.
3. **IAM**: criar usuário `summit-api` (programmatic access) com política mínima:
   `ec2:RunInstances`, `ec2:TerminateInstances`, `ec2:DescribeInstances`,
   `ec2:CreateTags`, `s3:GetObject/PutObject` no bucket, `iam:PassRole` (da role da instância).
   Guardar Access Key/Secret → vão pro `appsettings`/env da API (NUNCA no git).
4. Checar quota de vCPUs spot em sa-east-1 (Service Quotas → EC2). Pedir aumento se < 8.

## Fase 1 — Pré-requisitos do jogo (30 min)
1. **GSLT** (Game Server Login Token): https://steamcommunity.com/dev/managegameservers
   → App 730 → criar token. Sem ele o servidor não registra na Steam/VAC.
   Guardar no **SSM Parameter Store** (`/summit/gslt`), não em texto plano.
2. **S3**: bucket `summit-matches` (privado) — `configs/{matchId}.json` e `demos/`.
3. **Security Group** `summit-cs2`:
   - UDP 27015 ← 0.0.0.0/0 (jogo)
   - TCP 27015 ← só o IP da API (RCON)
   - UDP 27020 ← 0.0.0.0/0 (GOTV, opcional)
   - SSH 22 ← só teu IP (debug)
4. **Key pair** pra SSH de debug.

## Fase 2 — POC manual: um servidor rodando na unha (1 dia)
1. Subir EC2 Ubuntu 22.04, **c5.large**, EBS gp3 **60 GB** (CS2 pesa ~35 GB), SG acima.
2. Instalar via SSH:
   ```bash
   sudo apt update && sudo apt install -y lib32gcc-s1 curl
   mkdir ~/steamcmd && cd ~/steamcmd
   curl -sqL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar zxvf -
   ./steamcmd.sh +force_install_dir ../cs2 +login anonymous +app_update 730 validate +quit
   ```
3. Instalar **Metamod + CounterStrikeSharp + MatchZy** (plugin de partida:
   warmup, knife round, ready system, placar, demo, webhook de resultado).
4. Rodar:
   ```bash
   ./cs2/game/bin/linuxsteamrt64/cs2 -dedicated -port 27015 \
     +sv_setsteamaccount SEU_GSLT +map de_mirage +sv_password teste123
   ```
5. Do teu PC: console CS2 → `connect IP_PUBLICO; password teste123`. **Entrou? POC ok.**

## Fase 3 — AMI + Launch Template (meio dia)
1. Escrever `user-data` (roda no boot):
   - lê tag `matchId` da instância (metadata)
   - baixa `s3://summit-matches/configs/{matchId}.json` (times, steamids, mapa, senha)
   - pega GSLT do SSM
   - gera cfg do MatchZy e inicia o CS2 no mapa do veto
   - MatchZy: webhook de fim de partida → `POST http://SEU_API/api/matches/{id}/result`
   - sobe a demo pro S3 e roda `shutdown -h now`
2. Na instância do POC configurada: **criar AMI** `summit-cs2-v1`.
3. **Launch Template** `summit-cs2`: AMI, c5.large **spot** (fallback on-demand),
   SG, instance profile (S3 read + SSM read), `InstanceInitiatedShutdownBehavior=terminate`.
4. Testar manual: `aws ec2 run-instances --launch-template ... --tag matchId=xxx` → conectar → terminou sozinho?

## Fase 4 — Integração na Summit.Api (1-2 dias, comigo)
1. NuGet: `AWSSDK.EC2`, `AWSSDK.S3`.
2. Novo `MatchServerService`:
   - `ProvisionAsync(match)`: sobe config no S3 → `RunInstances` (template + tag matchId)
     → Match.Status = Preparando
   - `LifecycleWorker` observa `DescribeInstances` → IP público pronto →
     grava `Match.ServerIp` real → status Ready (o hub do client já mostra tudo sozinho)
3. Trocar o gancho atual (IP fake em `CompetitionEndpoints`, veto completo) pela chamada real.
4. Endpoint `POST /api/matches/{id}/result` (webhook MatchZy): placar + stats por player
   → vencedor avança na chave → `TerminateInstances`.
5. Config por env: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_REGION=sa-east-1`,
   `SUMMIT_LAUNCH_TEMPLATE`, `SUMMIT_S3_BUCKET`.
   Obs.: a API precisa ser alcançável pela EC2 (webhook) — pra dev, ngrok/túnel; depois API na AWS.

## Fase 5 — Teste real de fogo (1 dia)
Camp de teste no Summit → veto → EC2 sobe (~90-120 s de cold start — o hub já
mostra "PREPARANDO SERVIDOR") → conectar pelo botão → jogar → resultado volta →
chave avança → instância morre. Medir custo real (meta: ~R$ 0,15/partida spot).

## Custos estimados (MVP, do camp.txt)
| Item | R$/mês |
|---|---|
| EC2 spot c5.large (200 partidas ~1h) | ~30 |
| S3 (configs + demos 20 GB) | ~5 |
| Transferência de dados (~50 GB) | ~25 |
| **Total partidas** | **~60** |

## Armadilhas conhecidas
- **Spot interruption**: AWS pode retomar a instância no meio da partida (raro, mas real).
  MVP: aceitar; depois: fallback on-demand pra finais.
- **IP público muda** a cada instância — por isso o fluxo grava o IP na sala na hora.
- **GSLT**: 1 token por servidor simultâneo; token banido se vazar. Rotacionar.
- **Cold start**: sem pool quente é 90-120 s. UX honesta no hub resolve; pool warm depois.
- **EBS da AMI** custa ~R$ 0,50/GB/mês parado — manter 1 AMI só.
- Elastic IP **desanexado** cobra por hora — não usar no MVP.

### POC (Fase 2) — 3 problemas reais encontrados e resolvidos (21/jul/2026)
Confirmado ao vivo numa `c5.large` sa-east-1, Ubuntu 24.04. Guarde isso pro `user-data` da AMI (Fase 3):

1. **Disco de 60 GiB não é suficiente** — o CS2 precisa pré-alocar ~57 GB e o volume de 60 GiB
   líquido dá só ~55 GB. Usar **no mínimo 80-100 GiB** no Launch Template.
   (Se acontecer de novo: `sudo growpart /dev/nvme0n1 1 && sudo resize2fs /dev/nvme0n1p1` depois de aumentar o volume no console/API.)
2. **`libv8.so: cannot open shared object file`** — o binário roda de `game/bin/linuxsteamrt64/`
   mas depende de libs que só existem em `game/csgo/bin/linuxsteamrt64/`. Fix: exportar as DUAS
   pastas no `LD_LIBRARY_PATH` antes de rodar:
   ```bash
   cd ~/cs2/game
   export LD_LIBRARY_PATH="$PWD/bin/linuxsteamrt64:$PWD/csgo/bin/linuxsteamrt64:$LD_LIBRARY_PATH"
   ```
3. **`steamclient.so` ausente em `~/.steam/sdk64/`** — o SteamCMD baixa o arquivo em
   `~/steamcmd/linux64/steamclient.so`, mas o CS2 procura em `~/.steam/sdk64/steamclient.so`. Fix:
   ```bash
   mkdir -p ~/.steam/sdk64
   ln -sf ~/steamcmd/linux64/steamclient.so ~/.steam/sdk64/steamclient.so
   ```
4. **SSH cai → servidor morre junto** — rodar o `cs2` solto no terminal SSH é fatal: qualquer
   queda de conexão mata o processo em primeiro plano (visto na prática: `client_loop: send
   disconnect: Connection reset`). Sempre rodar dentro de `screen` (ou `tmux`/`nohup`) pra
   sobreviver à sessão:
   ```bash
   sudo apt install -y screen
   screen -S cs2server
   # ... roda o comando do cs2 normalmente aqui dentro ...
   # Ctrl+A depois D pra "soltar" sem matar o processo
   # screen -r cs2server pra voltar a ver o console depois
   ```
   No AMI/user-data (Fase 3) isso não é problema — lá o processo já roda como serviço/systemd,
   não numa sessão SSH interativa.

### ✅ CounterStrikeSharp — desbloqueado, era instalação incompleta (23/jul/2026)
O bloqueio de 22/jul (`undefined symbol: g_bUpdateStringTokenDatabase`) **não era incompatibilidade
real** — o PR que corrigiu esse exato erro (`#1348`, pro CS2 update 1.41.6.9) foi mesclado no
mesmo dia do lançamento da v1.0.371, ou seja, já estávamos testando a versão corrigida em 22/jul.
A causa real foi uma instalação incompleta/corrompida naquela tentativa.

Fix: baixar de novo do zero e sobrescrever a pasta inteira (não só copiar por cima sem limpar):
```bash
sudo pkill -9 -f 'bin/linuxsteamrt64/cs2'
cd ~
curl -L -o css.zip https://github.com/roflmuffin/CounterStrikeSharp/releases/download/v1.0.371/counterstrikesharp-with-runtime-linux-1.0.371.zip
rm -rf ~/cs2/game/csgo/addons/counterstrikesharp
cd ~/cs2/game/csgo
unzip -o ~/css.zip
```
Validado ao vivo: `CSSharp: CounterStrikeSharp.API Loaded Successfully.` / `Hooks added.` /
`[META] Loaded 1 plugin.` — zero erro de `undefined symbol`.

**MatchZy 0.8.15 instalado por cima e validado no mesmo teste** (pacote sem CSS embutido, só
`MatchZy-0.8.15.zip` puro, já que o CounterStrikeSharp da instância já estava funcionando):
```
[MatchZy] [InitializeDatabase] SQLite Database connection successful
[MatchZy] [AutoStart] autoStartMode: 1
[MatchZy] [StartWarmup] Starting warmup!
[MatchZy 0.8.15 LOADED] MatchZy by WD-
```
**Stack completa (CS2 + Metamod + CounterStrikeSharp + MatchZy) funcionando de ponta a ponta.**
Path livre pra Fase 3 (criar a AMI definitiva já com tudo isso embutido, sem precisar reinstalar
a cada boot) quando quiser seguir.

**Armadilha à parte descoberta nesse teste — `screen` com nome repetido de sessão morta:**
se um `screen -dmS cs2server ...` anterior morreu (processo caiu) mas a sessão ainda aparece em
`screen -ls` como `(Dead ???)`, um NOVO `screen -dmS cs2server ...` com o **mesmo nome** pode
falhar silenciosamente — o comando roda mas o processo não fica de fato detached num `screen`
novo, e some sem aviso quando a sessão SSH que o lançou termina. Sintoma: `ps aux | grep cs2`
vazio pouco depois de ter visto logs do processo rodando. Fix: `screen -wipe` (limpa sessões
mortas) antes de relançar, ou usar um nome de sessão diferente a cada tentativa manual
(`screen -dmS cs2test2 ...`).

Arquivos já no lugar certo nessa instância de teste (não precisa refazer):
- `~/cs2/game/csgo/addons/metamod/` (Metamod instalado e funcionando)
- `~/cs2/game/csgo/gameinfo.gi` já tem a linha `Game csgo/addons/metamod` inserida
- `~/cs2/game/csgo/addons/counterstrikesharp/` (arquivos da v1.0.371, aguardando update)

### ✅ Fase 2 (POC) validada ao vivo em 21/jul/2026
Conectou de verdade no mapa de_mirage rodando na EC2. Ciclo completo confirmado:
EC2 c5.large sa-east-1 → CS2 instalado via steamcmd → servidor sobe com GSLT válido →
autentica na Steam (VAC secure) → cliente conecta pelo IP público → dentro do jogo.
Todas as 4 armadilhas acima foram as únicas barreiras — nenhuma é bloqueante, todas têm fix
de uma linha. Path livre pra Fase 3 (AMI + Launch Template) quando quiser seguir.

### 5ª armadilha — `/etc/machine-id` clonado na AMI trava a autenticação Steam (23/jul/2026)
Depois de validar o POC, toda instância criada a partir da AMI `summit-cs2-v1` travava mudo no
mesmo ponto de sempre (logo após `libv8system.so`, antes de qualquer mensagem de rede/Steam) —
inclusive com `screen` já garantido, GSLT trocado por um token novo, e Security Group com saída
liberada (`-1`/todas as portas/`0.0.0.0/0`, conferido via `DescribeSecurityGroups`). As threads do
processo ficavam num loop limpo (`futex_do_wait`/`hrtimer_nanosleep`, nada de I/O bloqueado),
memória e disco sobrando, sem erro no `dmesg`.

Causa raiz: a AMI foi criada por snapshot de uma instância já rodando, sem limpar
`/etc/machine-id` antes — então **toda instância nascida dela tinha a mesma identidade de
máquina** (`cat /etc/machine-id` idêntico em instâncias completamente diferentes, confirmado
comparando duas). A Steam aparentemente rejeita/trava silenciosamente a autenticação de
game-server quando detecta o mesmo fingerprint de máquina tentando logar repetidas vezes em
pouco tempo (e hoje isso aconteceu bastante: 3 instâncias simultâneas nasceram sem querer de
resets de veto no meio de um provisionamento).

Fix (permanente, dentro do `BuildUserData`/`BuildIdleUserData` em `MatchServerService.cs`,
roda como root antes do `su ubuntu`):
```bash
rm -f /etc/machine-id
systemd-machine-id-setup   # regenera a partir da UUID real da VM (unica por instancia)
systemctl restart dbus
```
Validado ao vivo: instância nova, machine-id diferente a cada boot, servidor sobe até
`SV: 64 player server started` sem intervenção manual.

### Pool "quente" de servidores (23/jul/2026) — cold start de 90-120s era inviável pro veto
Mesmo com o bug acima corrigido, uma EC2 nova ainda leva 60-120s+ pra ficar pronta (cloud-init +
CS2 carregando ~35GB do zero). Pra um veto de partida real isso é tempo demais. Solução: manter
`SUMMIT_POOL_SIZE` (env var, default `1`) instâncias sempre ligadas, CS2 já rodando num mapa
neutro (`de_dust2`, sem senha, com `rcon_password` fixo) — na hora do veto, em vez de criar
instância nova, a API manda `changelevel {mapa}` + `sv_password {senha}` via **RCON** (protocolo
Source RCON, TCP porta 27015) pro servidor livre. Turnaround caiu de minutos pra segundos.

Peças novas: `Models/PoolServer.cs` (estado Booting/Idle/InUse/Unhealthy),
`Summit.Api/RconClient.cs` (cliente RCON caseiro, sem NuGet), `Summit.Api/PoolManagerService.cs`
(BackgroundService: repõe o pool, só marca Idle depois de confirmar via RCON de verdade — não
só "instância Running na AWS", que não quer dizer CS2 pronto — e libera automaticamente
servidores vazios). Endpoint `GET /api/debug/pool` pra inspecionar sem entrar na AWS.
Sem servidor livre no pool, cai pro cold boot antigo (`ProvisionAsync`) como fallback — zero
mudança de UX nesse caso, o hub já mostra "PREPARANDO SERVIDOR... ~90S" pra esse cenário.

**3 bugs encontrados no `RconClient.cs` caseiro ao validar (guardar pra não recair):**
1. Leitura de pacote desalinhada: lia 8 bytes como "header" (achando que eram só o campo
   `size`) e depois mais `size` bytes como "resto" — descartava os 4 bytes do campo `id` e
   desalinhava tudo daí pra frente. Fix: ler `size` (4 bytes) sozinho, depois exatamente `size`
   bytes pro resto (`id`+`type`+corpo+2 nulls).
2. `NetworkStream.ReadAsync` retorna `ValueTask<int>` — chamar `.AsTask()` dele e DEPOIS dar
   `await` na `ValueTask` original de novo (não na Task já convertida) lança
   `InvalidOperationException` (consumo duplo de `ValueTask`). Fix: converter pra `Task` uma
   única vez e reusar essa referência.
3. **Nome de mapa sem prefixo**: o pool de mapas do veto guarda nomes curtos ("Nuke", "Ancient"),
   mas o `changelevel`/`+map` do CS2 precisa do nome real do arquivo (`de_nuke`, `de_ancient`).
   Mandar `changelevel Nuke` sem prefixo não move o mapa — o console só ecoa `int(0=0x0)` e
   fica no mapa antigo. Esse bug também estava silenciosamente presente no `BuildUserData` do
   cold-boot (só não tinha sido percebido porque os testes manuais usavam `de_mirage` digitado
   à mão). Fix: helper `ToConsoleMapName()` (`"de_" + nome.ToLower()`, idempotente) aplicado
   nos dois caminhos.

### ✅ Fase 3 (AMI definitiva) concluída — rebuild completo em 15/ago/2026
A AMI `summit-cs2-v1` (jul/2026) tinha sido apagada da conta numa limpeza de custo — descoberto ao
tentar usar `SUMMIT_MATCH_PROVIDER=aws` de novo (`DescribeImages` devolveu `does not exist`). O key
pair `summit-cs2-key` também tinha sumido. Refeito do zero, tudo via os novos endpoints de debug
(`/api/debug/find-ubuntu-ami`, `launch-build-instance`, `create-key-pair`, `authorize-ip`,
`create-image` — em `Program.cs`, reaproveitáveis pra próxima vez):

1. AMI base: Ubuntu 24.04 oficial da Canonical mais recente encontrada em sa-east-1
   (`ami-031a45ebb21af623a`, `ubuntu-noble-24.04-amd64-server-20260714`).
2. Instância de build: `c5.large`, **100 GiB gp3** (a lição de 60 GiB insuficiente ainda vale — o
   CS2 sozinho ocupou 67 GiB depois de instalado).
3. Key pair novo (`summit-cs2-key2`) e IP atual autorizado no security group (22+27015 TCP) — os
   dois recursos antigos amarrados ao `SUMMIT_KEY_PAIR_NAME`/regra de firewall tinham expirado.
4. Stack instalada limpa: CS2 (steamcmd, ~71 GiB via app_update 730) → Metamod (build atual
   `git1410`, o `git1358` hardcoded no plano antigo já tinha sido descontinuado) → gameinfo.gi
   patcheado (**cuidado**: `sed` com `\t` no texto de append não gera tab de verdade em todo
   `sed`/shell — usar `awk`/heredoc com tab literal e manter o CRLF do arquivo original, senão a
   linha fica corrompida) → CounterStrikeSharp v1.0.371 (overwrite completo, mesmo fix já
   documentado abaixo) → MatchZy 0.8.15.
5. **Armadilha nova, mesma raiz da #3 antiga**: mesmo numa instância nova (não clonada de AMI), o
   `steamclient.so` não está em `~/.steam/sdk64/` por padrão — o mesmo `ln -sf` documentado abaixo
   resolve. Validado ao vivo: Steam auth OK, VAC secure, `[MatchZy 0.8.15 LOADED]`,
   `CSSharp: Hooks added.`, porta 27015 UDP/TCP escutando.
6. AMI criada com `CreateImage` (reboot automático, sem `NoReboot`) — **snapshot de 100 GiB levou
   ~65 min pra ficar `completed`** (proporção não-linear com o tamanho: ritmo caiu de ~1.7%/min no
   início pra mais devagar perto do fim — planejar essa espera, não é operação rápida).
7. **AMI nova: `ami-012a6cfa5008d60f0`** (`summit-cs2-v2`). `SUMMIT_AMI_ID` atualizada. Instância
   de build terminada logo após o snapshot completar (evitar cobrança de EC2 parada à toa).

**Lição de processo**: a AMI e o key pair morreram numa limpeza de custo sem isso ficar registrado
em lugar nenhum — o `SUMMIT_AMI_ID`/`SUMMIT_KEY_PAIR_NAME` no ambiente continuavam apontando pra
recursos inexistentes silenciosamente (só descoberto ao tentar usar de verdade). Vale conferir
`GET /api/debug/ami-status` antes de qualquer sessão que for usar `SUMMIT_MATCH_PROVIDER=aws`.

### ✅ Fase 4+5 (integração real + teste de fogo) validadas em 15/ago/2026
Com a AMI `ami-012a6cfa5008d60f0` no ar, subiu a API com `SUMMIT_MATCH_PROVIDER=aws` de verdade
(não simulado) e jogou um campeonato inteiro pelo fluxo real (sem nenhum atalho de resultado
"local"): criação via `POST /api/tournaments`, inscrição de 2 times reais (`team_faze`,
`team_vit`), check-in, geração de chave, **veto real** (`/api/debug/simulate-veto`, que usa os
mesmos endpoints `/api/veto/*` que o client usa). No fim do veto, `AdvanceSeriesAsync`/
`ProvisionAsync` chamou a AWS de verdade:

- **EC2 real provisionada** (`i-05ffbf913428713f7`, `c5.large`, sa-east-1) com o mapa exato do
  veto (`de_inferno`) e senha gerada, via o mesmo `LaunchInstanceAsync`/`BuildUserData` de produção
  (não o caminho manual de build usado na Fase 3).
- `ServerProvisionPoller` confirmou IP público e marcou a sala `Ready` (`ProvisionState=3`) —
  bateu com o SSH manual: `SV: Connection to Steam servers successful`, `AuthStatus... OK`,
  `SV: VAC secure mode is activated.`, `GC Connection established`, `[MatchZy 0.8.15 LOADED]`,
  `CSSharp: Hooks added.` — servidor real, funcional, jogável.
- Resultado postado direto em `POST /api/matches/{id}/result` (o mesmo endpoint que o webhook do
  MatchZy chamaria de verdade) — `FAZE` venceu 16-10, chave avançou, campeonato fechou sozinho
  (`Status=Finished`), `FAZE.FinalPosition=1`, `VIT.FinalPosition=2, IsEliminated=true`.
- Instância terminada logo depois (`POST /api/debug/instances/{id}/terminate`) pra não continuar
  cobrando — volume EBS de 100 GiB com `DeleteOnTermination` default, some junto.

**Conclusão**: o ciclo completo (veto → EC2 real → CS2+MatchZy+CSS rodando de verdade, VAC secure
→ resultado → chave avança → campeonato fecha) está provado ponta a ponta. `SUMMIT_POOL_SIZE`
continua em `0` (cold-boot puro testado; pool quente ainda não reexercitado com a AMI nova). Depois
deste teste a API voltou pra `SUMMIT_MATCH_PROVIDER=local` (padrão seguro de desenvolvimento —
evita provisionar EC2 real sem querer durante trabalho normal no client/API).

### ✅ Webhook automático do MatchZy configurado em 15/ago/2026 (`Summit.Api/MatchZyIntegration.cs`)
Até aqui o resultado só chegava manualmente (via `/api/debug/force-match-result` ou postado à mão
imitando o webhook, como no teste da Fase 4/5). Peça que faltava: o MatchZy real chamando a API
sozinho quando o mapa termina, sem intervenção nenhuma. Pesquisado o schema oficial do MatchZy
(`shobhit-pathak/MatchZy`, arquivos `Events.cs`/`MatchData.cs`/`PublishEvents.cs` no GitHub —
não tem doc formal completa, teve que ler o código-fonte) e implementado:

- **`GET /api/matchzy-config/{matchId}`** — o config real que o MatchZy carrega
  (`matchzy_loadmatch_url`): `team1`/`team2` com nome + roster real (steamid→nick, puxado de
  `Team.Members`), `num_maps: 1` (cada EC2 nossa = 1 mapa só — a série MD3/MD5 é feita de vários
  boots de EC2 separados, não pelo veto interno de série do MatchZy), `maplist` com o mapa já
  escolhido no NOSSO veto, e um bloco `cvars` com `hostname`, `sv_password` (mesma senha que já
  usávamos) e — o pulo do gato — `matchzy_remote_log_url` já embutido apontando pro nosso endpoint
  de eventos. `matchid` foi deixado de fora de propósito (o MatchZy autogera um se omitido — a
  correlação com o `Match.Id` nosso é feita pelo path da URL, não por esse campo, que aliás é
  `long` no MatchZy e nosso `Match.Id` é string, então não dava pra reusar de qualquer jeito).
- **`POST /api/matchzy-events/{matchId}`** — recebe TODO evento que o `matchzy_remote_log_url`
  manda (`round_end`, `side_picked`, etc. — o MatchZy despeja tudo nessa URL); só age no
  `"event": "map_result"` (que pra nós já é o resultado final, já que 1 EC2 = 1 mapa). Resolve
  cada jogador de volta pro nosso `User` via `steamid` (o roster que mandamos no config É os
  usuários reais, então o steamid bate 100%), calcula ADR (`damage/rounds_played`) e uma
  aproximação de rating (MatchZy não manda rating estilo HLTV pronto), e **reusa o endpoint de
  resultado já testado** (`POST /api/matches/{id}/result`, mesmo padrão de auto-chamada que o
  `/api/debug/simulate-veto` já usa) em vez de duplicar a lógica de avanço de chave.
- `MatchServerService.BuildUserData`: com `SUMMIT_PUBLIC_API_URL` configurada, o boot troca
  `+map/+sv_password` diretos por `+matchzy_loadmatch_url "{url}/api/matchzy-config/{matchId}"` —
  sem essa env var, cai no comportamento antigo (sem MatchZy configurado, sem webhook automático),
  pra não quebrar quem ainda não tem a API exposta na internet.
- **Verificado via evento sintético** (POST direto imitando exatamente o payload que o MatchZy
  manda de verdade, com steamids reais de `team_faze`/`team_vit`): os 5 jogadores foram resolvidos
  certo pelos `User` corretos, K/D/A/HS/ADR bateram exatamente com o enviado, `isMvp` bateu com
  `mvp > 0`, o placar (10-16) e o vencedor (FAZE) ficaram certos, a chave avançou e o campeonato
  fechou sozinho (`FinalPosition` 1º/2º corretos) — mesmo comportamento já provado no teste real da
  Fase 4/5, só que entrando pelo caminho do webhook em vez de manual.
- **Idempotência confirmada incidentalmente**: um teste anterior (mesmo fluxo, sem o delay
  estendido) teve o resultado simulado local disparar ANTES do evento sintético chegar — o endpoint
  devolveu `{"alreadyFinished": true}` corretamente em vez de sobrescrever, exatamente como o
  endpoint de resultado já fazia.

### ✅ Testado ao vivo com túnel real + bug de timing corrigido (15/ago/2026)
Instalado `cloudflared` (túnel rápido, sem precisar de conta — `ngrok` exige login que eu não
tenho como criar por conta do André) e exposta a API local (`https://<subdomínio-aleatório>.
trycloudflare.com` → `localhost:5180`). Setado `SUMMIT_PUBLIC_API_URL` com essa URL e rodado o
ciclo completo de novo com `SUMMIT_MATCH_PROVIDER=aws` de verdade.

**Bug real encontrado na primeira tentativa**: passar `+matchzy_loadmatch_url "..."` direto na
linha de comando do CS2 (`+cvar` no boot) dispara ANTES do motor terminar de inicializar —
`[MatchZy] [LoadMatchFromURL - FATAL] An error occured: Entity system yet is not initialized`. O
fetch da config em si funcionou perfeitamente (o MatchZy chegou a baixar e logar o JSON certinho
pelo túnel), só a ordem de execução que estava errada.

**Fix**: `matchzy_loadmatch_url` não pode ir no boot — precisa ser mandado via **RCON** depois que
o servidor confirma pronto, igual ao padrão já usado pro pool quente:
- Boot agora sempre sobe com `+map`/`+sv_password` normais + `+rcon_password` (mesmo valor da senha
  da sala — só quando `SUMMIT_PUBLIC_API_URL` está setada).
- Novo campo `Match.MatchZyConfigLoaded` (`ALTER TABLE Matches ADD COLUMN ... TINYINT(1) DEFAULT 0`).
- `MatchServerService.TryLoadMatchZyConfigAsync`: conecta via RCON na sala e manda
  `matchzy_loadmatch_url "..."` — se falhar (CS2 ainda subindo), não marca como carregado.
- `ServerProvisionPoller` ampliado pra continuar re-tentando partidas `Ready` com
  `!MatchZyConfigLoaded` a cada tick (10s), não só as `Booting` — limitado a partidas ainda não
  `Finished` pra não ficar repolling pra sempre.

**Reteste completo, funcionou de ponta a ponta**: EC2 nova → boot normal → poller detecta Ready →
RCON manda `matchzy_loadmatch_url` → `[MatchZy] [LoadMatchFromJSON] Success with matchid: -1!`
(sem FATAL dessa vez) → `matchzy_remote_log_url` executado (confirmado no log do próprio servidor).
Instância de teste terminada logo depois, túnel derrubado, API voltou pro modo `local`.

**Ainda não provado**: o POST de verdade do MatchZy chegando na API quando uma partida REAL (jogada
por humanos até o fim) termina — só a parte de carregar o config foi validada ao vivo; o envio do
evento em si (`series_end`/`map_result`) foi validado separadamente só com payload sintético (ver
seção anterior), porque não dá pra simular 13 rounds vencidos sem jogadores de verdade conectados.
Também não coberto: caminho do pool "quente" (`BuildIdleUserData`) — o assignment via RCON não
carrega config do MatchZy ainda, só `changelevel`+`sv_password` direto; dá pra estender do mesmo
jeito (`rcon matchzy_loadmatch_url ...` na hora da atribuição), fica pra quando o pool voltar a ser
usado (`SUMMIT_POOL_SIZE > 0`). E pra produção de verdade, o túnel `cloudflared`/`ngrok` era só pra
teste — a API vai precisar estar hospedada num lugar com URL pública fixa.

### Comando de start validado (funcionando)
```bash
cd ~/cs2/game
export LD_LIBRARY_PATH="$PWD/bin/linuxsteamrt64:$PWD/csgo/bin/linuxsteamrt64:$LD_LIBRARY_PATH"
./bin/linuxsteamrt64/cs2 -dedicated -port 27015 \
  +sv_setsteamaccount SEU_GSLT +map de_mirage +sv_password SENHA
```
Resultado esperado: `SV: 64 player server started` + `CSource2Server::GameServerSteamAPIActivated()`.
