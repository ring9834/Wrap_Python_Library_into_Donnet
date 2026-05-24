using System;
using System.Collections.Generic;
using System.Text;

namespace EasyOCR_Services.Models
{
    public record OcrTextBlock(
     string Text,
     double Confidence,
     double MinX,
     double MinY,
     double MaxX,
     double MaxY
    )
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public bool IsEmpty => Width <= 0 || Height <= 0;
    }
}
