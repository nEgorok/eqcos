using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Eqcos.Web.Services;

// что сервер возвращает при входе
public record LoginResult(string Token, string Role, string FullName);

// помнит, кто вошёл, и прикладывает пропуск к запросам
public class AuthService
{
    private readonly HttpClient _http;

    public string? Token { get; private set; }
    public string? Role { get; private set; }
    public string? FullName { get; private set; }
    public bool IsLoggedIn => Token != null;

    public event Action? Changed;   // "сообщить странице, что вход/выход произошёл"

    public AuthService(HttpClient http)
    {
        _http = http;
    }

    // вернёт null, если всё ок, или текст ошибки
    public async Task<string?> Login(string login, string password)
    {
        var resp = await _http.PostAsJsonAsync("api/auth/login", new { login, password });
        if (!resp.IsSuccessStatusCode)
            return await resp.Content.ReadAsStringAsync();

        var data = (await resp.Content.ReadFromJsonAsync<LoginResult>())!;
        Token = data.Token;
        Role = data.Role;
        FullName = data.FullName;

        // теперь каждый запрос пойдёт с пропуском
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        Changed?.Invoke();
        return null;
    }

    public void Logout()
    {
        Token = Role = FullName = null;
        _http.DefaultRequestHeaders.Authorization = null;
        Changed?.Invoke();
    }
}