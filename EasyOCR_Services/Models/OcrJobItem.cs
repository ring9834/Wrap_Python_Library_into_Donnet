using System;
using System.Collections.Generic;
using System.Text;

namespace EasyOCR_Services.Models
{
    // ---- Internal helper for the pipeline ----
    public record OcrJobItem(
    string GroupId,
    int PageIndex,
    string ImagePath,
    string OutputPdfPath
);
}
