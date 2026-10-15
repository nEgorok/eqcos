using System.Net;

namespace Eqcos.Web.Services;

// "проходная" для всех запросов сайта к серверу.
// Каждый запрос HttpClient проходит через SendAsync: смотрим ответ и сообщаем ApiState.
public class ApiHandler : DelegatingHandler
{
    private readonly ApiState _state;

    public ApiHandler(ApiState state)
    {
        _state = state;
        InnerHandler = new HttpClientHandler();   // дальше запрос уходит в браузер как обычно
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            _state.SetOffline(true);   // сервер выключен или нет интернета
            throw;                     // ошибку отдаём дальше — её поймает страница или ErrorBoundary
        }

        _state.SetOffline(false);   // ответ пришёл — связь есть

        // 401 на запрос С пропуском = пропуск истёк или аккаунт удалён.
        // Неверный пароль при входе — тоже 401, но это не «истёк вход», его пропускаем.
        var isLogin = request.RequestUri?.AbsolutePath.EndsWith("/api/auth/login") == true;
        if (response.StatusCode == HttpStatusCode.Unauthorized && request.Headers.Authorization != null && !isLogin)
            _state.RaiseUnauthorized();

        return response;
    }
}