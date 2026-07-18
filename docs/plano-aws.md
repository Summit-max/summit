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
