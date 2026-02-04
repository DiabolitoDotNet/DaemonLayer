using System.Collections.Concurrent;
using InfernalHierarchy.Core.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Agents.CQRS;

/// <summary>
/// Dispatcher for CQRS commands and queries
/// </summary>
public class CqrsDispatcher
{
    private readonly ILogger<CqrsDispatcher> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, object> _queryCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CqrsDispatcher"/> class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="serviceProvider">Service provider for resolving handlers</param>
    public CqrsDispatcher(ILogger<CqrsDispatcher> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Dispatches a command to its handler
    /// </summary>
    /// <typeparam name="TCommand">Command type</typeparam>
    /// <param name="command">Command to dispatch</param>
    /// <param name="ct">Cancellation token</param>
    public async Task DispatchCommandAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand
    {
        _logger.LogInformation("Dispatching command {CommandType} with ID {CommandId}",
            typeof(TCommand).Name, command.CommandId);

        var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand>>();
        
        try
        {
            await handler.HandleAsync(command, ct).ConfigureAwait(false);
            _logger.LogInformation("Command {CommandId} handled successfully", command.CommandId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {CommandId}", command.CommandId);
            throw;
        }
    }

    /// <summary>
    /// Dispatches a query to its handler
    /// </summary>
    /// <typeparam name="TQuery">Query type</typeparam>
    /// <typeparam name="TResult">Result type</typeparam>
    /// <param name="query">Query to dispatch</param>
    /// <param name="useCache">Whether to use query cache</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Query result</returns>
    public async Task<TResult> DispatchQueryAsync<TQuery, TResult>(
        TQuery query,
        bool useCache = false,
        CancellationToken ct = default)
        where TQuery : IQuery<TResult>
    {
        _logger.LogDebug("Dispatching query {QueryType} with ID {QueryId}",
            typeof(TQuery).Name, query.QueryId);

        // Check cache if enabled
        if (useCache)
        {
            var cacheKey = $"{typeof(TQuery).FullName}:{query.QueryId}";
            if (_queryCache.TryGetValue(cacheKey, out var cachedResult))
            {
                _logger.LogDebug("Query {QueryId} served from cache", query.QueryId);
                return (TResult)cachedResult;
            }
        }

        var handler = _serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        
        try
        {
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            _logger.LogDebug("Query {QueryId} handled successfully", query.QueryId);

            // Cache result if enabled
            if (useCache)
            {
                var cacheKey = $"{typeof(TQuery).FullName}:{query.QueryId}";
                _queryCache[cacheKey] = result!;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling query {QueryId}", query.QueryId);
            throw;
        }
    }

    /// <summary>
    /// Clears the query cache
    /// </summary>
    public void ClearCache()
    {
        _queryCache.Clear();
        _logger.LogInformation("Query cache cleared");
    }

    /// <summary>
    /// Invalidates specific query from cache
    /// </summary>
    /// <param name="queryType">Query type</param>
    /// <param name="queryId">Query ID</param>
    public void InvalidateCache(Type queryType, string queryId)
    {
        var cacheKey = $"{queryType.FullName}:{queryId}";
        _queryCache.TryRemove(cacheKey, out _);
        _logger.LogDebug("Invalidated cache for {QueryType}:{QueryId}", queryType.Name, queryId);
    }
}
