using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyOCR_Services.Services;

/// <summary>
/// Replaces EasyOcrSharp by calling your local Python/EasyOCR installation directly.
/// Drop-in compatible with OcrServicePool — just swap EasyOcrService for EasyOcrWrapper.
/// </summary>
public sealed class EasyOcrWrapper : IDisposable
{
    private readonly string _pythonExe;
    private readonly string _modelDir;
    private readonly ILogger? _logger;
    private bool _disposed;

    // Inline Python script — no temp .py file needed
    private const string OcrScript = """
        import sys, json, easyocr

        image_path  = sys.argv[1]
        model_dir   = sys.argv[2]
        languages   = sys.argv[3:]

        reader = easyocr.Reader(
            languages,
            model_storage_directory=model_dir,
            download_enabled=False,   # never download at runtime
            gpu=False
        )

        raw = reader.readtext(image_path, detail=1)

        lines = []
        for (bbox, text, conf) in raw:
            # Cast to native Python float — NumPy int32/float32 are not JSON serializable
            xs = [float(pt[0]) for pt in bbox]
            ys = [float(pt[1]) for pt in bbox]
            lines.append({
                "text":       text,
                "confidence": round(float(conf), 6),
                "minX":       round(min(xs), 2),
                "minY":       round(min(ys), 2),
                "maxX":       round(max(xs), 2),
                "maxY":       round(max(ys), 2),
            })

        print(json.dumps({"lines": lines}, ensure_ascii=False))
        """;

    /// <param name="pythonExe">Full path to python.exe, e.g. D:\Installed\Python312\python.exe</param>
    /// <param name="modelDir">Folder containing .pth model files, e.g. bin\models</param>
    /// <param name="logger">Optional logger.</param>
    public EasyOcrWrapper(string pythonExe, string modelDir, ILogger? logger = null)
    {
        if (!File.Exists(pythonExe))
            throw new FileNotFoundException($"Python executable not found: '{pythonExe}'", pythonExe);

        if (!Directory.Exists(modelDir) || !Directory.EnumerateFiles(modelDir, "*.pth").Any())
            throw new InvalidOperationException(
                $"No .pth model files found in '{modelDir}'. Run download-models.py first.");

        _pythonExe = pythonExe;
        _modelDir = modelDir;
        _logger = logger;
    }

    /// <summary>
    /// Runs EasyOCR on the given image and returns structured results.
    /// Compatible with the OcrResult shape OcrServicePool expects.
    /// </summary>
    public async Task<OcrWrapperResult> ExtractTextFromImageAsync(
        string imagePath,
        string[] languages,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image not found: '{imagePath}'", imagePath);

        if (languages is null || languages.Length == 0)
            throw new ArgumentException("At least one language must be specified.", nameof(languages));

        // Write the inline script to a temp file
        var scriptPath = Path.Combine(Path.GetTempPath(), $"easyocr_{Guid.NewGuid():N}.py");
        try
        {
            await File.WriteAllTextAsync(scriptPath, OcrScript, Encoding.UTF8, ct);

            // Build args: script imagePath modelDir lang1 lang2 ...
            var args = new StringBuilder();
            args.Append($"\"{scriptPath}\" \"{imagePath}\" \"{_modelDir}\"");
            foreach (var lang in languages)
                args.Append($" {lang}");

            var sw = Stopwatch.StartNew();
            var (stdout, stderr, exitCode) = await RunProcessAsync(_pythonExe, args.ToString(), ct);
            sw.Stop();

            // Only fail on non-zero exit code — PyTorch always prints "Using CPU" and
            // pin_memory warnings to stderr which are harmless and expected.
            if (exitCode != 0)
            {
                _logger?.LogError("EasyOCR Python error (exit {Code}):\n{Stderr}", exitCode, stderr);
                throw new InvalidOperationException(
                    $"EasyOCR failed (exit code {exitCode}):\n{stderr}");
            }

            // Log stderr as debug only — never treat warnings as errors
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger?.LogDebug("EasyOCR warnings (non-fatal):\n{Stderr}", stderr);

            // Empty stdout after exit 0 = no text found in image, which is valid
            if (string.IsNullOrWhiteSpace(stdout))
            {
                _logger?.LogWarning("EasyOCR returned no text for image: {Image}", Path.GetFileName(imagePath));
                return new OcrWrapperResult(new List<OcrWrapperLine>());
            }

            var result = JsonSerializer.Deserialize<OcrWrapperResult>(stdout,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new OcrWrapperResult(new List<OcrWrapperLine>());

            _logger?.LogInformation(
                "OCR completed: {LineCount} lines in {Ms}ms | image={Image}",
                result.Lines.Count, sw.ElapsedMilliseconds, Path.GetFileName(imagePath));

            return result;
        }
        finally
        {
            // Always clean up temp script
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
    }

    // ---------- Process helper ----------

    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunProcessAsync(
        string exe, string args, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };

        // Force Python to use UTF-8 for stdout/stderr — critical for Chinese characters.
        // Without this, Windows defaults to cp1252 which cannot encode CJK characters.
        process.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        process.StartInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

        process.Start();

        // Read stdout and stderr concurrently to avoid deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return (stdoutTask.Result, stderrTask.Result, process.ExitCode);
    }

    public void Dispose() => _disposed = true;
}

// ---------- Result models ----------

public record OcrWrapperResult(
    [property: JsonPropertyName("lines")] List<OcrWrapperLine> Lines);

public record OcrWrapperLine(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("minX")] double MinX,
    [property: JsonPropertyName("minY")] double MinY,
    [property: JsonPropertyName("maxX")] double MaxX,
    [property: JsonPropertyName("maxY")] double MaxY);
