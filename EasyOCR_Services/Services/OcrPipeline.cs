using EasyOCR_Services.Models;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace EasyOCR_Services.Services;

public sealed class OcrPipeline
{
    private readonly OcrServicePool _ocrService;
    private readonly SearchablePdfWriter _pdfWriter;

    private readonly int _maxConcurrency = Environment.ProcessorCount;
    private readonly int _queueCapacity = 30;

    public OcrPipeline(OcrServicePool ocrService, SearchablePdfWriter pdfWriter)
    {
        _ocrService = ocrService;
        _pdfWriter = pdfWriter;
    }

    public async Task ProcessGroupsAsync(
        IEnumerable<ImageGroup> groups,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Warming up OCR engine...");
        // await _ocrService.WarmupAsync(ct);

        var channel = Channel.CreateBounded<OcrJobItem>(
            new BoundedChannelOptions(_queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        var results = new ConcurrentDictionary<string, ConcurrentBag<OcrPageResult>>();

        // PRODUCER
        var producer = Task.Run(async () =>
        {
            foreach (var group in groups)
            {
                progress?.Report($"[同时正在处理件数 {groups.Count()}] 末件是 '{group.GroupId}' ({group.ImagePaths.Count} 页)");

                for (int i = 0; i < group.ImagePaths.Count; i++)
                {
                    await channel.Writer.WriteAsync(
                        new OcrJobItem(group.GroupId, i, group.ImagePaths[i], group.OutputPdfPath),
                        ct);
                }
            }

            channel.Writer.Complete();
        }, ct);

        // CONSUMERS
        var consumers = Enumerable.Range(0, _maxConcurrency).Select(workerId => Task.Run(async () =>
        {
            await foreach (var job in channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var result = await _ocrService.OcrImageAsync(job.ImagePath, job.PageIndex, ct);

                    var bag = results.GetOrAdd(job.GroupId, _ => new ConcurrentBag<OcrPageResult>());
                    bag.Add(result);
                }
                catch (Exception ex)
                {
                    progress?.Report($"[出现错误] OCR 失败: {job.ImagePath} → {ex.Message}");
                }
            }
        }, ct));

        await Task.WhenAll(consumers.Prepend(producer));

        // BUILD PDFs
        var buildTasks = groups.Select(async group =>
        {
            try
            {
                if (!results.TryGetValue(group.GroupId, out var bag))
                {
                    progress?.Report($"[警告] 件 '{group.GroupId} 未找到'");
                    return;
                }

                var ordered = bag.OrderBy(p => p.PageIndex).ToArray();
                await _pdfWriter.BuildAsync(group, ordered, ct);

                progress?.Report($"[OCR识别完成] 末件是'{group.GroupId}' 路径 → {group.OutputPdfPath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"[出现错误] PDF 生成失败！ 件'{group.GroupId}': {ex.Message}");
            }
        });

        await Task.WhenAll(buildTasks);
    }

    private record OcrJobItem(string GroupId, int PageIndex, string ImagePath, string OutputPdfPath);
}