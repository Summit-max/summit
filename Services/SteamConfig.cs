using System.IO;

namespace Summit.Services;

public static class SteamConfig
{
    private const string EnvVarName = "SUMMIT_STEAM_API_KEY";
    private const string FileName = "steam.config";

    public static string? GetApiKey()
    {
        var env = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(appData, "Summit", FileName);
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch
        {
        }

        return null;
    }

    public static string ConfigFilePath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "Summit", FileName);
        }
    }
}
