import easyocr

# Traditional Chinese + English
reader_tra = easyocr.Reader(
    ['ch_tra', 'en'],
    model_storage_directory='./models',
    download_enabled=True
)

# Simplified Chinese + English
reader_sim = easyocr.Reader(
    ['ch_sim', 'en'],
    model_storage_directory='./models',
    download_enabled=True
)

print("Done! Models saved to ./models")