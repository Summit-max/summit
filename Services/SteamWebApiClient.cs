using System.Net.Http;
using System.Text.Json;

namespace Summit.Services;

public class SteamPlayerSummary
{
    public string SteamId { get; set; } = string.Empty;
    public string PersonaName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string ProfileUrl { get; set; } = string.Empty;
}

public class SteamWebApiClient
{
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId64)
    {
        var apiKey = SteamConfig.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
            return null;

        var url =
            $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={apiKey}&steamids={steamId64}";

        try
        {
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("response", out var response)) return null;
            if (!response.TryGetProperty("players", out var players)) return null;
            if (players.ValueKind != JsonValueKind.Array || players.GetArrayLength() == 0) return null;

            var p = players[0];
            return new SteamPlayerSummary
            {
                SteamId = p.TryGetProperty("steamid", out var sid) ? sid.GetString() ?? string.Empty : string.Empty,
                PersonaName = p.TryGetProperty("personaname", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                AvatarUrl = p.TryGetProperty("avatarfull", out var a) ? a.GetString() ?? string.Empty : string.Empty,
                ProfileUrl = p.TryGetProperty("profileurl", out var pr) ? pr.GetString() ?? string.Empty : string.Empty
            };
        }
        catch
        {
            return null;
        }
    }
}
