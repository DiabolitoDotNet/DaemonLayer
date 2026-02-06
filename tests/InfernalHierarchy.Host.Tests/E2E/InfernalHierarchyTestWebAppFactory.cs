using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host;
using InfernalHierarchy.Tools.Clients.Search;
using InfernalHierarchy.Tools.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfernalHierarchy.Host.Tests.E2E;

public class InfernalHierarchyTestWebAppFactory : WebApplicationFactory<Program>
{
    public string TempDbPath { get; } = Path.Combine(Path.GetTempPath(), $"infernal_e2e_{Guid.NewGuid():N}.db");
    public string TempSoulsDir { get; } = Path.Combine(Path.GetTempPath(), $"infernal_souls_{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(TempSoulsDir);
        SeedPersona(TempSoulsDir);

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Http:Enabled"] = "true",

                // UI & WebSockets are local-only by default; TestServer doesn't always populate RemoteIpAddress.
                ["Ui:Enabled"] = "true",
                ["Ui:LocalOnly"] = "false",
                ["WebSockets:Enabled"] = "true",
                ["WebSockets:LocalOnly"] = "false",

                ["Voice:Enabled"] = "true",
                ["Voice:LocalOnly"] = "false",

                // Required by validators (ValidateOnStart)
                ["Ollama:BaseUrl"] = "http://localhost:11434",
                ["Ollama:DefaultModel"] = "test-model",
                ["Ollama:MaxTokens"] = "256",
                ["Ollama:Temperature"] = "0",

                ["Memory:DatabasePath"] = TempDbPath,

                ["Hierarchy:MainAgentName"] = "Lucifer",

                ["SearXNG:Enabled"] = "true",
                ["SearXNG:BaseUrl"] = "http://searxng:8080",

                ["BraveSearch:Enabled"] = "false",

                ["Email:Enabled"] = "true",
                ["Email:Host"] = "localhost",
                ["Email:Port"] = "25",
                ["Email:Username"] = "user",
                ["Email:Password"] = "pass",
                ["Email:FromAddress"] = "bot@example.com",
                ["Email:FromName"] = "Infernal Test Bot",

                // Keep ReAct deterministic in tests.
                ["ReActOptions:UseJsonResponse"] = "true",

                // Persona editor APIs use a file-backed store
                ["Personas:SoulsDirectory"] = TempSoulsDir
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // Replace LLM with scripted deterministic implementation
            services.AddSingleton<ScriptedLlmClient>();
            services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<ScriptedLlmClient>());

            // Replace persona loader (avoid filesystem)
            services.AddSingleton<IPersonaLoader, TestPersonaLoader>();

            // Replace external integrations
            services.AddSingleton<FakeSearXngClient>();
            services.AddSingleton<ISearXngClient>(sp => sp.GetRequiredService<FakeSearXngClient>());

            services.AddSingleton<FakeEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>());

            // Remove noisy/background hosted services not needed for API E2E.
            RemoveHostedService<InfernalHierarchy.Memory.Vector.VectorMemoryInitializationService>(services);
            RemoveHostedService<InfernalHierarchy.Memory.Maintenance.MemoryPruningService>(services);
            RemoveHostedService<InfernalHierarchy.Memory.Learning.MemoryLearningService>(services);
            RemoveHostedService<InfernalHierarchy.Host.Configuration.ConfigurationReloadService>(services);
            RemoveHostedService<InfernalHierarchy.Host.Security.SecretRotationService>(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            try
            {
                if (File.Exists(TempDbPath))
                {
                    File.Delete(TempDbPath);
                }
            }
            catch
            {
                // best-effort
            }

            try
            {
                if (Directory.Exists(TempSoulsDir))
                {
                    Directory.Delete(TempSoulsDir, recursive: true);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static void SeedPersona(string soulsDir)
    {
        var path = Path.Combine(soulsDir, "testdemon.json");
        if (File.Exists(path)) return;

        var json = """
{
  "name": "testdemon",
  "demonTitle": "Test Demon",
  "systemPrompt": "You are a test persona.",
  "specializations": ["Testing"],
  "availableTools": ["read_memory"],
  "personality": {
    "tone": "Neutral",
    "approach": "Direct",
    "verbosity": 3,
    "useDemonicTheme": false
  },
  "customInstructions": {}
}
""";

        File.WriteAllText(path, json);
    }

    private static void RemoveHostedService<T>(IServiceCollection services)
    {
        var toRemove = services
            .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                        && d.ImplementationType == typeof(T))
            .ToList();

        foreach (var descriptor in toRemove)
        {
            services.Remove(descriptor);
        }
    }
}
