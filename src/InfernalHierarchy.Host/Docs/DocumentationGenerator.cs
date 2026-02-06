using System.Text;
using System.Text.Json;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Core.Serialization;
using InfernalHierarchy.Host.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using InfernalHierarchy.Host.Configuration;

namespace InfernalHierarchy.Host.Docs;

internal sealed class DocumentationGenerator
{
    private readonly IConfiguration _config;
    private readonly IToolRegistry _tools;
    private readonly IPersonaLoader _personaLoader;
    private readonly MetricsService _metrics;
    private readonly IOptions<UiInterfaceOptions> _ui;
    private readonly IOptions<WebSocketInterfaceOptions> _ws;
    private readonly IOptions<VoiceInterfaceOptions> _voice;

    public DocumentationGenerator(
        IConfiguration config,
        IToolRegistry tools,
        IPersonaLoader personaLoader,
        MetricsService metrics,
        IOptions<UiInterfaceOptions> ui,
        IOptions<WebSocketInterfaceOptions> ws,
        IOptions<VoiceInterfaceOptions> voice)
    {
        _config = config;
        _tools = tools;
        _personaLoader = personaLoader;
        _metrics = metrics;
        _ui = ui;
        _ws = ws;
        _voice = voice;
    }

    public async Task<string> GenerateMarkdownAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# InfernalHierarchy – Runtime Docs");
        sb.AppendLine();
        sb.AppendLine($"Generated (UTC): `{DateTime.UtcNow:O}`");
        sb.AppendLine();

        sb.AppendLine("## Interfaces");
        sb.AppendLine();
        sb.AppendLine($"- UI: `{_ui.Value.Enabled}` (localOnly: `{_ui.Value.LocalOnly}`)");
        sb.AppendLine($"- WebSockets: `{_ws.Value.Enabled}` (localOnly: `{_ws.Value.LocalOnly}`) endpoint: `/ws`");
        sb.AppendLine($"- Voice API: `{_voice.Value.Enabled}` (localOnly: `{_voice.Value.LocalOnly}`) endpoints: `/api/voice/*`");
        sb.AppendLine();

        sb.AppendLine("## HTTP Endpoints");
        sb.AppendLine();
        sb.AppendLine("### UI");
        sb.AppendLine();
        sb.AppendLine("- `/ui` (dashboard)");
        sb.AppendLine("- `/ui/perf` (performance)");
        sb.AppendLine("- `/ui/personas` (persona editor)");
        sb.AppendLine("- `/ui/docs` (documentation generator)");
        sb.AppendLine();

        sb.AppendLine("### APIs");
        sb.AppendLine();
        sb.AppendLine("- `GET /api/agents` – list agents");
        sb.AppendLine("- `GET /api/tools` – list tools");
        sb.AppendLine("- `POST /api/chat` – send a task and wait for Report");
        sb.AppendLine("- `GET /api/events?minutes=60` – recent events");
        sb.AppendLine("- `GET /api/perf/snapshot` – runtime snapshot");
        sb.AppendLine("- `GET /api/perf/histograms` – latency histograms snapshot");
        sb.AppendLine("- `GET /api/personas` – list persona files");
        sb.AppendLine("- `GET /api/personas/{name}` – load persona JSON");
        sb.AppendLine("- `PUT /api/personas/{name}` – save persona JSON");
        sb.AppendLine("- `GET /api/docs/markdown` – this document as markdown");
        sb.AppendLine("- `GET /api/docs/json` – docs as JSON");
        sb.AppendLine();

        sb.AppendLine("## Tools");
        sb.AppendLine();
        foreach (var tool in _tools.GetAllTools().OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- `{tool.Name}` – {tool.Description}");
        }
        sb.AppendLine();

        sb.AppendLine("## Personas");
        sb.AppendLine();
        try
        {
            var personas = (await _personaLoader.LoadAllPersonasAsync(ct)).ToList();
            foreach (var p in personas.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- `{p.Name}` – {p.DemonTitle}");
            }
        }
        catch
        {
            sb.AppendLine("- (failed to enumerate personas)");
        }
        sb.AppendLine();

        sb.AppendLine("## Metrics");
        sb.AppendLine();
        sb.AppendLine("Prometheus endpoint: `/metrics`");
        var message = _metrics.GetMessageLatencyStats();
        var llm = _metrics.GetLlmLatencyStats();
        sb.AppendLine($"- message latency p95 (ms): `{message.P95:F2}` (n={message.Count})");
        sb.AppendLine($"- llm latency p95 (ms): `{llm.P95:F2}` (n={llm.Count})");
        sb.AppendLine();

        sb.AppendLine("## Configuration Hints");
        sb.AppendLine();
        sb.AppendLine("- Persona editor can be pointed at a custom folder via `Personas:SoulsDirectory`.");

        return sb.ToString();
    }

    public async Task<string> GenerateJsonAsync(CancellationToken ct)
    {
        var tools = _tools.GetAllTools().Select(t => new { t.Name, t.Description }).OrderBy(t => t.Name).ToList();
        var personas = (await _personaLoader.LoadAllPersonasAsync(ct)).Select(p => new { p.Name, p.DemonTitle }).OrderBy(p => p.Name).ToList();

        var doc = new
        {
            generatedUtc = DateTime.UtcNow,
            interfaces = new
            {
                ui = _ui.Value,
                webSockets = _ws.Value,
                voice = _voice.Value
            },
            endpoints = new
            {
                ui = new[] { "/ui", "/ui/perf", "/ui/personas", "/ui/docs" },
                apis = new[]
                {
                    "/api/agents",
                    "/api/tools",
                    "/api/chat",
                    "/api/events",
                    "/api/perf/snapshot",
                    "/api/perf/histograms",
                    "/api/personas",
                    "/api/personas/{name}",
                    "/api/docs/markdown",
                    "/api/docs/json"
                }
            },
            tools,
            personas
        };

        return JsonSerializer.Serialize(doc, JsonDefaults.WebIndented);
    }
}
