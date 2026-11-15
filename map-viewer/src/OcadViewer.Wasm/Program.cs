using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using OcadViewer.Core;
using OcadViewer.Wasm;
using OcadViewer.Wasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Scoped, not transient: the directory handle behind it lives as long as the page,
// and a component being disposed must not take the user's folder permission with it.
builder.Services.AddScoped<ProjectService>();

builder.Services.AddScoped<Localiser>();
builder.Services.AddScoped<AppSettingsService>();

// Comparing versions is deferred work. The table is built against this interface
// so it renders an honest empty state now and needs no reshaping when a real
// comparison arrives.
builder.Services.AddScoped<IDiffSource, NoDiffSource>();

builder.Services.AddScoped<TelemetryService>();

// Blazor reports an unhandled component exception through ILogger, so hooking the
// logging pipeline is how errors reach telemetry - the counterpart of Angular's
// ErrorHandlerService.
var telemetryLogging = new TelemetryLoggerProvider();
builder.Logging.AddProvider(telemetryLogging);

var host = builder.Build();

// The logger provider had to be registered before the container existed; give it
// the real one now so it reports through the same TelemetryService that starts
// below, not a copy.
telemetryLogging.Services = host.Services;

// Fonts have to be registered before the first map is parsed, not merely before
// it is drawn: the OCAD importer asks whether a face is installed while reading
// text symbols. Doing it here means no page has to remember to.
using (var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
    await FontPack.InstallFromHttpAsync(http);

// Settings before the first render, so the interface does not appear in English
// and then switch to the user's language a moment later.
await host.Services.GetRequiredService<AppSettingsService>().InitialiseAsync();

// Telemetry last, and never fatal: no key means no SDK is fetched at all.
await host.Services.GetRequiredService<TelemetryService>()
    .InitialiseAsync(builder.Configuration["Telemetry:ConnectionString"]);

await host.RunAsync();
