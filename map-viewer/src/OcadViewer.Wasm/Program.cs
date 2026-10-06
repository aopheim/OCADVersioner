using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using OcadViewer.Core;
using OcadViewer.Wasm;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Scoped, not transient: the directory handle behind it lives as long as the page,
// and a component being disposed must not take the user's folder permission with it.
builder.Services.AddScoped<OcadViewer.Wasm.Services.ProjectService>();

// Comparing versions is deferred work. The table is built against this interface
// so it renders an honest empty state now and needs no reshaping when a real
// comparison arrives.
builder.Services.AddScoped<OcadViewer.Wasm.Services.IDiffSource,
                           OcadViewer.Wasm.Services.NoDiffSource>();

var host = builder.Build();

// Fonts have to be registered before the first map is parsed, not merely before
// it is drawn: the OCAD importer asks whether a face is installed while reading
// text symbols. Doing it here means no page has to remember to.
using (var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
    await FontPack.InstallFromHttpAsync(http);

await host.RunAsync();
