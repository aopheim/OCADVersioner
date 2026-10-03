using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using OcadViewer.Core;
using OcadViewer.Wasm;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

var host = builder.Build();

// Fonts have to be registered before the first map is parsed, not merely before
// it is drawn: the OCAD importer asks whether a face is installed while reading
// text symbols. Doing it here means no page has to remember to.
using (var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
    await FontPack.InstallFromHttpAsync(http);

await host.RunAsync();
