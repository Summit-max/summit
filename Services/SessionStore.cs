using System.IO;
using System.Text.Json;

namespace Wallbang.Services;

public static class SessionStore
{
    private class SessionData
    {
        public string SteamId { get; set; } = string.Empty;
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    }

    private static string SessionFilePath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "Wallbang");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "session.json");
        }
    }

    public static void Save(string steamId)
    {
        try
        {
            var data = new SessionData { SteamId = steamId, SavedAt = DateTime.UtcNow };
            File.WriteAllText(SessionFilePath, JsonSerializer.Serialize(data));
        }
        catch
        {
        }
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(SessionFilePath)) return null;
            var json = File.ReadAllText(SessionFilePath);
            var data = JsonSerializer.Deserialize<SessionData>(json);
            return string.IsNullOrWhiteSpace(data?.SteamId) ? null : data.SteamId;
        }
        catch
        {
            return null;
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
