
namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class FileSystemToolOptionsValidator : IValidateOptions<FileSystemToolOptions>
{
    public ValidateOptionsResult Validate(string? name, FileSystemToolOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            failures.Add("FileSystem:RootDirectory is required when FileSystem:Enabled=true");
        }
        else
        {
            var root = options.RootDirectory;
            if (!Path.IsPathRooted(root))
            {
                // Allowed: relative paths; they'll be resolved by the Host at runtime.
                // Here we only validate it isn't obviously invalid.
                if (root.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    failures.Add("FileSystem:RootDirectory contains invalid path characters");
                }
            }
            else
            {
                if (!Directory.Exists(root))
                {
                    failures.Add("FileSystem:RootDirectory must exist when absolute path is provided");
                }
            }
        }

        if (options.MaxReadBytes <= 0) failures.Add("FileSystem:MaxReadBytes must be > 0");
        if (options.MaxWriteBytes <= 0) failures.Add("FileSystem:MaxWriteBytes must be > 0");
        if (options.MaxSearchFileBytes <= 0) failures.Add("FileSystem:MaxSearchFileBytes must be > 0");
        if (options.MaxSearchResults <= 0) failures.Add("FileSystem:MaxSearchResults must be > 0");
        if (options.MaxSearchFilesScanned <= 0) failures.Add("FileSystem:MaxSearchFilesScanned must be > 0");

        if (options.AllowedExtensions is null)
        {
            failures.Add("FileSystem:AllowedExtensions cannot be null");
        }
        else
        {
            for (var i = 0; i < options.AllowedExtensions.Count; i++)
            {
                var ext = options.AllowedExtensions[i];
                if (string.IsNullOrWhiteSpace(ext) || !ext.StartsWith('.'))
                {
                    failures.Add($"FileSystem:AllowedExtensions[{i}] must start with '.'");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
