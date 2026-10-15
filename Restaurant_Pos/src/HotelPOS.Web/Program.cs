using HotelPOS.Web;
using HotelPOS.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The app is served from /app/ by the API; API, hub and images live at the same origin's root.
var origin = new Uri(new Uri(builder.HostEnvironment.BaseAddress).GetLeftPart(UriPartial.Authority) + "/");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = origin });

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<IBrowserStorage, JsBrowserStorage>();
builder.Services.AddScoped<DeviceIdentity>();
builder.Services.AddScoped<WebAuthState>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<WebAuthState>());
builder.Services.AddScoped<WebApiClient>();
builder.Services.AddScoped<IdempotentSender>();
builder.Services.AddScoped<DraftStore>();
builder.Services.AddScoped<WebRealtimeClient>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<ShellState>();

var host = builder.Build();

// Restore the session from the refresh cookie before the first render so routes know who is signed in.
await host.Services.GetRequiredService<WebAuthState>().TryRestoreAsync();

await host.RunAsync();
