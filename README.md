# ▲ SUMMIT

Plataforma de campeonatos de CS2. Client desktop WPF + API ASP.NET + MySQL/Aurora + servidores de partida efêmeros na AWS.

Este documento é o roteiro pra quem está pegando o código pela primeira vez: como rodar local, como tudo se conecta, onde estão as pegadinhas, e o que falta.

---

## 1. As três peças

```
┌─────────────────┐        HTTPS/JSON        ┌──────────────────┐
│  Client WPF      │ ────────────────────────▶│  Summit.Api       │
│  (roda no PC do   │◀──────────────────────── │  (ASP.NET         │
│  jogador)         │        JWT Bearer         │  Minimal API)     │
└─────────────────┘                           └─────────┬────────┘
                                                          │ EF Core (Pomelo)
                                                          ▼
                                               ┌──────────────────┐
                                               │  MySQL / Aurora   │
                                               └──────────────────┘
                                                          │
                                                          │ AWS SDK (EC2)
                                                          ▼
                                               ┌──────────────────┐
                                               │  Servidores CS2   │
                                               │  efêmeros (por    │
                                               │  partida)          │
                                               └──────────────────┘
```

- **Client** (`Summit.csproj`, raiz) — WPF/.NET 8, MVVM. Login via Steam, times, campeonatos, ranking, veto ao vivo, sala de partida. Não fala com o banco diretamente — tudo passa pela API.
- **Summit.Api** (`Summit.Api/`) — ASP.NET Minimal API. Autenticação, regras de campeonato/time, orquestra os servidores de partida na AWS.
- **Models/** — compartilhado entre client e API via link de compilação (`<Compile Include="..\Models\*.cs">` no `.csproj` de cada um). Mudou um model, os dois lados enxergam.

---

## 2. Rodando local (dev)

```powershell
# 1. Banco (MySQL local em localhost:3306, root sem senha)
powershell -File database\start-mysql.ps1

# 2. API (http://localhost:5180) — cria as tabelas e o seed sozinha
cd Summit.Api
dotnet run

# 3. Client WPF
cd ..
dotnet run --project Summit.csproj
```

Sem `SUMMIT_DB` configurada, a API cai pra SQLite local (`Summit.Api/summit-api.db`) — dá pra testar sem MySQL instalado, só que sem paridade completa com produção (Pomelo/MySQL tem diferenças sutis de tipos/collation).

Pra habilitar o provisionamento real de servidor de partida em dev, ver `SUMMIT_MATCH_PROVIDER` na seção 4.

---

## 3. Autenticação

Login é **Steam OpenID de verdade** (`Services/SteamAuthService.cs`), verificado contra os servidores da Steam — isso nunca foi simulado. Depois de verificar:

1. Client chama `POST /api/users/steam-login` com o `steamId` validado.
2. API cria/atualiza o `User` e emite um **JWT** (`Summit.Api/SummitAuth.cs`, HMAC-SHA256, 30 dias de validade).
3. Client guarda o token (`Services/SessionStore.cs`) e anexa `Authorization: Bearer` em toda chamada (`Services/ApiClient.cs`).
4. Endpoints sensíveis usam `.RequireAuthorization()` + `SummitAuth.GetUserId(ctx)` — nunca confiam num `userId` que veio solto no corpo/query da requisição.

**Segredo do JWT**: env var `SUMMIT_JWT_SECRET`. Sem ela, cai num valor de dev inseguro hardcoded (só serve pra rodar local — nunca usar isso em produção).

---

## 4. Variáveis de ambiente

| Variável | Onde é usada | Padrão | Descrição |
|---|---|---|---|
| `SUMMIT_API_URL` | Client | `http://<IP atual da API>` (ver `Services/ApiClient.cs`) | Pra onde o client aponta. Setar `http://localhost:5180` pra testar contra a API local |
| `SUMMIT_DB` | API | *(vazio → SQLite)* | Connection string MySQL/Aurora |
| `SUMMIT_JWT_SECRET` | API | valor de dev inseguro | Segredo de assinatura do JWT — **obrigatório trocar em produção** |
| `PORT` | API | `5180` | Porta que o Kestrel escuta. Produção usa `80` |
| `SUMMIT_ENABLE_DEBUG` | API | `false` | Liga as ~30 rotas `/api/debug/*` (ver seção 7). **Nunca deixar `true` em produção fora de uma sessão de manutenção ativa** |
| `SUMMIT_MATCH_PROVIDER` | API | `local` | `local` = simula servidor de partida sem AWS; `aws` = provisiona EC2 de verdade |
| `AWS_REGION` | API | `sa-east-1` | Região dos recursos AWS |
| `SUMMIT_AMI_ID` | API | — | AMI do servidor CS2 (Ubuntu + CS2 + MatchZy pré-instalados) |
| `SUMMIT_SECURITY_GROUP_ID` | API | — | Security group dos servidores de partida (CS2) |
| `SUMMIT_KEY_PAIR_NAME` | API | — | Key pair EC2 pra SSH nos servidores de partida |
| `SUMMIT_GSLT` | API | — | Game Server Login Token da Steam, necessário pro CS2 dedicated server |
| `SUMMIT_PUBLIC_API_URL` | API | — | URL pública da própria API — os servidores de partida usam isso pra buscar a config do MatchZy via RCON. **Sem essa variável, o RCON dos servidores provisionados diretamente nem recebe senha** |

---

## 5. Banco de dados — a pegadinha mais importante

**Não tem migrations.** O `ApiDbContext` usa `db.Database.EnsureCreated()` no boot (`Program.cs`). Isso significa:

- Banco vazio → cria o schema inteiro sozinho, sem precisar de `mysql` CLI nem scripts.
- Banco que **já existe** → `EnsureCreated()` não faz nada, mesmo que você tenha adicionado uma propriedade nova num model.

**Se você adicionar um campo a um model existente**, precisa aplicar `ALTER TABLE` manualmente no banco de produção. Não tem cliente MySQL instalado por padrão nem localmente nem no servidor — instale com `sudo apt-get install -y mysql-client` (Ubuntu) e conecte:

```bash
mysql -h <endpoint-aurora> -u admin -p'<senha>' --ssl-mode=REQUIRED summit -e "ALTER TABLE Matches ADD COLUMN NovoCampo longtext NOT NULL;"
```

Tabela nova (classe nova em `Models/` + `DbSet` novo no `ApiDbContext`) tem o mesmo problema — banco existente não ganha a tabela sozinho, precisa de `CREATE TABLE` manual.

---

## 6. Produção — onde tudo mora hoje

| Peça | Onde | Detalhe |
|---|---|---|
| **API** | EC2 `t3.micro`, Ubuntu 24.04, `sa-east-1` | Roda como serviço systemd (`summit-api.service`) na porta 80, reinicia sozinho se cair. Chaves AWS estáticas no `/etc/summit-api.env` por ora (pendência: trocar por IAM role — ver seção 9) |
| **Banco** | Aurora Serverless v2 MySQL-compatible, `sa-east-1` | `min=0 / max=1 ACU`, pausa sozinho depois de ~5min ocioso. Publicamente acessível (decisão consciente — mesma região que a API, mas processo de deploy foi feito manual, sem VPC Connector) |
| **Servidores de partida** | EC2 `c5.large` sob demanda, `sa-east-1` | Sobem por partida (pool "quente" + fallback de provisionamento direto), terminam sozinhos ao fim |
| **Código-fonte** | GitHub privado — `github.com/Summit-max/summit` | — |

### Deploy manual (o que existe hoje)

```bash
# 1. empacota só os arquivos versionados
git archive HEAD -o deploy.tar.gz --format=tar.gz

# 2. copia e extrai no servidor
scp -i <chave.pem> deploy.tar.gz ubuntu@<ip-da-api>:/tmp/
ssh -i <chave.pem> ubuntu@<ip-da-api> "tar -xzf /tmp/deploy.tar.gz -C /opt/summit-api/src"

# 3. publica e reinicia
ssh -i <chave.pem> ubuntu@<ip-da-api> "cd /opt/summit-api/src/Summit.Api && dotnet publish -c Release -o /opt/summit-api/publish && sudo systemctl restart summit-api"
```

### Deploy automático (parcialmente configurado)

Existe um workflow (`.github/workflows/deploy-api.yml`) que builda e publica via SSH a cada push que toque `Summit.Api/`. **Precisa dos secrets `EC2_SSH_KEY` e `EC2_HOST` cadastrados no GitHub** (Settings → Secrets → Actions) pra funcionar — sem eles, o workflow roda e falha no passo de SSH.

---

## 7. Ferramentas de debug (`/api/debug/*`)

Só existem com `SUMMIT_ENABLE_DEBUG=true` — sem essa env var, as rotas nem são mapeadas (404, não só bloqueadas). Cobrem praticamente todo o ciclo de teste sem precisar de múltiplos jogadores reais:

- **Times/torneios de teste**: `reset-test-teams`, `create-test-tournament`, `bracket/{tournamentId}`, `restart-veto/{bracketMatchId}`
- **Ciclo de campeonato acelerado**: `force-register`, `force-checkin`, `generate-bracket`, `simulate-veto`, `add-ghost-teams`, `set-tournament-date`
- **Infraestrutura AWS**: `instances`, `security-groups`, `authorize-ip/{cidr}`, `my-amis`, `create-security-group`, `launch-api-instance`, `retry-provision`, `rds-cost`
- **RCON ad-hoc**: `POST /api/debug/rcon` — manda qualquer comando pro servidor de um IP+senha dados

**Nunca fazer deploy com essa flag ligada** — reabre ~30 rotas poderosas sem autenticação numa instância pública. O padrão é: ligar só durante uma sessão de manutenção, desligar (e reiniciar o serviço) assim que terminar.

---

## 8. Ciclo de vida de um campeonato

1. **Inscrição** (`POST /api/tournaments/{id}/register`) — fecha sozinha em T-12h.
2. **Check-in** (`POST /api/tournaments/{id}/checkin`) — janela T-1h → T-30min.
3. **`LifecycleWorker`** (tick 20s, `Summit.Api/LifecycleWorker.cs`) — remove quem não fez check-in, gera a chave em T-30min, e no T-0 marca o campeonato ao vivo e abre o veto da 1ª rodada.
4. **Veto** — ao vivo, turno a turno (`Data/VetoRepository.cs` no client, `CompetitionEndpoints.cs` na API). Times sem capitão real (id de seed) são vetados automaticamente pelo próprio `LifecycleWorker`.
5. **Servidor de partida** — assim que o veto fecha, `ProvisionRoomAsync` tenta puxar do pool "quente" (`PoolManagerService.cs`, tick 30s) e, se não tiver disponível, provisiona uma instância dedicada na hora (`MatchServerService.ProvisionAsync`). `ServerProvisionPoller` (tick 10s) confirma o IP e carrega a config do MatchZy via RCON.
6. **Resultado** — MatchZy manda webhook pro fim da partida; a API avança a chave (`CompetitionEndpoints.AdvanceBracketAsync`) e libera o servidor de volta pro pool.

**Pegadinha de rede**: API e servidores de partida ficam na mesma VPC/região. RCON e qualquer chamada "interna" da API pra um servidor **tem que usar o IP privado**, não o público — conectar via IP público de dentro da própria VPC não funciona (mesmo motivo por trás da configuração do Aurora). `PoolServer.PrivateIp` e `Match.ServerPrivateIp` existem especificamente pra isso; `PublicIp`/`ServerIp` continuam sendo os certos pro connect string do jogador.

---

## 9. Dívidas técnicas conhecidas (conscientes, não esquecidas)

- **Sem HTTPS** — API roda em HTTP puro. Falta domínio (`summitcs.com.br`) apontar via DNS + Caddy/certbot pra ter TLS de verdade. Token JWT trafega sem criptografia até lá.
- **Aurora publicamente acessível** — decisão consciente pra simplificar enquanto o time é só o André. Reconsiderar quando o projeto tiver usuários reais (VPC Connector ou peering se a API for pra outra região).
- **Chaves AWS estáticas no servidor da API** — em vez de IAM role. O usuário IAM `summit-api` não tem permissão de criar roles de propósito (evita escalada de privilégio caso a chave vaze). Trocar assim que a role for criada manualmente no console.
- **Uma instância só de API** — sem auto-scaling horizontal. Teto real de escala; migrar pra ECS Express Mode (ou similar) quando o tráfego justificar.
- **Kill-switch de teste** — `GET /api/app/status` (tabela `AppConfigs`, linha `singleton`). Serve pra desativar remotamente builds distribuídas pra testadores externos sem precisar redistribuir nada — útil pra qualquer rodada de teste fechado futura, não só a de agosto/2026.

---

## 10. Estrutura de pastas

| Pasta | O que é |
|---|---|
| raiz (`Summit.csproj`) | Client desktop WPF |
| `Summit.Api/` | API ASP.NET Minimal API |
| `Models/` | Modelos compartilhados client+API |
| `ViewModels/`, `Views/`, `Services/`, `Data/`, `Commands/`, `Components/`, `Helpers/`, `Resources/` | Client (MVVM) |
| `database/schema.sql` | Dump de referência do schema MySQL |
| `docs/plano-aws.md` | Plano original da infra de servidores de partida efêmeros |
| `docs/book/` | Documentação de referência mais profunda (arquitetura, cada feature, classes) — bom pra consulta pontual, este README é o ponto de partida |
| `.github/workflows/deploy-api.yml` | Deploy automático (ver seção 6) |

---

## 11. Convenções de código

- **Client**: MVVM puro, sem framework de DI — instâncias globais estáticas em `App.xaml.cs` (`App.UserService`, `App.TeamService`, etc.).
- **API**: Minimal API, sem controllers. Lógica de domínio mais complexa fica em `CompetitionEndpoints.cs` (métodos estáticos), endpoints simples ficam inline em `Program.cs`.
- **Comentários**: só quando explicam o *porquê* (uma decisão não óbvia, uma pegadinha, um bug já corrigido) — não descrevem o óbvio.
- **Sem testes automatizados ainda** — verificação é manual, via client real ou ferramentas de debug.
