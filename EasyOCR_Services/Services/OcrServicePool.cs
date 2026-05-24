using EasyOCR_Services.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace EasyOCR_Services.Services;

public sealed class OcrServicePool : IAsyncDisposable
{
    private readonly int _maxSize;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentQueue<EasyOcrWrapper> _pool = new();
    private readonly string[] _languages;
    private readonly string _pythonExe;
    private readonly string _modelDir;
    private readonly ILogger? _logger;
    private volatile bool _disposed;

    /// <param name="maxSize">Max concurrent OCR operations.</param>
    /// <param name="languages">EasyOCR language codes, e.g. ["ch_sim", "en"].</param>
    /// <param name="pythonExe">Full path to python.exe, e.g. D:\Installed\Python312\python.exe</param>
    /// <param name="modelDir">Folder containing .pth model files. Defaults to bin\models.</param>
    /// <param name="logger">Optional logger.</param>
    public OcrServicePool(
        int maxSize,
        string[] languages,
        string pythonExe = @"D:\Installed\Python312\python.exe",
        string? modelDir = null,
        ILogger? logger = null)
    {
        _maxSize = maxSize > 0 ? maxSize : throw new ArgumentOutOfRangeException(nameof(maxSize));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _pythonExe = pythonExe ?? OcrEnvironmentInstaller.PythonExePath;
        _modelDir = modelDir ?? Path.Combine(AppContext.BaseDirectory, "models");
        _semaphore = new SemaphoreSlim(maxSize, maxSize);
        _logger = logger;
    }

    /// <summary>
    /// OCR a single image and return a page result.
    /// </summary>
    public async Task<OcrPageResult> OcrImageAsync(
        string imagePath,
        int pageIndex,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var rented = await RentAsync(ct).ConfigureAwait(false);

        // Read image dimensions and run OCR concurrently
        var ocrTask = rented.Instance.ExtractTextFromImageAsync(imagePath, _languages, ct);
        var (width, height) = ReadImageDimensions(imagePath);

        var ocrResult = await ocrTask.ConfigureAwait(false);

        return new OcrPageResult(
            PageIndex: pageIndex,
            ImagePath: imagePath,
            Blocks: ocrResult.Lines.Select(line => new OcrTextBlock(
                Text: line.Text,
                Confidence: line.Confidence,
                MinX: line.MinX,
                MinY: line.MinY,
                MaxX: line.MaxX,
                MaxY: line.MaxY
            )).ToList(),
            Width: width,
            Height: height
        );
    }

    // ---------- Pool implementation ----------

    private async Task<IRentedOcr> RentAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pool.TryDequeue(out var existing))
                return new RentedOcrHandle(this, existing);

            return new RentedOcrHandle(this, new EasyOcrWrapper(_pythonExe, _modelDir, _logger));
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    private void Return(EasyOcrWrapper instance)
    {
        _pool.Enqueue(instance);
        _semaphore.Release();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        while (_pool.TryDequeue(out var instance))
            instance.Dispose();
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---------- Image dimension helpers ----------

    private static (int Width, int Height) ReadImageDimensions(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 24);
        int read = fs.Read(header);

        // PNG
        if (read >= 24
            && header[0] == 0x89 && header[1] == (byte)'P'
            && header[2] == (byte)'N' && header[3] == (byte)'G')
        {
            int w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return (w, h);
        }

        // JPEG
        if (read >= 2 && header[0] == 0xFF && header[1] == 0xD8)
            return ReadJpegDimensions(path);

        throw new NotSupportedException($"Unsupported image format: {path}");
    }

    private static (int Width, int Height) ReadJpegDimensions(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 512);
        Span<byte> buf = stackalloc byte[4];

        fs.Seek(2, SeekOrigin.Begin);
        while (fs.Position < fs.Length - 8)
        {
            if (fs.ReadByte() != 0xFF) continue;
            int marker = fs.ReadByte();

            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                fs.Seek(3, SeekOrigin.Current);
                fs.Read(buf);
                int h = (buf[0] << 8) | buf[1];
                int w = (buf[2] << 8) | buf[3];
                return (w, h);
            }

            if (fs.Read(buf[..2]) < 2) break;
            int segLen = (buf[0] << 8) | buf[1];
            fs.Seek(segLen - 2, SeekOrigin.Current);
        }

        throw new InvalidDataException($"Could not find SOF marker in JPEG: {path}");
    }

    // ---------- Inner types ----------

    private sealed class RentedOcrHandle : IRentedOcr
    {
        private readonly OcrServicePool _pool;
        public EasyOcrWrapper Instance { get; }

        public RentedOcrHandle(OcrServicePool pool, EasyOcrWrapper instance)
        {
            _pool = pool;
            Instance = instance;
        }

        public void Dispose() => _pool.Return(Instance);
    }
}

public interface IRentedOcr : IDisposable
{
    EasyOcrWrapper Instance { get; }
}
