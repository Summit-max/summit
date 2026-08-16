namespace Summit.Api;

/// <summary>
/// Abstração de "conseguir um servidor pra partida" — ver docs/spec/summit-fase-final/plan.md
/// RF-00. Duas implementações: <see cref="AwsMatchServerProvider"/> (real, fala com a AWS) e
/// <see cref="LocalSimulatedMatchServerProvider"/> (padrão — não toca em AWS, simula o ciclo de
/// vida inteiro da partida localmente). Escolhida em Program.cs via SUMMIT_MATCH_PROVIDER
/// ("local" por padrão, "aws" pra ligar a de verdade).
/// </summary>
public interface IMatchServerProvider
{
    Task ProvisionAsync(string matchId);
    Task<bool> TryAssignFromPoolAsync(string matchId, string map, string password);
}
