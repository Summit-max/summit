using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Summit.Api;

/// <summary>
/// Emissão e leitura de tokens JWT da Summit API (Fase A do plano de produção —
/// docs/plano-aws.md não cobre isso, é o começo da "adequação" de segurança).
/// Segredo via SUMMIT_JWT_SECRET (mesmo padrão de SUMMIT_GSLT em MatchServerService.cs).
/// ATENÇÃO: o fallback abaixo é só pra dev local — precisa ir pro Secrets Manager
/// (ou equivalente) antes de qualquer deploy real, senão qualquer um forja token.
/// </summary>
public static class SummitAuth
{
    public const string Issuer = "summit-api";
    public const string Audience = "summit-client";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);

    private static string Secret =>
        Environment.GetEnvironmentVariable("SUMMIT_JWT_SECRET")
        ?? "summit-dev-insecure-jwt-secret-DO-NOT-USE-IN-PROD";

    public static SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Secret));

    public static string GenerateToken(string userId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim("uid", userId)
        };
        var creds = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(TokenLifetime),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Id do usuário autenticado, lido das claims do token (null se não autenticado).</summary>
    public static string? GetUserId(HttpContext ctx) =>
        ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? ctx.User.FindFirst("uid")?.Value;
}
