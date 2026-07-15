# Especificação Funcional — Fluxo Completo de Campeonatos

> Referência de desenvolvimento. Fluxo: Criação → Publicação → Inscrições → Fechamento (T-12h) → Check-in (T-1h) → Remoção de ausentes → Seed → Geração da chave → Partidas → Vetos → Servidor → Ao vivo → Finalização → Progressão → Final → Encerramento.

## 1. Criação do campeonato
Organizador configura:
- **Gerais:** nome, descrição, banner, jogo, região, data/hora de início, premiação, máx. de equipes, **mín. de equipes para iniciar**, entrada gratuita ou paga.
- **Formato:** Eliminação Simples | Eliminação Dupla (Upper+Lower) | Sistema Suíço.
- **Séries:** MD1 | MD3 | MD5 (futuro). Combinações: todas MD1; MD1 até semi + final MD3; todas MD3. A final pode ter configuração própria.
- **Map pool:** definido pelo organizador (ex.: Mirage, Inferno, Ancient, Anubis, Nuke, Dust2, Train).

## 2. Inscrições
Abrem na publicação. Equipe só se inscreve se: possui nº mínimo de jogadores; todos confirmados na equipe; nenhum banido; nenhum inscrito por outra equipe no mesmo campeonato. O capitão (dono/sublíder) inscreve → status **Inscrita**.

## 3. Fechamento das inscrições (T-12h)
Encerram automaticamente 12h antes do início. Depois: ninguém entra, sem troca/remoção de jogadores, inscrições bloqueadas. Lista congelada.

## 4. Check-in (T-1h)
Abre automaticamente 1h antes. Capitão confirma presença. Status: Aguardando Check-in | Check-in Confirmado | Não Compareceu. Ao terminar: equipes sem check-in são **removidas automaticamente**; sobrando vagas o campeonato continua e a chave é recalculada só com as presentes.

## 5. Seed
Só após o fim do check-in (nunca antes). Modos: manual (organizador) | por ranking Summit | aleatório.

## 6. Geração da chave
- **Eliminação simples:** só Upper; perdeu → eliminado.
- **Eliminação dupla:** Upper + Lower; perdeu na Upper → desce pra Lower; perdeu na Lower → eliminado. **Grande final:** reset de chave configurável (se o campeão da Lower vencer a primeira série).
- **Suíço:** sem chave fixa; a cada rodada, mesma campanha se enfrenta (1-0 vs 1-0, 0-1 vs 0-1...). Desempate Buchholz (configurável). X vitórias → classifica; Y derrotas → eliminado.

## 7. Publicação das partidas
Cada partida recebe: horário, rodada, adversários, formato (MD1/MD3), status.
**Status:** Aguardando → Vetos → Preparando servidor → Ao vivo → Finalizada.

## 8. Sistema de vetos (automático por formato; pool de 7)
- **MD1:** A ban → B ban → A ban → B ban → A ban → B ban → mapa restante é jogado.
- **MD3:** A ban → B ban → A pick → B pick → A ban → B ban → restante = **Decider**.
- **MD5:** A ban → B ban → A pick → B pick → A pick → B pick → restante.
- **Lados:** quem escolhe o mapa escolhe o lado inicial do adversário... (regra: quem picka o mapa, o outro escolhe? Não — quem escolhe o mapa escolhe também o lado inicial). No Decider: o **último veto** escolhe o lado inicial.
- Nenhum mapa repetido na série; banidos ficam indisponíveis a série toda; todos os mapas do pool configurado.

## 9. Preparação do servidor
Ao fim dos vetos, a plataforma sobe o servidor automaticamente: mapa correto, senha, IP, warmup, configs oficiais. Cada equipe recebe: IP, senha, mapa, horário e botão "Entrar no servidor".

## 10-11. Entrada e No-Show
Sistema monitora conectados/mínimo/tempo. Início manual (admin) ou automático. **No-show:** após tempo configurado (ex. 10 min) → W.O. → chave atualizada automaticamente.

## 12-13. Partida ao vivo e finalização
Servidor envia em tempo real: placar, rounds, mapa, jogadores, kills, deaths, MVPs, duração. Ao terminar: servidor envia resultado → sistema valida → atualiza vencedor/perdedor, chave, estatísticas, histórico.

## 14-16. Progressão, semi e final
Simples: vencedor avança, perdedor fora. Dupla: perdedor → Lower. Suíço: atualiza campanha e gera nova rodada automaticamente. Semi/final seguem a série configurada (ex. final MD3); vetos seguem o formato da série.

## 17. Encerramento
Campeonato → **Finalizado**. Registra campeão, vice, 3ºs; salva histórico permanente; atualiza estatísticas e ranking; distribui premiações (quando integrado).

## Regras gerais
- **Troca de jogadores:** só até o fechamento (T-12h); depois, equipe bloqueada.
- **Abandono:** derrota; conforme a fase, eliminação automática.
- **Desclassificação:** admins podem; chave recalculada.
- **Empates:** não existem; toda série tem vencedor.
- **Pause:** quantidade configurável por equipe (ex. 4×30s).
- **Overtime:** configurável (ex. MR3 $10.000 / MR6 $16.000).
- **Forfeit:** menos que o mínimo de jogadores após o tempo limite → W.O.
- **Administração:** editar placares, reiniciar partidas, reabrir vetos, recriar servidores, remarcar, punir, vitória administrativa, cancelar partida/campeonato. **Toda ação administrativa gera log de auditoria** (usuário, data/hora, ação, motivo).
