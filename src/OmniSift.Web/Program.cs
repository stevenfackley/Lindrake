// ============================================================
// OmniSift.Web — Blazor WASM Entry Point
// Configures HttpClient and services for the frontend
// ============================================================

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OmniSift.Web;
using OmniSift.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Empty ApiBaseUrl = same origin (the container renders it from API_BASE_URL).
var apiBaseAddress = ApiBaseAddress.Resolve(builder.Configuration["ApiBaseUrl"], builder.HostEnvironment.BaseAddress);

// Singleton: AuthHandler is created in the HTTP client factory's own scope and
// must share the components' auth state.
builder.Services.AddSingleton<AuthStateService>();
builder.Services.AddTransient<AuthHandler>();
builder.Services.AddHttpClient<OmniSiftApiClient>(client => client.BaseAddress = apiBaseAddress)
    .AddHttpMessageHandler<AuthHandler>();
builder.Services.AddSingleton<MarkdownService>();

var host = builder.Build();

// Hydrate the stored token before the first render so routing sees it.
await host.Services.GetRequiredService<AuthStateService>().InitializeAsync().ConfigureAwait(false);

await host.RunAsync().ConfigureAwait(false);
