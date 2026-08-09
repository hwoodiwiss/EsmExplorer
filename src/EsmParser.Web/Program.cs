using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EsmParser.Web;
using EsmParser.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<PluginExplorerState>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<GameDataService>();
builder.Services.AddScoped<ModelViewerState>();
builder.Services.AddScoped<NifViewer.Blazor.NifViewerInterop>();

await builder.Build().RunAsync();
