using System.IO;
using System.Text.Json;

namespace Summit.Services;

public static class SessionStore
{
    private class SessionData
    {
        public string SteamId { get; set; } = string.Empty;
        public string? Token { get; set; }
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    }

    private static string SessionFilePath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "Summit");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "session.json");
        }
    }

    public static void Save(string steamId, string? token)
    {
        try
        {
            var data = new SessionData { SteamId = steamId, Token = token, SavedAt = DateTime.UtcNow };
            File.WriteAllText(SessionFilePath, JsonSerializer.Serialize(data));
        }
        catch
        {
        }
    }

    /// <summary>Sessão salva antes da autenticação por token vem sem Token (null) — quem
    /// consome deve tratar isso como "sem sessão válida", não crashar.</summary>
    public static (string? SteamId, string? Token) Load()
    {
        try
        {
            if (!File.Exists(SessionFilePath)) return (null, null);
            var json = File.ReadAllText(SessionFilePath);
            var data = JsonSerializer.Deserialize<SessionData>(json);
            return string.IsNullOrWhiteSpace(data?.SteamId) ? (null, null) : (data.SteamId, data.Token);
        }
        catch
        {
            return (null, null);
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(SessionFilePath))
                File.Delete(SessionFilePath);
        }
        catch
        {
        }
    }
}
