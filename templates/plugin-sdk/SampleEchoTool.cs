using InfernalHierarchy.Core.Interfaces;

namespace InfernalPlugin.Sample;

public sealed class SampleEchoTool : ITool
{
    public string Name => "sample_echo";

    public string Description => "Example plugin tool. Params: text.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var text = parameters.TryGetValue("text", out var raw) ? raw?.ToString() ?? string.Empty : string.Empty;

        return Task.FromResult(new ToolResult
        {
            Success = true,
            Output = text,
            Metadata = new Dictionary<string, object>
            {
                ["provider"] = "plugin_sdk_sample"
            }
        });
    }
}
