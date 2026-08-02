namespace InfernalHierarchy.Tools.Clients;

public static class OllamaModelRoutingPolicy
{
    public static string ResolveModel(
        OllamaOptions options,
        LlmRoutingHint routingHint,
        IModelRoutingFeedbackStore? feedbackStore = null)
    {
        if (!options.EnableModelRoutingPolicy || options.ModelRoutes.Count == 0)
        {
            return options.DefaultModel;
        }

        var requestedTaskType = NormalizeTaskType(routingHint.TaskType);
        var budget = routingHint.LatencyBudgetMs.GetValueOrDefault();
        var hasBudget = budget > 0;

        var candidates = options.ModelRoutes
            .Where(route => !string.IsNullOrWhiteSpace(route.Model))
            .Where(route => IsTaskMatch(route.TaskType, requestedTaskType))
            .Where(route => IsLatencyMatch(route.MaxLatencyMs, budget, hasBudget))
            .OrderBy(route => IsExactTaskMatch(route.TaskType, requestedTaskType) ? 0 : 1)
            .ThenBy(route => route.MaxLatencyMs > 0 ? 0 : 1)
            .ThenBy(route => route.MaxLatencyMs <= 0 ? int.MaxValue : route.MaxLatencyMs)
            .ThenBy(route => ComputeAdaptivePenalty(options, feedbackStore, route.Model))
            .ThenBy(route => route.Priority)
            .ToList();

        if (candidates.Count == 0)
        {
            return options.DefaultModel;
        }

        return candidates[0].Model.Trim();
    }

    private static double ComputeAdaptivePenalty(
        OllamaOptions options,
        IModelRoutingFeedbackStore? feedbackStore,
        string modelName)
    {
        if (!options.EnableAdaptiveRoutingFeedback || feedbackStore is null)
        {
            return 0d;
        }

        return feedbackStore.GetPenalty(modelName);
    }

    private static bool IsLatencyMatch(int maxLatencyMs, int requestedBudgetMs, bool hasBudget)
    {
        if (maxLatencyMs <= 0)
        {
            return true;
        }

        if (!hasBudget)
        {
            return false;
        }

        return requestedBudgetMs <= maxLatencyMs;
    }

    private static bool IsTaskMatch(string routeTaskType, string requestedTaskType)
    {
        var normalizedRoute = NormalizeTaskType(routeTaskType);
        if (normalizedRoute == "*")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(requestedTaskType))
        {
            return false;
        }

        return string.Equals(normalizedRoute, requestedTaskType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactTaskMatch(string routeTaskType, string requestedTaskType)
    {
        if (string.IsNullOrWhiteSpace(requestedTaskType))
        {
            return false;
        }

        return string.Equals(NormalizeTaskType(routeTaskType), requestedTaskType, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTaskType(string? taskType)
    {
        if (string.IsNullOrWhiteSpace(taskType))
        {
            return "*";
        }

        return taskType.Trim().ToLowerInvariant();
    }
}
