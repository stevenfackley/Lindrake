// ============================================================
// Lindrake.Web — Blazor WASM Entry Point
// Configures HttpClient and services for the frontend
// ============================================================

using Lindrake.Web;
using Lindrake.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Empty ApiBaseUrl = same origin (the container renders it from API_BASE_URL).
var apiBaseAddress = ApiBaseAddress.Resolve(builder.Configuration["ApiBaseUrl"], builder.HostEnvironment.BaseAddress);

// Singleton: AuthHandler is created in the HTTP client factory's own scope and
// must share the components' auth state.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AuthStateService>();
builder.Services.AddTransient<AuthHandler>();
builder.Services.AddHttpClient<LindrakeApiClient>(client => client.BaseAddress = apiBaseAddress)
    .AddHttpMessageHandler<AuthHandler>();
builder.Services.AddSingleton<MarkdownService>();

var host = builder.Build();

// Hydrate the stored token before the first render so routing sees it.
await host.Services.GetRequiredService<AuthStateService>().InitializeAsync().ConfigureAwait(false);

await host.RunAsync().ConfigureAwait(false);
