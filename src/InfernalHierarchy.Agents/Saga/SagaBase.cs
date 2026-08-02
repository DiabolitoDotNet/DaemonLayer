using InfernalHierarchy.Core.Saga;
using System.Collections.ObjectModel;

namespace InfernalHierarchy.Agents.Saga;

/// <summary>
/// Base implementation of saga coordinator
/// </summary>
public abstract class SagaBase : ISaga
{
    private const int CompensationMaxAttempts = 3;

    /// <inheritdoc/>
    public string SagaId { get; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public Collection<ISagaStep> Steps { get; } = new();

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

    /// <summary>
    /// Creates the saga execution context. Derived sagas can override this to seed initial data.
    /// </summary>
    protected virtual SagaContext CreateContext() => new() { SagaId = SagaId };

    /// <inheritdoc/>
    public async Task<SagaResult> ExecuteAsync(CancellationToken ct = default)
    {
        var context = CreateContext();
        context.SagaId = SagaId;
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
            var compensationOutcome = await CompensateAsync(context, ct).ConfigureAwait(false);

            return new SagaResult
            {
                Success = false,
                Context = context,
                ErrorMessage = ex.Message,
                CompensationSuccess = compensationOutcome.Success,
                ExecutionTime = context.EndTime.Value - startTime,
                FailureReasonCode = compensationOutcome.FailureReasonCode,
                NextAction = compensationOutcome.NextAction,
                NeedsSupervisorIntervention = compensationOutcome.NeedsSupervisorIntervention
            };
        }
    }

    /// <summary>
    /// Compensates all completed steps in reverse order
    /// </summary>
    private async Task<CompensationOutcome> CompensateAsync(SagaContext context, CancellationToken ct)
    {
        Logger.LogWarning("Starting compensation for saga {SagaName} ({SagaId})", Name, SagaId);

        var compensationSuccess = true;
        var failedCompensationSteps = new List<string>();

        // Compensate in reverse order
        for (int i = context.CompletedSteps.Count - 1; i >= 0; i--)
        {
            var stepName = context.CompletedSteps[i];
            var step = Steps.First(s => s.Name == stepName);

            var compensated = false;
            for (int attempt = 1; attempt <= CompensationMaxAttempts; attempt++)
            {
                try
                {
                    Logger.LogDebug(
                        "Compensating step: {StepName} (attempt {Attempt}/{MaxAttempts})",
                        stepName,
                        attempt,
                        CompensationMaxAttempts);

                    await step.CompensateAsync(context, ct).ConfigureAwait(false);
                    context.CompensatedSteps.Add(stepName);
                    compensated = true;
                    Logger.LogDebug("Step {StepName} compensated successfully", stepName);
                    break;
                }
                catch (Exception ex) when (attempt < CompensationMaxAttempts)
                {
                    var backoff = TimeSpan.FromMilliseconds(100 * attempt);
                    Logger.LogWarning(
                        ex,
                        "Compensation attempt {Attempt}/{MaxAttempts} failed for step {StepName}; retrying in {Backoff}ms",
                        attempt,
                        CompensationMaxAttempts,
                        stepName,
                        backoff.TotalMilliseconds);
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex,
                        "Compensation failed for step {StepName} after {MaxAttempts} attempts",
                        stepName,
                        CompensationMaxAttempts);
                    break;
                }
            }

            if (!compensated)
            {
                compensationSuccess = false;
                failedCompensationSteps.Add(stepName);
            }
        }

        if (compensationSuccess)
        {
            Logger.LogInformation("Compensation completed successfully for saga {SagaName}", Name);
            return new CompensationOutcome(
                Success: true,
                FailureReasonCode: "execution_step_failed",
                NextAction: "saga_compensated",
                NeedsSupervisorIntervention: false);
        }

        var failedStepsSummary = string.Join(",", failedCompensationSteps.OrderBy(s => s, StringComparer.Ordinal));
        context.Data["CompensationFailureReasonCode"] = "compensation_retry_exhausted";
        context.Data["CompensationFailedSteps"] = failedCompensationSteps;
        context.Data["SupervisorEscalationRequested"] = true;

        Logger.LogError(
            "Compensation failed for saga {SagaName}. Reason={ReasonCode} FailedSteps={FailedSteps} EscalationRequested={EscalationRequested}",
            Name,
            "compensation_retry_exhausted",
            failedStepsSummary,
            true);

        return new CompensationOutcome(
            Success: false,
            FailureReasonCode: "compensation_retry_exhausted",
            NextAction: "request_supervisor_compensation_assistance",
            NeedsSupervisorIntervention: true);
    }

    /// <summary>
    /// Adds a step to the saga
    /// </summary>
    /// <param name="step">Step to add</param>
    protected void AddStep(ISagaStep step)
    {
        Steps.Add(step);
    }

    private sealed record CompensationOutcome(
        bool Success,
        string FailureReasonCode,
        string NextAction,
        bool NeedsSupervisorIntervention);
}
