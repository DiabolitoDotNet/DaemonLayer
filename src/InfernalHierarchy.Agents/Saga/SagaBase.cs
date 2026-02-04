using InfernalHierarchy.Core.Saga;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Agents.Saga;

/// <summary>
/// Base implementation of saga coordinator
/// </summary>
public abstract class SagaBase : ISaga
{
    /// <inheritdoc/>
    public string SagaId { get; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public List<ISagaStep> Steps { get; } = new();

    /// <summary>
    /// Logger instance
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaBase"/> class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    protected SagaBase(ILogger logger)
    {
        Logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SagaResult> ExecuteAsync(CancellationToken ct = default)
    {
        var context = new SagaContext { SagaId = SagaId };
        var startTime = DateTime.UtcNow;

        Logger.LogInformation("Starting saga {SagaName} ({SagaId}) with {StepCount} steps",
            Name, SagaId, Steps.Count);

        try
        {
            // Execute all steps sequentially
            for (int i = 0; i < Steps.Count; i++)
            {
                context.CurrentStep = i;
                var step = Steps[i];

                Logger.LogDebug("Executing step {StepIndex}/{Total}: {StepName}",
                    i + 1, Steps.Count, step.Name);

                await step.ExecuteAsync(context, ct).ConfigureAwait(false);
                context.CompletedSteps.Add(step.Name);

                Logger.LogDebug("Step {StepName} completed successfully", step.Name);
            }

            context.EndTime = DateTime.UtcNow;
            Logger.LogInformation("Saga {SagaName} ({SagaId}) completed successfully in {Duration}ms",
                Name, SagaId, (context.EndTime.Value - startTime).TotalMilliseconds);

            return new SagaResult
            {
                Success = true,
                Context = context,
                ExecutionTime = context.EndTime.Value - startTime
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Saga {SagaName} ({SagaId}) failed at step {StepIndex}/{Total}",
                Name, SagaId, context.CurrentStep + 1, Steps.Count);

            context.ErrorMessage = ex.Message;
            context.EndTime = DateTime.UtcNow;

            // Compensate completed steps in reverse order
            var compensationSuccess = await CompensateAsync(context, ct).ConfigureAwait(false);

            return new SagaResult
            {
                Success = false,
                Context = context,
                ErrorMessage = ex.Message,
                CompensationSuccess = compensationSuccess,
                ExecutionTime = context.EndTime.Value - startTime
            };
        }
    }

    /// <summary>
    /// Compensates all completed steps in reverse order
    /// </summary>
    private async Task<bool> CompensateAsync(SagaContext context, CancellationToken ct)
    {
        Logger.LogWarning("Starting compensation for saga {SagaName} ({SagaId})", Name, SagaId);

        var compensationSuccess = true;

        // Compensate in reverse order
        for (int i = context.CompletedSteps.Count - 1; i >= 0; i--)
        {
            var stepName = context.CompletedSteps[i];
            var step = Steps.First(s => s.Name == stepName);

            try
            {
                Logger.LogDebug("Compensating step: {StepName}", stepName);
                await step.CompensateAsync(context, ct).ConfigureAwait(false);
                context.CompensatedSteps.Add(stepName);
                Logger.LogDebug("Step {StepName} compensated successfully", stepName);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to compensate step {StepName}", stepName);
                compensationSuccess = false;
            }
        }

        if (compensationSuccess)
        {
            Logger.LogInformation("Compensation completed successfully for saga {SagaName}", Name);
        }
        else
        {
            Logger.LogError("Compensation failed for saga {SagaName} - manual intervention required", Name);
        }

        return compensationSuccess;
    }

    /// <summary>
    /// Adds a step to the saga
    /// </summary>
    /// <param name="step">Step to add</param>
    protected void AddStep(ISagaStep step)
    {
        Steps.Add(step);
    }
}
