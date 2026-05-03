using System.Globalization;
using MassTransit;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.AiNewsDelivery.Abstractions;
using Platform.AiNewsDelivery.Configuration;
using Platform.AiNewsDelivery.Infrastructure;
using Platform.AiNewsDelivery.Infrastructure.Extractors;
using Platform.AiNewsDelivery.Infrastructure.HealthChecks;
using Platform.AiNewsDelivery.Infrastructure.Persistence;
using Polly;
using Serilog;

// ── Bootstrap Serilog (captura erros de startup antes do Host estar pronto) ──
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    Log.Information("Platform.AiNewsDelivery iniciando...");

    var host = new HostBuilder()
        .ConfigureFunctionsWorkerDefaults()
        .ConfigureAppConfiguration((context, config) =>
        {
            config
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                    optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();
        })
        .UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName();
        })
        .ConfigureServices((context, services) =>
        {
            var config = context.Configuration;

            // ── Options Pattern: validação na inicialização ────────────────────
            services.AddOptions<NewsWorkerOptions>()
                .BindConfiguration(NewsWorkerOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<SourceOptions>()
                .BindConfiguration(SourceOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<RabbitMqOptions>()
                .BindConfiguration(RabbitMqOptions.SectionName)
                .ValidateDataAnnotations();

            // ── EF Core + SQLite ───────────────────────────────────────────────
            var workerOptions = config
                .GetSection(NewsWorkerOptions.SectionName)
                .Get<NewsWorkerOptions>();

            var dbPath = workerOptions?.DatabasePath ?? "news-delivery.db";

            services.AddDbContext<NewsDbContext>(options =>
            {
                options.UseSqlite($"Data Source={dbPath}");

                // Em desenvolvimento, loga queries SQL no console
                if (context.HostingEnvironment.IsDevelopment())
                    options.EnableDetailedErrors().EnableSensitiveDataLogging();
            });

            // ── Aplica migrations automaticamente na inicialização ─────────────
            // Seguro para SQLite: idempotente, não bloqueia, cria o arquivo se necessário.
            services.AddHostedService<DatabaseMigrationService>();

            // ── HealthChecks ───────────────────────────────────────────────────
            services.AddHealthChecks()
                .AddCheck<SqliteHealthCheck>(
                    name: "sqlite",
                    tags: ["ready", "database"]);

            // ── Messaging: MassTransit + RabbitMQ (DI pronto, Sprint 4 ativa) ──
            var rabbitConfig = config
                .GetSection(RabbitMqOptions.SectionName)
                .Get<RabbitMqOptions>();

            services.AddMassTransit(bus =>
            {
                bus.AddEntityFrameworkOutbox<NewsDbContext>(o =>
                {
                    o.UseSqlite();
                    o.UseBusOutbox();
                });

                // Consumers do MSEMC residem no MSEMC — aqui apenas publicamos.
                // Sprint 4 adicionará: bus.AddConsumer<...>() se necessário.

                if (rabbitConfig is not null
                    && !string.IsNullOrWhiteSpace(rabbitConfig.Host))
                {
                    bus.UsingRabbitMq((busContext, cfg) =>
                    {
                        cfg.Host(rabbitConfig.Host, rabbitConfig.Port,
                            rabbitConfig.VirtualHost, h =>
                            {
                                h.Username(rabbitConfig.Username);
                                h.Password(rabbitConfig.Password);
                            });

                        cfg.ConfigureEndpoints(busContext);
                    });
                }
                else
                {
                    // Fallback: InMemory para desenvolvimento local sem Docker
                    bus.UsingInMemory((busContext, cfg) =>
                        cfg.ConfigureEndpoints(busContext));
                }
            });

            // ── HttpClientFactory + Polly Resilience ────────────────────────────
            var sourceConfig = config.GetSection(SourceOptions.SectionName).Get<SourceOptions>()
                ?? new SourceOptions();

            services.AddHttpClient("OpenRouter", client =>
            {
                client.BaseAddress = new Uri(sourceConfig.OpenRouter.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "Platform-AiNewsDelivery/1.0");
            })
            .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
                3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500))))
            .AddTransientHttpErrorPolicy(p => p.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

            services.AddHttpClient("HuggingFace", client =>
            {
                client.BaseAddress = new Uri(sourceConfig.HuggingFace.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "Platform-AiNewsDelivery/1.0");
            })
            .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
                3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500))))
            .AddTransientHttpErrorPolicy(p => p.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

            // ── Extractors: registrados como IExtractor (resolvidos via IEnumerable) ─
            services.AddTransient<IExtractor, OpenRouterExtractor>();
            services.AddTransient<IExtractor, HuggingFaceExtractor>();
            services.AddHttpClient<IExtractor, ArtificialAnalysisExtractor>(client =>
            {
                client.BaseAddress = new Uri(sourceConfig.ArtificialAnalysis.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "Platform-AiNewsDelivery/1.0");

                if (!string.IsNullOrWhiteSpace(sourceConfig.ArtificialAnalysis.ApiKey))
                {
                    client.DefaultRequestHeaders.Add("x-api-key", sourceConfig.ArtificialAnalysis.ApiKey);
                }
            })
                .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
                    3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
                        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500))))
                .AddTransientHttpErrorPolicy(p => p.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

            // ── Comparador de Snapshots ───────────────────────────────────────────
            services.AddTransient<ISnapshotComparer, SnapshotComparer>();
            services.AddTransient<Platform.AiNewsDelivery.Infrastructure.Dispatchers.IDigestDispatcher, Platform.AiNewsDelivery.Infrastructure.Dispatchers.DigestDispatcher>();


        })
        .Build();

    // Garante que o banco e as migrations estão prontos antes de aceitar triggers
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Platform.AiNewsDelivery encerrado inesperadamente.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
