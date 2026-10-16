using HotelPOS.Client.Services;
using HotelPOS.Web;
using HotelPOS.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The app is served from /app/ by the API; API, hub and images live at the same origin's root.
var origin = new Uri(new Uri(builder.HostEnvironment.BaseAddress).GetLeftPart(UriPartial.Authority) + "/");
var version = typeof(Program).Assembly.GetName().Version?.ToString(3);

builder.Services.AddScoped(_ => new HttpClient());
builder.Services.AddScoped<IClientHost>(_ => new BrowserClientHost(origin, version));
builder.Services.AddScoped<IBrowserStorage, JsBrowserStorage>();
builder.Services.AddScoped<IAlertSignal, JsAlertSignal>();
builder.Services.AddScoped<ISessionTransport, CookieSessionTransport>();
builder.Services.AddHotelPosClient();

var host = builder.Build();

// Restore the session from the refresh cookie before the first render so routes know who is signed in.
await host.Services.GetRequiredService<WebAuthState>().TryRestoreAsync();

await host.RunAsync();
