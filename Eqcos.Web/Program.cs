using Eqcos.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<Eqcos.Web.Services.ApiState>();
// все запросы к серверу идут через ApiHandler: он замечает «нет связи» и «вход истёк»
builder.Services.AddScoped(sp => new HttpClient(new Eqcos.Web.Services.ApiHandler(sp.GetRequiredService<Eqcos.Web.Services.ApiState>()))
{
    BaseAddress = new Uri("https://localhost:7227/")
});
builder.Services.AddScoped<Eqcos.Web.Services.AuthService>();
var host = builder.Build();
await host.Services.GetRequiredService<Eqcos.Web.Services.AuthService>().Restore();   // вспомнить вход
await host.RunAsync();
