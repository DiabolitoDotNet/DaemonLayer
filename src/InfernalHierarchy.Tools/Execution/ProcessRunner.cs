using System.Diagnostics;
using System.Text;

namespace InfernalHierarchy.Tools.Execution;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct);
}

public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int TimeoutMs,
    int MaxOutputBytes,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);

public sealed record ProcessRunResult(
    int ExitCode,
    bool TimedOut,
    string StdOut,
    string StdErr,
    bool Truncated,
    TimeSpan Duration);

public sealed class DefaultProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("FileName is required", nameof(request));
        }

        if (request.TimeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "TimeoutMs must be > 0");
        }

        if (request.MaxOutputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxOutputBytes must be > 0");
        }

        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in request.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        if (request.EnvironmentVariables != null)
        {
            foreach (var (k, v) in request.EnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(k) && v != null)
                {
                    psi.Environment[k] = v;
                }
            }
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };

        var startedAt = Stopwatch.GetTimestamp();

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start process");
        }

        var stdoutTask = ReadToEndLimitedAsync(process.StandardOutput, request.MaxOutputBytes, ct);
        var stderrTask = ReadToEndLimitedAsync(process.StandardError, request.MaxOutputBytes, ct);

        var timedOut = false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(request.TimeoutMs);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort.
            }
        }

        var (stdout, stdoutTruncated) = await stdoutTask.ConfigureAwait(false);
        var (stderr, stderrTruncated) = await stderrTask.ConfigureAwait(false);

        var duration = Stopwatch.GetElapsedTime(startedAt);

        return new ProcessRunResult(
            ExitCode: timedOut ? -1 : process.ExitCode,
            TimedOut: timedOut,
            StdOut: stdout,
            StdErr: stderr,
            Truncated: stdoutTruncated || stderrTruncated,
            Duration: duration);
    }

    private static async Task<(string Text, bool Truncated)> ReadToEndLimitedAsync(
        StreamReader reader,
        int maxBytes,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        var bytes = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            var chunk = buffer.AsSpan(0, read);
            var chunkBytes = Encoding.UTF8.GetByteCount(chunk);

            if (bytes + chunkBytes <= maxBytes)
            {
                sb.Append(chunk);
                bytes += chunkBytes;
                continue;
            }

            var remaining = maxBytes - bytes;
            if (remaining > 0)
            {
                var allowedChars = chunk.Length;

                // Binary-search-ish shrink loop to find a prefix that fits.
                while (allowedChars > 0 && Encoding.UTF8.GetByteCount(chunk[..allowedChars]) > remaining)
                {
                    allowedChars = allowedChars <= 8 ? allowedChars - 1 : allowedChars - (allowedChars / 8);
                }

                if (allowedChars > 0)
                {
                    sb.Append(chunk[..allowedChars]);
                }
            }

            return (sb.ToString(), true);
        }

        return (sb.ToString(), false);
    }
}
