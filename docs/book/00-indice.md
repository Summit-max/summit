# SUMMIT — O Livro do Sistema

### Documentação técnica completa da plataforma de campeonatos de CS2

> Este livro documenta o sistema Summit (ex-Wallbang) de ponta a ponta: o client desktop WPF,
> a API ASP.NET Core, o banco de dados MySQL, e a lógica de cada feature — para que um
> desenvolvedor intermediário que nunca viu o projeto consiga, lendo isto, ganhar domínio real
> sobre como o sistema é construído e por quê. Não cobre configuração de infraestrutura AWS
> (isso já está em `docs/plano-aws.md`) — mas cobre a *lógica* de como o pool de servidores
> funciona, porque essa lógica é parte do desenho do sistema, não da infraestrutura em si.
>
> Todo código mostrado aqui é o código real do projeto, com caminho de arquivo e trecho exato,
> a menos que esteja explicitamente marcado como "exemplo genérico" para ilustrar um conceito.

---

## Como este livro está organizado

O livro tem seis partes. As Partes I-IV constroem, camada por camada, o entendimento de como o
sistema é montado (arquitetura → banco → client → API). A Parte V pega cada *feature* do produto
e explica, de ponta a ponta, como ela atravessa todas essas camadas — é a parte mais longa e a
mais importante para quem vai *trabalhar* no sistema no dia a dia. A Parte VI é referência pura:
um dicionário de toda classe do projeto, para consulta rápida.

---

## Parte I — Visão Geral e Fundamentos

- [Capítulo 1 — O que é o Summit](01-visao-geral.md)
- [Capítulo 2 — Arquitetura Geral](02-arquitetura.md)
- [Capítulo 3 — Padrões de Projeto Usados](03-padroes-projeto.md)

## Parte II — Banco de Dados

- [Capítulo 4 — Modelo de Dados Completo](04-banco-dados.md)

## Parte III — Client WPF

- [Capítulo 5 — MVVM na Prática](05-client-mvvm.md)
- [Capítulo 6 — Navegação e Comunicação com a API](06-client-navegacao-api.md)
- [Capítulo 7 — Modelos do Client](07-client-models.md)
- [Capítulo 8 — Services e Repositórios do Client](08-client-services-repos.md)

## Parte IV — Backend (Summit.Api)

- [Capítulo 9 — Program.cs e o Bootstrap da API](09-backend-api-program.md)
- [Capítulo 10 — Endpoints por Domínio](10-backend-endpoints.md)
- [Capítulo 11 — Services e Background Workers](11-backend-services-workers.md)

## Parte V — Features de Ponta a Ponta

- [Capítulo 12 — Conta, Login e Perfil](12-feature-conta-login.md)
- [Capítulo 13 — Times](13-feature-times.md)
- [Capítulo 14 — Amizades](14-feature-amizades.md)
- [Capítulo 15 — Auditoria](15-feature-auditoria.md)
- [Capítulo 16 — Campeonatos: Inscrição e Check-in](16-feature-campeonatos-inscricao.md)
- [Capítulo 17 — Escalação (Lineup)](17-feature-escalacao.md)
- [Capítulo 18 — Chave (Bracket)](18-feature-bracket.md)
- [Capítulo 19 — Veto de Mapas e Sala da Partida](19-feature-veto.md)
- [Capítulo 20 — Pool de Servidores CS2 (a lógica, não a infra)](20-feature-pool-servidores.md)
- [Capítulo 21 — Pós-Partida: o Que Ainda Não Existe](21-feature-pos-partida-gaps.md)

## Parte VI — Referência de Classes

- [Capítulo 22 — Referência: Client (Models, ViewModels, Views, Services)](22-referencia-classes-client.md)
- [Capítulo 23 — Referência: API (Endpoints, Services, DbContext)](23-referencia-classes-api.md)

## Apêndices

- [Apêndice A — Glossário](24-apendices.md#apêndice-a--glossário)
- [Apêndice B — Convenções de Código](24-apendices.md#apêndice-b--convenções-de-código)
- [Apêndice C — Roadmap Consolidado](24-apendices.md#apêndice-c--roadmap-consolidado)

---

## Mapa mental rápido (para quem vai ler correndo)

```
┌─────────────────────┐         HTTP/JSON         ┌──────────────────────┐
│   Summit (client)   │ ────────────────────────▶ │     Summit.Api       │
│   WPF .NET 8, MVVM   │ ◀──────────────────────── │  ASP.NET Minimal API │
└─────────────────────┘                            └──────────┬───────────┘
                                                                │ EF Core
                                                     ┌──────────▼───────────┐
                                                     │   MySQL (ou SQLite   │
                                                     │   local em dev)      │
                                                     └───────────────────────┘

Summit.Api também roda 3 BackgroundServices em paralelo:
  • LifecycleWorker       — motor do ciclo de vida do campeonato (tick 20s)
  • ServerProvisionPoller — acompanha EC2 de cold-boot (tick 10s)
  • PoolManagerService    — mantém o pool de servidores CS2 quentes (tick 30s)
```

Os dois projetos (`Summit.csproj` e `Summit.Api/Summit.Api.csproj`) vivem no mesmo repositório e
**compartilham a pasta `Models/`** via link direto de arquivos (`<Compile Include="..\Models\*.cs"
LinkBase="Models" />` no `.csproj` da API) — não é um pacote NuGet separado, é literalmente o
mesmo arquivo `.cs` compilado nos dois projetos. Isso significa que uma classe como `Tournament`
tem exatamente o mesmo formato dos dois lados da rede, o que elimina uma classe inteira de bugs
de serialização/desserialização divergente que normalmente aparece quando client e servidor têm
modelos "parecidos, mas não iguais".
