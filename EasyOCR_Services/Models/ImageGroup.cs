using System;
using System.Collections.Generic;
using System.Text;

namespace EasyOCR_Services.Models
{
    public record ImageGroup(
        string GroupId,
        IReadOnlyList<string> ImagePaths,   // ordered: page 1, 2, 3...
        string OutputPdfPath
    );
}
