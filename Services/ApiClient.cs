using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Summit.Services;

/// <summary>
/// Cliente HTTP central da Summit API.
/// Base URL configurável via variável de ambiente SUMMIT_API_URL
/// (padrão: instância de produção na AWS; setar SUMMIT_API_URL=http://localhost:5180
/// pra voltar a testar contra a API local).
/// </summary>
public static class ApiClient
{
    public static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("SUMMIT_API_URL")?.TrimEnd('/')
        ?? "http://18.231.129.187";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>Anexa (ou limpa, com null) o Bearer token em toda requisição futura —
    /// chamado depois do login/restauro de sessão e no logout.</summary>
    public static void SetAuthToken(string? token)
    {
        Http.DefaultRequestHeaders.Authorization =
            string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>GET tolerante: retorna default se a API estiver fora ou responder erro.</summary>
    public static async Task<T?> GetAsync<T>(string path)
    {
        try
        {
            using var resp = await Http.GetAsync(path);
            if (!resp.IsSuccessStatusCode) return default;
            return await resp.Content.ReadFromJsonAsync<T>(Json);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>POST tolerante: retorna default em falha.</summary>
    public static async Task<T?> PostAsync<T>(string path, object? body)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync(path, body, Json);
            if (!resp.IsSuccessStatusCode) return default;
            return await resp.Content.ReadFromJsonAsync<T>(Json);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>POST que exige sucesso — lança exceção com mensagem clara (usado no login).</summary>
    public static async Task<T> PostRequiredAsync<T>(string path, object? body)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await Http.PostAsJsonAsync(path, body, Json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Não foi possível conectar à Summit API em {BaseUrl}. A API está rodando?", ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Summit API respondeu {(int)resp.StatusCode} em {path}.");
            var result = await resp.Content.ReadFromJsonAsync<T>(Json);
            return result ?? throw new InvalidOperationException($"Resposta vazia da Summit API em {path}.");
        }
    }

    /// <summary>POST cujo resultado relevante é apenas sucesso/falha (ou um bool no corpo).</summary>
    public static async Task<bool> PostBoolAsync(string path, object? body = null)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync(path, body, Json);
            if (!resp.IsSuccessStatusCode) return false;
            var text = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text)) return true;
            return !text.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<T?> PutAsync<T>(string path, object? body)
    {
        try
        {
            using var resp = await Http.PutAsJsonAsync(path, body, Json);
            if (!resp.IsSuccessStatusCode) return default;
            return await resp.Content.ReadFromJsonAsync<T>(Json);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>PUT cujo erro precisa ser mostrado ao usuário (a API retorna BadRequest(string) com o motivo).</summary>
    public static async Task<(bool Ok, string? Message)> PutWithMessageAsync(string path, object? body)
    {
        try
        {
            using var resp = await Http.PutAsJsonAsync(path, body, Json);
            var text = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode) return (true, null);
            return (false, string.IsNullOrWhiteSpace(text) ? null : text.Trim().Trim('"'));
        }
        catch
        {
            return (false, "Erro de conexão.");
        }
    }

    /// <summary>POST cujo erro precisa ser mostrado ao usuário (mesma ideia de PutWithMessageAsync),
    /// mas devolve também o objeto criado quando dá certo — usado na criação de campeonato (RF-09).</summary>
    public static async Task<(bool Ok, T? Result, string? Message)> PostWithMessageAsync<T>(string path, object? body)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync(path, body, Json);
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<T>(Json);
                return (true, result, null);
            }
            var text = await resp.Content.ReadAsStringAsync();
            return (false, default, string.IsNullOrWhiteSpace(text) ? null : text.Trim().Trim('"'));
        }
        catch
        {
            return (false, default, "Erro de conexão.");
        }
    }

    public static async Task<bool> DeleteBoolAsync(string path)
    {
        try
        {
            using var resp = await Http.DeleteAsync(path);
            if (!resp.IsSuccessStatusCode) return false;
            var text = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text)) return true;
            return !text.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>DELETE cujo erro precisa ser mostrado ao usuário (mesma ideia de PutWithMessageAsync).</summary>
    public static async Task<(bool Ok, string? Message)> DeleteWithMessageAsync(string path)
    {
        try
        {
            using var resp = await Http.DeleteAsync(path);
            var text = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode) return (true, null);
            return (false, string.IsNullOrWhiteSpace(text) ? null : text.Trim().Trim('"'));
        }
        catch
        {
            return (false, "Erro de conexão.");
        }
    }
}
