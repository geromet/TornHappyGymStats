using HappyGymStats.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new HealthResponse(
        Status: "ok",
        Api: "HappyGymStats.Api",
        DataAssembly: typeof(DataAssemblyMarker).Assembly.GetName().Name ?? "HappyGymStats.Data")))
    .WithName("GetHealth")
    .WithOpenApi();

app.Run();

public sealed record HealthResponse(string Status, string Api, string DataAssembly);
