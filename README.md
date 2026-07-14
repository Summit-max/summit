# ▲ SUMMIT

Plataforma de campeonatos de CS2. Client desktop WPF + API ASP.NET + MySQL.

## Como rodar (dev)

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

## Estrutura

| Pasta | O que é |
|---|---|
| raiz (`Summit.csproj`) | Client desktop WPF (.NET 8) — login Steam, times, campeonatos, ranking |
| `Summit.Api/` | ASP.NET Minimal API — users, teams, tournaments, matches, friends, badges, ranking |
| `Models/` | Modelos compartilhados entre client e API |
| `database/schema.sql` | Schema MySQL para criar o banco em produção |
| `database/start-mysql.ps1` | Sobe o MySQL local de dev |
| `camp.txt` | Plano da infraestrutura AWS de partidas (servidores efêmeros) |

## Configuração

| Variável | Padrão | Descrição |
|---|---|---|
| `SUMMIT_API_URL` | `http://localhost:5180` | URL da API usada pelo client |
| `SUMMIT_DB` | connection string do `Summit.Api/appsettings.json` | MySQL da API |
| `SUMMIT_STEAM_API_KEY` | *(vazio)* | Steam Web API key (opcional — sem ela usa o perfil público XML) |

## Banco em produção

```bash
mysql -u root -e "CREATE DATABASE summit CHARACTER SET utf8mb4"
mysql -u root summit < database/schema.sql
# aponte a API:
# SUMMIT_DB=server=SEU_HOST;port=3306;database=summit;user=USUARIO;password=SENHA
```
