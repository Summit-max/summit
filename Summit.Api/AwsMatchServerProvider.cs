namespace Summit.Api;

/// <summary>
/// Wrapper fino sobre o <see cref="MatchServerService"/> existente (RCON/AWS SDK reais, ver
/// docs/plano-aws.md) — só existe pra satisfazer <see cref="IMatchServerProvider"/>. Nenhuma
/// lógica nova aqui; toda a implementação real continua em MatchServerService.cs, intocada.
/// Só é usado quando SUMMIT_MATCH_PROVIDER=aws é definido explicitamente.
/// </summary>
public class AwsMatchServerProvider : IMatchServerProvider
{
    private readonly MatchServerService _inner;

    public AwsMatchServerProvider(MatchServerService inner) => _inner = inner;

    public Task ProvisionAsync(string matchId) => _inner.ProvisionAsync(matchId);

    public Task<bool> TryAssignFromPoolAsync(string matchId, string map, string password)
        => _inner.TryAssignFromPoolAsync(matchId, map, password);

    public Task TerminateAsync(string ec2InstanceId) => _inner.TerminateAsync(ec2InstanceId);
}
