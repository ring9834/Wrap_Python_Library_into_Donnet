using System;
using System.Collections.Generic;
using System.Text;

namespace EasyOCR_Services.Models
{
    public record OcrPageResult(
        int PageIndex,
        string ImagePath,
        IReadOnlyList<OcrTextBlock> Blocks,
        int Width,
        int Height
    );
}
