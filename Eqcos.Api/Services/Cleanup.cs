using Eqcos.Api.Data;
using Eqcos.Api.Models;

namespace Eqcos.Api.Services;

// уборка: при запуске сервера и потом раз в час.
// Задачи вида «должно было случиться — делаем», поэтому выключенный на день сервер ничего не пропустит.
public class Cleanup : BackgroundService
{
    public const int PendingDays = 14;    // заявку на регистрацию не рассмотрели за 2 недели — удаляем
    public const int RejectedDays = 30;   // сколько дней помнить причину отклонения

    private readonly IServiceScopeFactory _scopes;

    // сервис живёт всё время работы сервера, а база (AppDbContext) — короткая,
    // поэтому берём её на каждую уборку заново через "scope"
    public Cleanup(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                Run();
            }
            catch (Exception e)
            {
                // база недоступна и т.п. — сервер не падает, попробуем через час
                Console.WriteLine("Уборка не удалась: " + e.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stop));
    }

    private void Run()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        // 1. регистрации, которые никто не проверил за 2 недели (у старых учёток CreatedAt пустой — их не трогаем)
        var old = now.AddDays(-PendingDays);
        var forgotten = db.Users
            .Where(u => u.Role == "Student" && u.Status == "Pending" && u.CreatedAt != null && u.CreatedAt < old)
            .ToList();
        foreach (var u in forgotten)
        {
            db.RejectedAccounts.Add(new RejectedAccount
            {
                Login = u.Login,
                PasswordHash = u.PasswordHash,
                Reason = $"учебная часть не рассмотрела заявку за {PendingDays} дней",
                RejectedAt = now
            });
            db.Notifications.RemoveRange(db.Notifications.Where(n => n.UserId == u.Id));
            db.Users.Remove(u);
        }

        // 2. причины отклонения старше 30 дней больше не нужны
        var expired = now.AddDays(-RejectedDays);
        db.RejectedAccounts.RemoveRange(db.RejectedAccounts.Where(r => r.RejectedAt < expired));

        // 3. порядочность: закончились баны, прошли чистые сроки (точки в очереди и уведомления не ждут входа студента)
        foreach (var u in db.Users.Where(u => u.Level > 0 || u.PunishUntil != null).ToList())
            Conduct.Refresh(db, u);

        db.SaveChanges();
    }
}