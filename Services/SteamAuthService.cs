using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Wallbang.Data;
using Wallbang.Models;

namespace Wallbang.Services;

public class SteamAuthService
{
    private const string OpenIdEndpoint = "https://steamcommunity.com/openid/login";
    private const string CallbackPath = "/auth/steam/callback/";
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(2);
    private static readonly Regex SteamIdRegex =
        new(@"^https?://steamcommunity\.com/openid/id/(\d{17})$", RegexOptions.Compiled);

    private readonly UserService _userService;
    private readonly UserRepository _userRepository;
    private readonly SteamWebApiClient _steamApi;

    public SteamAuthService(UserService userService, UserRepository userRepository, SteamWebApiClient steamApi)
    {
        _userService = userService;
        _userRepository = userRepository;
        _steamApi = steamApi;
    }

    public bool IsAuthenticated => _userService.CurrentUser != null;

    public async Task<User?> LoginWithSteamAsync()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}{CallbackPath}";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try { listener.Start(); }
        catch (HttpListenerException) { return null; }

        OpenInBrowser(BuildAuthUrl(prefix));

        using var cts = new CancellationTokenSource(LoginTimeout);
        HttpListenerContext ctx;
        try
        {
            var getContextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(getContextTask, Task.Delay(Timeout.Infinite, cts.Token));
            if (completed != getContextTask) return null;
            ctx = await getContextTask;
        }
        catch
        {
            return null;
        }

        var query = ctx.Request.QueryString;
        await RespondBrowserAsync(ctx, success: true);

        var claimedId = query["openid.claimed_id"];
        if (string.IsNullOrEmpty(claimedId)) return null;

        var match = SteamIdRegex.Match(claimedId);
        if (!match.Success) return null;
        var steamId64 = match.Groups[1].Value;

        if (!await ValidateWithSteamAsync(query)) return null;

        var summary = await _steamApi.GetPlayerSummaryAsync(steamId64);
        var nickname = summary?.PersonaName ?? $"Player_{steamId64[^4..]}";
        var avatar = summary?.AvatarUrl ?? string.Empty;

        var user = await _userRepository.UpsertFromSteamAsync(steamId64, nickname, avatar);
        _userService.SetCurrentUser(user);
        SessionStore.Save(steamId64);
        return user;
    }

    public async Task<User?> TryRestoreSessionAsync()
    {
        var steamId = SessionStore.Load();
        if (string.IsNullOrEmpty(steamId)) return null;

        var user = await _userRepository.GetBySteamIdAsync(steamId);
        if (user == null)
        {
            SessionStore.Clear();
            return null;
        }

        _ = RefreshSummaryAsync(user, steamId);

        _userService.SetCurrentUser(user);
        return user;
    }

    private async Task RefreshSummaryAsync(User user, string steamId)
    {
        try
        {
            var summary = await _steamApi.GetPlayerSummaryAsync(steamId);
            if (summary == null) return;

            var changed = false;
            if (!string.IsNullOrWhiteSpace(summary.PersonaName) && summary.PersonaName != user.Nickname)
            {
                user.Nickname = summary.PersonaName;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(summary.AvatarUrl) && summary.AvatarUrl != user.AvatarUrl)
            {
                user.AvatarUrl = summary.AvatarUrl;
                changed = true;
            }
            if (changed)
                await _userRepository.UpdateAsync(user);
        }
        catch
        {
        }
    }

    public async Task<User?> LoginDemoAsync()
    {
        await Task.Delay(400);

        var user = await _userRepository.UpsertFromSteamAsync(
            steamId: "76561198000000000",
            nickname: "xGhostFrag",
            avatarUrl: "");

        user.Rank = "Global Elite";
        user.Level = 42;
        user.Elo = 2400;
        user.WinRate = 0.64;
        user.KD = 1.47;
        user.HeadshotPercent = 0.52;
        user.AvgDamagePerRound = 84.3;
        user.TotalMatches = 380;
        user.TotalWins = 243;
        user.TotalKills = 8942;
        user.TotalDeaths = 6083;
        user.TotalAssists = 1540;
        user.Country = "🇧🇷";
        user.FavoriteMap = "Inferno";
        user.FavoriteWeapon = "AK-47";
        user.PrimaryRole = "Entry Fragger";
        user.Bio = "Rifler agressivo. Aqui pra ganhar.";

        await _userRepository.UpdateAsync(user);
        _userService.SetCurrentUser(user);
        SessionStore.Save(user.SteamId);
        return user;
    }

    public void Logout()
    {
        _userService.SetCurrentUser(null);
        SessionStore.Clear();
    }

    private static string BuildAuthUrl(string returnTo)
    {
        var realm = returnTo.Substring(0, returnTo.IndexOf('/', "http://".Length) + 1);
        var parameters = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnTo,
            ["openid.realm"] = realm,
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select"
        };

        var qs = string.Join("&", parameters
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{OpenIdEndpoint}?{qs}";
    }

    private static async Task<bool> ValidateWithSteamAsync(System.Collections.Specialized.NameValueCollection query)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var form = new Dictionary<string, string>();
        foreach (string? key in query)
        {
            if (string.IsNullOrEmpty(key)) continue;
            if (!key.StartsWith("openid.")) continue;
            form[key] = query[key] ?? string.Empty;
        }
        form["openid.mode"] = "check_authentication";

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var resp = await http.PostAsync(OpenIdEndpoint, content);
            if (!resp.IsSuccessStatusCode) return false;
            var body = await resp.Content.ReadAsStringAsync();
            return body.Contains("is_valid:true", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static async Task RespondBrowserAsync(HttpListenerContext ctx, bool success)
    {
        var html = success ? SuccessHtml : FailureHtml;
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.StatusCode = 200;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.OutputStream.Close();
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static int GetFreePort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    private const string SuccessHtml = @"<!doctype html>
<html><head><meta charset='utf-8'><title>Wallbang</title>
<style>
  body{margin:0;background:#0E0E10;color:#F5F5F5;font-family:Segoe UI,system-ui,sans-serif;
       display:flex;align-items:center;justify-content:center;height:100vh}
  .card{padding:40px 48px;border-radius:14px;border:1px solid #2A2A35;
        background:linear-gradient(135deg,#1E1208,#17171A);text-align:center;max-width:420px}
  h1{margin:0 0 10px;font-size:28px;background:linear-gradient(90deg,#FF6A00,#D7261E);
     -webkit-background-clip:text;color:transparent;font-weight:900;letter-spacing:-.5px}
  p{color:#9999AA;font-size:14px;line-height:1.5}
</style></head><body><div class='card'>
<h1>WALLBANG</h1>
<p>Autenticação concluída. Você já pode voltar para o app.</p>
</div></body></html>";

    private const string FailureHtml = @"<!doctype html>
<html><head><meta charset='utf-8'><title>Wallbang</title></head>
<body style='background:#0E0E10;color:#EF4444;font-family:Segoe UI'>
<div style='padding:40px;text-align:center'><h1>Falha na autenticação</h1></div>
</body></html>";
}
