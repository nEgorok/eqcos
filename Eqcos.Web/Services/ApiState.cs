namespace Eqcos.Web.Services;

// состояние связи с сервером: есть ли связь и не истёк ли вход.
// Сообщает шапке сайта, а она показывает плашку или выводит на окно входа.
public class ApiState
{
    public bool Offline { get; private set; }   // true — сервер не отвечает

    public event Action? Changed;        // связь пропала или появилась
    public event Action? Unauthorized;   // сервер сказал «войдите снова» (401)

    public void SetOffline(bool value)
    {
        if (Offline == value)
            return;   // ничего не поменялось — не дёргаем шапку
        Offline = value;
        Changed?.Invoke();
    }

    public void RaiseUnauthorized() => Unauthorized?.Invoke();
}