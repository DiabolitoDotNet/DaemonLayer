using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class VoiceApi
{
    public static void Map(WebApplication app, VoiceInterfaceOptions voiceOptions)
    {
        if (!voiceOptions.Enabled)
        {
            return;
        }

        static bool IsAllowed(HttpContext ctx, VoiceInterfaceOptions options)
            => !options.LocalOnly || LoopbackGuard.IsLoopback(ctx.Connection.RemoteIpAddress);

        static string ResolveRootDirectory(string rootDirectory)
        {
            var root = string.IsNullOrWhiteSpace(rootDirectory) ? "data/voice" : rootDirectory;
            return Path.IsPathRooted(root) ? root : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), root));
        }

        static string GetContentType(string path)
        {
            var ext = Path.GetExtension(path);
            return ext.ToUpperInvariant() switch
            {
                ".WAV" => "audio/wav",
                ".MP3" => "audio/mpeg",
                ".OGG" => "audio/ogg",
                ".M4A" => "audio/mp4",
                _ => "application/octet-stream"
            };
        }

        app.MapPost("/api/voice/transcribe", async (
            HttpContext ctx,
            IToolRegistry tools,
            IOptions<VoiceTranscriptionToolOptions> stt,
            CancellationToken ct) =>
        {
            if (!IsAllowed(ctx, voiceOptions))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!ctx.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Expected multipart/form-data" });
            }

            var form = await ctx.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null && form.Files.Count > 0)
            {
                file = form.Files[0];
            }

            if (file is null)
            {
                return Results.BadRequest(new { error = "Missing form file (field name: file)" });
            }

            if (file.Length <= 0)
            {
                return Results.BadRequest(new { error = "Empty file" });
            }

            if (file.Length > voiceOptions.MaxUploadBytes)
            {
                return Results.BadRequest(new { error = $"File too large (max {voiceOptions.MaxUploadBytes} bytes)" });
            }

            var root = ResolveRootDirectory(stt.Value.RootDirectory);
            var uploadsDir = Path.Combine(root, "uploads");
            Directory.CreateDirectory(uploadsDir);

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

            var uploadPath = Path.Combine(uploadsDir, $"upload_{Guid.NewGuid():N}{ext}");

            await using (var fs = new FileStream(uploadPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                await file.CopyToAsync(fs, ct);
            }

            var result = await tools.ExecuteToolWithTrackingAsync(
                toolName: "audio_transcribe",
                parameters: new Dictionary<string, object> { ["path"] = uploadPath },
                agentId: "voice_api",
                agentRank: "interface",
                agentName: "voice_api",
                ct: ct);

            if (!result.Success)
            {
                return Results.Problem(title: "Transcription failed", detail: result.Error ?? "Unknown error", statusCode: 500);
            }

            return Results.Ok(new VoiceTranscribeResponse(
                transcript: result.Output,
                tool: "audio_transcribe",
                metadata: result.Metadata));
        });

        app.MapPost("/api/voice/speak", async (
            HttpContext ctx,
            IToolRegistry tools,
            CancellationToken ct) =>
        {
            if (!IsAllowed(ctx, voiceOptions))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var req = await ctx.Request.ReadFromJsonAsync<VoiceSpeakRequest>(cancellationToken: ct);
            if (req is null || string.IsNullOrWhiteSpace(req.Text))
            {
                return Results.BadRequest(new { error = "Missing request body: text" });
            }

            var result = await tools.ExecuteToolWithTrackingAsync(
                toolName: "tts_speak",
                parameters: new Dictionary<string, object> { ["text"] = req.Text },
                agentId: "voice_api",
                agentRank: "interface",
                agentName: "voice_api",
                ct: ct);

            if (!result.Success)
            {
                return Results.Problem(title: "TTS failed", detail: result.Error ?? "Unknown error", statusCode: 500);
            }

            var outputPath = result.Metadata.TryGetValue("output_path", out var raw) ? raw?.ToString() : result.Output;
            if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            {
                return Results.Problem(title: "TTS failed", detail: "No output audio file found", statusCode: 500);
            }

            var stream = File.OpenRead(outputPath);
            return Results.File(stream, contentType: GetContentType(outputPath), fileDownloadName: Path.GetFileName(outputPath));
        });
    }
}
