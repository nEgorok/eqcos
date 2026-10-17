using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace Eqcos.Web.Services;

// что сервер возвращает при входе
public record LoginResult(string Token, string Role, string FullName, bool HasEmail);

// помнит, кто вошёл, и прикладывает пропуск к запросам
public class AuthService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;   // доступ к браузеру (sessionStorage)

    public string? Token { get; private set; }
    public string? Role { get; private set; }
    public string? FullName { get; private set; }
    public bool HasEmail { get; private set; }   // привязана ли почта (нет — напоминаем)
    public bool IsLoggedIn => Token != null;

    public event Action? Changed;

    public AuthService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    // при запуске сайта: достать пропуск из sessionStorage, если он там есть
    public async Task Restore()
    {
        var token = await _js.InvokeAsync<string?>("sessionStorage.getItem", "token");
        if (token == null)
            return;

        Token = token;
        Role = await _js.InvokeAsync<string?>("sessionStorage.getItem", "role");
        FullName = await _js.InvokeAsync<string?>("sessionStorage.getItem", "fullName");
        HasEmail = await _js.InvokeAsync<string?>("sessionStorage.getItem", "hasEmail") == "1";
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    // вернёт null, если всё ок, или текст ошибки
    public async Task<string?> Login(string login, string password, bool staff = false)
    {
        var resp = await _http.PostAsJsonAsync("api/auth/login", new { login, password, staff });
        if (!resp.IsSuccessStatusCode)
            return await resp.Content.ReadAsStringAsync();

        var data = (await resp.Content.ReadFromJsonAsync<LoginResult>())!;
        Token = data.Token;
        Role = data.Role;
        FullName = data.FullName;
        HasEmail = data.HasEmail;

        // запомнить во вкладке, чтобы пережить обновление страницы
        await _js.InvokeVoidAsync("sessionStorage.setItem", "token", Token);
        await _js.InvokeVoidAsync("sessionStorage.setItem", "role", Role);
        await _js.InvokeVoidAsync("sessionStorage.setItem", "fullName", FullName);
        await _js.InvokeVoidAsync("sessionStorage.setItem", "hasEmail", HasEmail ? "1" : "0");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        Changed?.Invoke();
        return null;
    }

    // после изменения ФИО в профиле — обновить имя справа сверху
    public async Task SetFullName(string name)
    {
        FullName = name;
        await _js.InvokeVoidAsync("sessionStorage.setItem", "fullName", name);
        Changed?.Invoke();
    }
    // почту привязали — убрать напоминания
    public async Task SetHasEmail()
    {
        HasEmail = true;
        await _js.InvokeVoidAsync("sessionStorage.setItem", "hasEmail", "1");
        Changed?.Invoke();
    }

    // сменили пароль — сервер выдал новый пропуск (старые на других устройствах больше не действуют)
    public async Task SetToken(string token)
    {
        Token = token;
        await _js.InvokeVoidAsync("sessionStorage.setItem", "token", token);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task Logout()
    {
        Token = Role = FullName = null;
        HasEmail = false;
        await _js.InvokeVoidAsync("sessionStorage.clear");
        _http.DefaultRequestHeaders.Authorization = null;
        Changed?.Invoke();
    }
}