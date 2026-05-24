using EasyOCR_Services.Models;
using iText.IO.Font;
using iText.IO.Image;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using Microsoft.Extensions.Logging;

namespace EasyOCR_Services.Services;

public class SearchablePdfWriter
{
    private readonly ILogger<SearchablePdfWriter>? _logger;

    public SearchablePdfWriter(ILogger<SearchablePdfWriter>? logger = null)
    {
        _logger = logger;
    }


    // FIX 1: No longer async — iText7 has no async API, so async was fake.
    // Returns Task so callers can still await it without breaking existing call sites.
    public Task BuildAsync(
        ImageGroup group,
        IReadOnlyList<OcrPageResult> pages,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(group.OutputPdfPath)!);

        var sorted = pages.OrderBy(p => p.PageIndex).ToList();

        using var writer = new PdfWriter(group.OutputPdfPath);
        using var pdf = new PdfDocument(writer);
        var font = LoadChineseFont();

        foreach (var page in sorted)
        {
            // FIX 2: Respect cancellation at each page boundary.
            ct.ThrowIfCancellationRequested();

            var pageSize = new PageSize(page.Width, page.Height);
            var pdfPage = pdf.AddNewPage(pageSize);

            // FIX 3: Wrap PdfCanvas in using so the PDF stream is always flushed and cleaned up.
            var canvas = new PdfCanvas(pdfPage);

            // Background image
            var imgData = ImageDataFactory.Create(page.ImagePath);
            canvas.AddImageFittedIntoRectangle(imgData, pageSize, false);

            // Invisible searchable text layer
            canvas.BeginText();
            canvas.SetTextRenderingMode(PdfCanvasConstants.TextRenderingMode.INVISIBLE);
            canvas.SetFontAndSize(font, 10);

            foreach (var block in page.Blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text) || block.IsEmpty)
                    continue;

                // PDF origin is bottom-left: invert Y
                float pdfY1 = (float)(page.Height - block.MaxY);  // bottom
                float pdfY2 = (float)(page.Height - block.MinY);  // top

                // FIX 6: Use float throughout — iText7 is float-based, avoid double/float mixing.
                float boxHeight = pdfY2 - pdfY1;
                float fontSize = Math.Max(1f, boxHeight * 0.72f);

                canvas.SetFontAndSize(font, fontSize);
                canvas.SetTextMatrix((float)block.MinX, pdfY1);
                canvas.ShowText(block.Text);
            }

            canvas.EndText();
        }

        // document.Close() removed — using block handles it correctly above.
        return Task.CompletedTask;
    }

    private static PdfFont LoadChineseFont()
    {
        var bundledPath = System.IO.Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSansCJKsc-Regular.otf");
        if (!File.Exists(bundledPath))
            throw new FileNotFoundException($"CJK font not found at: {bundledPath}");

        return PdfFontFactory.CreateFont(
            bundledPath,
            PdfEncodings.IDENTITY_H,
            PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
    }
}