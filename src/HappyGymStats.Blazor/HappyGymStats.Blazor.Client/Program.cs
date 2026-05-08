using HappyGymStats.Blazor.Client.Crypto;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();
builder.Services.AddScoped<CryptoService>();

await builder.Build().RunAsync();
