
namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class CodeExecutionToolOptionsValidator : IValidateOptions<CodeExecutionToolOptions>
{
    public ValidateOptionsResult Validate(string? name, CodeExecutionToolOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            failures.Add("CodeExecution:RootDirectory is required when CodeExecution:Enabled=true");
        }
        else
        {
            var root = options.RootDirectory;
            if (!Path.IsPathRooted(root))
            {
                if (root.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    failures.Add("CodeExecution:RootDirectory contains invalid path characters");
                }
            }
            else
            {
                if (!Directory.Exists(root))
                {
                    failures.Add("CodeExecution:RootDirectory must exist when absolute path is provided");
                }
            }
        }

        if (!options.EnablePython && !options.EnableNode)
        {
            failures.Add("CodeExecution must enable at least one runtime (CodeExecution:EnablePython or CodeExecution:EnableNode)");
        }

        if (options.TimeoutMs <= 0) failures.Add("CodeExecution:TimeoutMs must be > 0");
        if (options.MaxOutputBytes <= 0) failures.Add("CodeExecution:MaxOutputBytes must be > 0");
        if (options.MaxCodeChars <= 0) failures.Add("CodeExecution:MaxCodeChars must be > 0");

        if (options.EnablePython && string.IsNullOrWhiteSpace(options.PythonExecutable))
        {
            failures.Add("CodeExecution:PythonExecutable is required when CodeExecution:EnablePython=true");
        }

        if (options.EnableNode && string.IsNullOrWhiteSpace(options.NodeExecutable))
        {
            failures.Add("CodeExecution:NodeExecutable is required when CodeExecution:EnableNode=true");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
