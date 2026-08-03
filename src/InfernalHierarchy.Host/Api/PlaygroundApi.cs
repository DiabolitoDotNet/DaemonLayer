using Microsoft.AspNetCore.Http;
using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Host.Api;

internal static class PlaygroundApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        var operatorOptions = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;

        app.MapGet("/api/playground/scenarios", (HttpContext ctx, IAgentPlaygroundService playground, int? limit) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var scenarios = playground.ListScenarios(limit ?? 50);
            return Results.Ok(new { items = scenarios });
        });

        app.MapPost("/api/playground/scenarios", async (
            HttpContext ctx,
            PlaygroundScenarioCreateRequest request,
            IAgentPlaygroundService playground,
            IMessageBus messageBus,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Missing request body: name" });
            }

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.BadRequest(new { error = "Missing request body: prompt" });
            }

            var toAgentId = string.IsNullOrWhiteSpace(request.ToAgentId) ? "lucifer" : request.ToAgentId.Trim();
            var timeoutMs = request.TimeoutMs is > 0 and <= 300_000 ? request.TimeoutMs.Value : 180_000;
            var executionProfile = string.IsNullOrWhiteSpace(request.ExecutionProfile)
                ? "Research"
                : request.ExecutionProfile.Trim();

            var id = playground.CreateScenario(request.Name.Trim(), request.Prompt.Trim(), toAgentId, timeoutMs, request.Tags);
            var scenario = playground.GetScenario(id);

            if (scenario is null)
            {
                return Results.Problem(title: "Playground", detail: "Failed to create scenario", statusCode: 500);
            }

            var response = await SendChatAndWaitAsync(messageBus, scenario.Prompt, scenario.ToAgentId, scenario.TimeoutMs, executionProfile, ct).ConfigureAwait(false);
            var run = playground.AddRun(scenario.ScenarioId, scenario.Prompt, scenario.ToAgentId, scenario.TimeoutMs, response);

            return Results.Ok(new { scenario, run });
        });

        app.MapPost("/api/playground/scenarios/{scenarioId}/run", async (
            HttpContext ctx,
            string scenarioId,
            PlaygroundScenarioRunRequest request,
            IAgentPlaygroundService playground,
            IMessageBus messageBus,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var scenario = playground.GetScenario(scenarioId);
            if (scenario is null)
            {
                return Results.NotFound(new { scenarioId, error = "Scenario not found" });
            }

            var prompt = string.IsNullOrWhiteSpace(request.Prompt) ? scenario.Prompt : request.Prompt.Trim();
            var toAgentId = string.IsNullOrWhiteSpace(request.ToAgentId) ? scenario.ToAgentId : request.ToAgentId.Trim();
            var timeoutMs = request.TimeoutMs is > 0 and <= 300_000 ? request.TimeoutMs.Value : scenario.TimeoutMs;
            var executionProfile = string.IsNullOrWhiteSpace(request.ExecutionProfile)
                ? "Research"
                : request.ExecutionProfile.Trim();

            var response = await SendChatAndWaitAsync(messageBus, prompt, toAgentId, timeoutMs, executionProfile, ct).ConfigureAwait(false);
            var run = playground.AddRun(scenario.ScenarioId, prompt, toAgentId, timeoutMs, response);

            return Results.Ok(new { scenario, run });
        });

        app.MapGet("/api/playground/scenarios/{scenarioId}/runs", (
            HttpContext ctx,
            string scenarioId,
            IAgentPlaygroundService playground,
            int? limit) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var scenario = playground.GetScenario(scenarioId);
            if (scenario is null)
            {
                return Results.NotFound(new { scenarioId, error = "Scenario not found" });
            }

            var runs = playground.GetRuns(scenarioId, limit ?? 20);
            return Results.Ok(new { scenario, items = runs });
        });

        app.MapPost("/api/playground/runs/{runId}/replay", async (
            HttpContext ctx,
            string runId,
            IAgentPlaygroundService playground,
            IMessageBus messageBus,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var run = playground.GetRun(runId);
            if (run is null)
            {
                return Results.NotFound(new { runId, error = "Run not found" });
            }

            var response = await SendChatAndWaitAsync(messageBus, run.Prompt, run.ToAgentId, run.TimeoutMs, "Research", ct).ConfigureAwait(false);
            var replay = playground.AddRun(run.ScenarioId, run.Prompt, run.ToAgentId, run.TimeoutMs, response);

            return Results.Ok(new { sourceRunId = runId, replayRun = replay });
        });
    }

    private static async Task<ChatResponse> SendChatAndWaitAsync(
        IMessageBus messageBus,
        string message,
        string toAgentId,
        int timeoutMs,
        string executionProfile,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var replyToId = $"playground-{Guid.NewGuid():N}";
        var startedUtc = DateTime.UtcNow;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
        var enumerator = messageBus.SubscribeAsync(replyToId, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);

        try
        {
            var msg = new AgentMessage
            {
                Id = replyToId,
                FromAgentId = replyToId,
                ToAgentId = toAgentId,
                Type = MessageType.Task,
                Content = message,
                CorrelationId = correlationId,
                Payload = new Dictionary<string, object>
                {
                    ["transport"] = "playground",
                    ["request_id"] = replyToId,
                    ["started_utc"] = startedUtc.ToString("O"),
                    ["execution_profile"] = string.IsNullOrWhiteSpace(executionProfile) ? "Research" : executionProfile
                }
            };

            await messageBus.PublishAsync(msg, ct).ConfigureAwait(false);

            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                var response = enumerator.Current;
                if (response.Type != MessageType.Report)
                {
                    continue;
                }

                return new ChatResponse(
                    fromAgentId: response.FromAgentId,
                    toAgentId: response.ToAgentId,
                    content: response.Content,
                    payload: AutonomyOutcomeContractEvaluator.EnrichAutonomyOutcomePayload(response.Content, response.Payload),
                    correlationId: response.CorrelationId ?? correlationId,
                    causationId: response.CausationId,
                    receivedUtc: DateTime.UtcNow,
                    durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds);
            }

            return new ChatResponse(
                fromAgentId: "system",
                toAgentId: toAgentId,
                content: $"Timeout: no report received within {timeoutMs}ms",
                payload: AutonomyOutcomeContractEvaluator.BuildTimeoutOutcomePayload(),
                correlationId: correlationId,
                causationId: null,
                receivedUtc: DateTime.UtcNow,
                durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return new ChatResponse(
                fromAgentId: "system",
                toAgentId: toAgentId,
                content: $"Timeout: no report received within {timeoutMs}ms",
                payload: AutonomyOutcomeContractEvaluator.BuildTimeoutOutcomePayload(),
                correlationId: correlationId,
                causationId: null,
                receivedUtc: DateTime.UtcNow,
                durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds);
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            if (messageBus is ChannelMessageBus cmb)
            {
                cmb.CleanupAgent(replyToId);
            }
        }
    }

}