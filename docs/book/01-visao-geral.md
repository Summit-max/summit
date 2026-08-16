[← Sumário](00-indice.md)

# Capítulo 1 — O que é o Summit

## 1.1 O produto

Summit é uma plataforma de campeonatos de Counter-Strike 2. Na prática, ela resolve o mesmo
problema que sites como FACEIT ou ESEA resolvem: dar a jogadores e times um lugar para se
organizar (perfil, time, amigos), se inscrever em campeonatos, disputar uma chave eliminatória
com vetos de mapa no estilo profissional, e jogar a partida num servidor dedicado que a própria
plataforma sobe automaticamente — sem que o jogador precise alugar ou configurar nada.

O sistema tem duas metades que conversam por HTTP:

1. **Um client desktop** (`Summit.csproj`), feito em WPF/.NET 8, que é a interface que o jogador
   realmente usa — telas de time, campeonatos, perfil, ranking, sala de partida.
2. **Uma API** (`Summit.Api/Summit.Api.csproj`), ASP.NET Core Minimal API, que guarda todo o
   estado real do mundo (banco de dados) e aplica todas as regras de negócio. O client nunca
   decide nada sozinho — ele só *pede* coisas à API e mostra o que ela responde.

Não existe (ainda) versão web ou mobile; o único jeito de usar o Summit hoje é rodando o
executável WPF em uma máquina Windows, apontando para uma API que pode estar rodando localmente
(`localhost:5180`, o padrão) ou em qualquer outro host via a variável de ambiente
`SUMMIT_API_URL`.

## 1.2 De onde veio o nome

O projeto começou com o nome "Wallbang" (um termo de CS2 — atirar através de uma parede) e foi
rebatizado para "Summit" no meio do desenvolvimento. Por isso o diretório do repositório no disco
ainda se chama `Wallbang`, mas todo o código, namespaces (`Summit.*`) e a marca já são "Summit".
Isso não é um bug nem uma inconsistência a corrigir — é só histórico, documentado aqui para que
ninguém estranhe o descompasso entre o nome da pasta e o nome do produto.

## 1.3 O que o sistema já faz de ponta a ponta (visão de produto)

Se você seguir a jornada completa de um jogador, hoje ela funciona assim:

1. Ele abre o client e faz login com a conta Steam (OpenID) — ou usa um modo demo sem Steam.
2. No primeiro login, um onboarding mínimo pede país e função principal (rifler, AWPer, IGL...).
3. Ele cria um time (vira dono automaticamente) ou é convidado/solicita entrada em um existente.
4. Dentro do time, dono e sublíder podem promover, rebaixar, transferir propriedade, remover
   jogador, editar dados do time ou excluí-lo.
5. O dono (ou sublíder) inscreve o time em um campeonato aberto, escolhendo os 5 jogadores da
   escalação e quem é o capitão da escalação daquela competição específica.
6. Quando o check-in abre (1h antes do início), o capitão confirma presença — quem não confirma
   é removido automaticamente 30 minutos antes do início.
7. A chave é gerada (eliminação simples ou dupla, qualquer quantidade de times) e as partidas da
   primeira rodada abrem o veto de mapas automaticamente, no formato configurado (MD1/MD3/MD5).
8. Ao fim do veto, a plataforma prepara uma sala com IP, senha e mapa — hoje já testado com um
   servidor CS2 + MatchZy real rodando na AWS, e com um "pool" de servidor sempre quente para que
   isso não demore minutos.
9. O jogador entra no servidor e joga.

O que **ainda não existe** é o que acontece depois disso: não há como o resultado da partida
voltar para a plataforma automaticamente, então a chave não avança sozinha, badges não são
concedidas por desempenho real, e o campeonato nunca se "encerra" sozinho. Isso está detalhado
no [Capítulo 21](21-feature-pos-partida-gaps.md) — é o maior buraco conhecido do sistema hoje, e
vale entender isso cedo porque explica por que várias telas do produto (estatísticas, ranking,
histórico de partidas) hoje só mostram dados de *seed* (dados de demonstração), não dados reais
gerados pelo uso.

## 1.4 Como ler este livro se você é novo no projeto

Se seu objetivo é só entender uma feature específica (ex.: "como funciona o veto de mapas?"),
vá direto para a Parte V — cada capítulo ali é autocontido e te manda de volta para os capítulos
de fundação (Parte II-IV) só quando precisa de um conceito que ainda não foi explicado.

Se seu objetivo é ganhar domínio geral do sistema para poder trabalhar nele com confiança, leia
as Partes I-IV em ordem primeiro — elas constroem o vocabulário (o que é um `BaseViewModel`, como
o `ApiDbContext` mapeia os modelos, como o `ApiClient` fala com a API) que todos os capítulos de
feature vão assumir que você já tem.
