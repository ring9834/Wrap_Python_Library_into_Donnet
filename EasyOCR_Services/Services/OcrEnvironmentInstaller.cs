using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Compression;

namespace EasyOCR_Services.Services;

/// <summary>
/// One-click offline installer — uses Python and EasyOCR packages
/// pre-bundled in AppBaseDirectory/packages. No internet required.
/// </summary>
public sealed class OcrEnvironmentInstaller
{
    private static readonly string PackagesDir = Path.Combine(AppContext.BaseDirectory, "packages");
    private static readonly string PythonZip = Path.Combine(PackagesDir, "python-embeddable.zip");
    private static readonly string ModelsDir = Path.Combine(AppContext.BaseDirectory, "models");

    private static readonly string PythonInstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyOCR", "Python");

    public static string PythonExePath => Path.Combine(PythonInstallDir, "python.exe");

    private readonly IProgress<(int Percent, string Message)>? _progress;
    private readonly ILogger? _logger;

    public OcrEnvironmentInstaller(
        IProgress<(int Percent, string Message)>? progress = null,
        ILogger? logger = null)
    {
        _progress = progress;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Check
    // -----------------------------------------------------------------------

    public static bool IsInstalled()
    {
        // Fast check — no Python process needed, no UI blocking
        if (!File.Exists(PythonExePath)) return false;

        // Check pip is present
        var sitePackages = Path.Combine(PythonInstallDir, "Lib", "site-packages");
        if (!Directory.Exists(sitePackages)) return false;

        // Check easyocr package folder exists
        var easyOcrDir = Path.Combine(sitePackages, "easyocr");
        if (!Directory.Exists(easyOcrDir)) return false;

        // Check model files exist
        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".EasyOCR", "model");

        return Directory.Exists(modelDir) &&
               Directory.EnumerateFiles(modelDir, "*.pth").Any();
    }

    public static void ValidatePackagesFolder()
    {
        var missing = new List<string>();

        if (!File.Exists(PythonZip))
            missing.Add(@"packages\python-embeddable.zip");

        if (!Directory.Exists(PackagesDir) ||
            Directory.GetFiles(PackagesDir, "pip-*.whl").Length == 0)
            missing.Add(@"packages\pip-*.whl  (run: pip download pip -d packages/)");

        if (!Directory.Exists(PackagesDir) ||
            !Directory.EnumerateFiles(PackagesDir, "*.whl").Any())
            missing.Add(@"packages\*.whl  (run: pip download easyocr -d packages/)");

        if (!Directory.Exists(ModelsDir) ||
            !Directory.EnumerateFiles(ModelsDir, "*.pth").Any())
            missing.Add(@"models\*.pth  (run download-models.py)");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                "The following required files are missing from the application folder:\n\n" +
                string.Join("\n", missing.Select(m => "  \u2022 " + m)) +
                "\n\nPlease reinstall the application.");
    }

    // -----------------------------------------------------------------------
    // Main install entry point
    // -----------------------------------------------------------------------

    public async Task InstallAsync(CancellationToken ct = default)
    {
        Report(0, "Checking installation...");

        if (IsInstalled())
        {
            Report(100, "OCR environment is already installed and ready.");
            return;
        }

        Report(5, "Validating bundled packages...");
        ValidatePackagesFolder();

        await InstallPythonAsync(ct);
        await InstallPipAsync(ct);
        await InstallEasyOcrAsync(ct);
        CopyModels();

        Report(100, "Installation complete! OCR is ready to use.");
    }

    // -----------------------------------------------------------------------
    // Step 1 — Extract bundled Python embeddable zip
    // -----------------------------------------------------------------------

    private async Task InstallPythonAsync(CancellationToken ct)
    {
        if (File.Exists(PythonExePath))
        {
            Report(25, "Python already installed — skipping.");
            return;
        }

        Report(10, "Installing Python...");
        Directory.CreateDirectory(PythonInstallDir);

        await Task.Run(() =>
            ZipFile.ExtractToDirectory(PythonZip, PythonInstallDir, overwriteFiles: true), ct);

        // Embeddable Python blocks site-packages via a ._pth file.
        // Uncomment 'import site' so pip-installed packages are discoverable.
        var pthFile = Directory.GetFiles(PythonInstallDir, "python*._pth").FirstOrDefault();
        if (pthFile != null)
        {
            var pthContent = await File.ReadAllTextAsync(pthFile, ct);
            pthContent = pthContent.Replace("#import site", "import site");
            await File.WriteAllTextAsync(pthFile, pthContent, ct);
            _logger?.LogDebug("Patched {PthFile} to enable site-packages.", pthFile);
        }

        Report(25, "Python installed.");
    }

    // -----------------------------------------------------------------------
    // Step 2 — Install pip from bundled .whl (no internet, no get-pip.py)
    // -----------------------------------------------------------------------

    private async Task InstallPipAsync(CancellationToken ct)
    {
        Report(30, "Setting up pip...");

        var (_, _, already) = RunProcess(PythonExePath, "-m pip --version");
        if (already == 0)
        {
            Report(40, "pip already available — skipping.");
            return;
        }

        var pipWhl = Directory.GetFiles(PackagesDir, "pip-*.whl").FirstOrDefault()
            ?? throw new InvalidOperationException(
                "pip wheel not found in packages folder.\n" +
                "Run: pip download pip -d packages/  on a machine with internet.");

        Report(33, "Installing pip by extracting wheel into site-packages...");

        // A .whl is just a zip — extract directly into Python\'s site-packages.
        // This is the most reliable approach for embeddable Python which has no ensurepip.
        var sitePackages = Path.Combine(PythonInstallDir, "Lib", "site-packages");
        Directory.CreateDirectory(sitePackages);
        await Task.Run(() =>
            ZipFile.ExtractToDirectory(pipWhl, sitePackages, overwriteFiles: true), ct);

        // Verify pip is now accessible
        var (_, stderr, exit) = RunProcess(PythonExePath, "-m pip --version");
        if (exit != 0)
            throw new InvalidOperationException("pip installation failed:\n" + stderr);

        Report(40, "pip installed.");
    }

    // -----------------------------------------------------------------------
    // Step 3 — Install EasyOCR from bundled .whl files
    // -----------------------------------------------------------------------

    private async Task InstallEasyOcrAsync(CancellationToken ct)
    {
        Report(42, "Checking EasyOCR...");

        var (_, _, already) = RunProcess(PythonExePath, "-c \"import easyocr\"");
        if (already == 0)
        {
            Report(85, "EasyOCR already installed — skipping.");
            return;
        }

        Report(45, "Installing EasyOCR from bundled packages (this may take a few minutes)...");

        var (_, stderr, exit) = await Task.Run(() => RunProcess(
            PythonExePath,
            $"-m pip install easyocr --no-index --find-links=\"{PackagesDir}\"",
            timeoutMinutes: 30), ct);

        if (exit != 0)
            throw new InvalidOperationException(
                "EasyOCR installation failed:\n" + stderr +
                "\n\nPlease make sure all required .whl files are in the packages folder.");

        Report(85, "EasyOCR installed.");
    }

    // -----------------------------------------------------------------------
    // Step 4 — Copy bundled model files to EasyOCR cache
    // -----------------------------------------------------------------------

    private void CopyModels()
    {
        Report(88, "Copying OCR model files...");

        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".EasyOCR", "model");

        Directory.CreateDirectory(targetDir);

        foreach (var src in Directory.GetFiles(ModelsDir, "*.pth"))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(src));
            if (!File.Exists(dest))
            {
                File.Copy(src, dest);
                _logger?.LogInformation("Copied model: {File}", Path.GetFileName(src));
            }
        }

        Report(95, "Model files ready.");
    }

    // -----------------------------------------------------------------------
    // Process helper
    // -----------------------------------------------------------------------

    private static (string Stdout, string Stderr, int ExitCode) RunProcess(
        string exe, string args, int timeoutMinutes = 5)
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
            }
        };

        process.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        process.StartInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit((int)TimeSpan.FromMinutes(timeoutMinutes).TotalMilliseconds);

        return (stdout, stderr, process.ExitCode);
    }

    private void Report(int percent, string message)
    {
        _logger?.LogInformation("[{Percent}%] {Message}", percent, message);
        _progress?.Report((percent, message));
    }
}