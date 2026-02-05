using InfernalHierarchy.Tools.Notifications;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            return args[i + 1];
        }
    }

    return null;
}

static bool HasFlag(string[] args, string name) =>
    args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

var to = GetArg(args, "--to") ?? GetArg(args, "-t");
var subject = GetArg(args, "--subject") ?? GetArg(args, "-s") ?? "DaemonLayer email_send test";
var body = GetArg(args, "--body") ?? GetArg(args, "-b") ?? "This is a test email from DaemonLayer (email_send).";
var isHtml = HasFlag(args, "--html");

if (string.IsNullOrWhiteSpace(to))
{
    Console.Error.WriteLine("Usage: dotnet run --project src/InfernalHierarchy.ToolRunner -- --to <email> [--subject <text>] [--body <text>] [--html]");
    return 2;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var options = new EmailNotificationOptions();
configuration.GetSection("Email").Bind(options);

if (!options.Enabled)
{
    Console.Error.WriteLine("Email is disabled. Set Email:Enabled=true in user-secrets.");
    return 3;
}

var required = new (string Name, string Value)[]
{
    ("Email:Host", options.Host),
    ("Email:Username", options.Username),
    ("Email:Password", options.Password),
    ("Email:FromAddress", options.FromAddress)
};

var missing = required.Where(x => string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Name).ToList();
if (missing.Count > 0)
{
    Console.Error.WriteLine("Missing required Email settings: " + string.Join(", ", missing));
    return 4;
}

var emailOptions = Options.Create(options);
var sender = new SmtpEmailSender(emailOptions);
var tool = new EmailNotificationTool(emailOptions, sender, NullLogger<EmailNotificationTool>.Instance);

var result = await tool.ExecuteAsync(new Dictionary<string, object>
{
    ["to"] = to!,
    ["subject"] = subject,
    ["body"] = body,
    ["is_html"] = isHtml
});

if (result.Success)
{
    Console.WriteLine(result.Output);
    return 0;
}

Console.Error.WriteLine(result.Error ?? "Unknown error");
return 1;
