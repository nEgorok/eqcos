using Eqcos.Api.Data;                    // НОВОЕ
using Microsoft.EntityFrameworkCore;     // НОВОЕ
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// НОВОЕ: подключение к базе PostgreSQL
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
// проверка пропусков (JWT)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,          // просроченный пропуск не пускаем
            ValidateIssuerSigningKey = true,  // проверяем подпись
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });
builder.Services.AddAuthorization();
// кто проверяет новых студентов (сейчас вручную админом)
builder.Services.AddScoped<Eqcos.Api.Services.IStudentVerifier, Eqcos.Api.Services.ManualVerifier>();
// уборка при запуске и раз в час: забытые заявки, старые причины отклонения
builder.Services.AddHostedService<Eqcos.Api.Services.Cleanup>();
// письма (коды подтверждения, «пароль изменён»)
builder.Services.AddSingleton<Eqcos.Api.Services.Mailer>();
// SMS (выключатель "Sms:Enabled" в appsettings.json)
builder.Services.AddSingleton<Eqcos.Api.Services.SmsSender>();
// разрешаем сайту обращаться к API
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication();

// пропуск есть, а пользователя уже нет (аккаунт удалили в другой вкладке),
// его заблокировали или сменили пароль (версия входа выросла) —
// отвечаем «войдите снова» (401), а не пускаем дальше
app.Use(async (context, next) =>
{
    var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (int.TryParse(id, out var userId))
    {
        // у старых пропусков (выданных до шага 25) версии нет — считаем её 0
        var ver = int.TryParse(context.User.FindFirstValue("ver"), out var v) ? v : 0;
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        if (!await db.Users.AnyAsync(u => u.Id == userId && !u.Blocked && u.TokenVersion == ver))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Вход устарел. Войдите снова.");
            return;
        }
    }
    await next();
});

app.UseAuthorization();

app.MapControllers();

app.Run();