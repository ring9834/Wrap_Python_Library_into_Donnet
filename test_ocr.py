import easyocr
import sys

reader = easyocr.Reader(
    ['ch_sim'],
    model_storage_directory='./Ocr_Images_Pdfs/models',
    download_enabled=False  # force use local models only
)

# Replace with an actual image path you're trying to OCR
result = reader.readtext(r'D:\OOOTEST\OcrInput\111-222-333-001\0001.jpg')

if len(result) == 0:
    print("0 results — EasyOCR found nothing")
else:
    for (bbox, text, confidence) in result:
        print(f"Text: {text} | Confidence: {confidence:.2f}")