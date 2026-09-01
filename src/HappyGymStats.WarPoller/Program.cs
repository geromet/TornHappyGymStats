using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HappyGymStats.WarPoller;

public static class Program
{
    public static Task Main(string[] args)
        => BuildHost(args).RunAsync();

    public static IHost BuildHost(
        string[]? args = null,
        Action<HostApplicationBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateApplicationBuilder(args ?? []);
        configureBuilder?.Invoke(builder);

        ConfigureServices(builder);
        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }

    private static void ConfigureServices(HostApplicationBuilder builder)
    {
        var connectionString = ResolveConnectionString(builder.Configuration);

        builder.Services
            .AddOptions<WarPollerOptions>()
            .Bind(builder.Configuration.GetSection(WarPollerOptions.SectionName));

        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<WarPollerOptions>>().Value;
            options.Validate();
            return options;
        });

        builder.Services.Configure<HostOptions>(options =>
        {
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });

        builder.Services.AddSingleton<IWarPollerClock, WarPollerClock>();
        builder.Services.AddHttpClient<TornApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.torn.com/");
        });
        builder.Services.AddHttpClient<IWarPollerNotifier, WarPollerNotifier>();

        builder.Services.AddDbContext<HappyGymStatsDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddScoped<IWarStateRepository, WarStateRepository>();
        builder.Services.AddScoped<IImportRunRepository, ImportRunRepository>();
        builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HappyGymStatsDbContext>());
        builder.Services.AddScoped<WarPollerService>();
        builder.Services.AddHostedService<WarPollerHostedService>();
    }

    private static string ResolveConnectionString(IConfiguration configuration)
        => configuration.GetConnectionString("HappyGymStats")
           ?? configuration["HAPPYGYMSTATS_CONNECTION_STRING"]
           ?? throw new InvalidOperationException(
               "No Postgres connection string found. Set ConnectionStrings:HappyGymStats or HAPPYGYMSTATS_CONNECTION_STRING.");
}
