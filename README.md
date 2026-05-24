# Wrap EasyOCR for its Python Library into a .NET helper
EasyOcr is an excellent library to Ocr texts from images or scanned files using AI models. However, I found some bugs when trying to use it. As follows are the reason why I wanna wrap EasyOcr from Python to .NET. Full code was included in this github project for your reference.

## Motivation to do this
1. EasyOcrSharp (version 1.0.2) bundles Python.Included 3.11.6 internally and uses that instead of our system Python. This means our Python with EasyOCR already installed on our OS is completely ignored.

2. The embedded Python is a stripped-down distribution with no pip and no ensurepip. We cannot install EasyOCR into it - attempts to find or run python.exe inside the NuGet package folder failed entirely.

3. We tried setting EASYOCR_MODULE_PATH in Program.cs to point to our local AI models folder. But EasyOcrSharp's internal ModelDownloadManager overwrote it with its own path every time.

4. GetModelCachePath() inside EasyOcrSharp computed the model directory as
```sh
  return Path.Combine(_customPythonPath, "models");
```
So even when passing the correct Python path, models were looked for in the wrong folder.

5. Because of problems 1–4 combined - wrong Python, wrong model path, overwritten environment variable - ExtractTextFromImage consistently returned 0 OCR lines regardless of what we tried.

## Wrap Python script and Execute it in C# Code

```sh
    private const string OcrScript = """
        import sys, json, easyocr

        image_path  = sys.argv[1]
        model_dir   = sys.argv[2]
        languages   = sys.argv[3:]

        reader = easyocr.Reader(
            languages,
            model_storage_directory=model_dir,
            download_enabled=False,   # never download at runtime
            gpu=False
        )

        raw = reader.readtext(image_path, detail=1)

        lines = []
        for (bbox, text, conf) in raw:
            # Cast to native Python float — NumPy int32/float32 are not JSON serializable
            xs = [float(pt[0]) for pt in bbox]
            ys = [float(pt[1]) for pt in bbox]
            lines.append({
                "text":       text,
                "confidence": round(float(conf), 6),
                "minX":       round(min(xs), 2),
                "minY":       round(min(ys), 2),
                "maxX":       round(max(xs), 2),
                "maxY":       round(max(ys), 2),
            })

        print(json.dumps({"lines": lines}, ensure_ascii=False))
        """;
```
We write this script to our local project, and execute it accordingly.

```sh
  await File.WriteAllTextAsync(scriptPath, OcrScript, Encoding.UTF8, ct);
```
```sh
  var args = new StringBuilder();
  args.Append($"\"{scriptPath}\" \"{imagePath}\" \"{_modelDir}\"");
  foreach (var lang in languages)
      args.Append($" {lang}");
```


