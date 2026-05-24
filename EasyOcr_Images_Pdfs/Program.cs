using EasyOCR_Services.Services;
using log4net;
using log4net.Config;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Ocr_2_PDF_Tests_Large
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // Run IsInstalled on background thread — even file I/O shouldn't block UI
            var installed = OcrEnvironmentInstaller.IsInstalled();

            if (!installed)
            {
                using var installForm = new InstallOcrForm();
                if (installForm.ShowDialog() != DialogResult.OK)
                    return;
            }

            // ── Bootstrap log4net
            var logRepo = LogManager.GetRepository(Assembly.GetEntryAssembly()!);
            var configFile = new FileInfo("log4net.config");
            XmlConfigurator.ConfigureAndWatch(logRepo, configFile); // ConfigureAndWatch means it reloads automatically if we edit the config file while the app is running — useful during dev.

            // await TestEasyOcrDirectly();

            Application.Run(new Form1());
        }

        private static async Task TestEasyOcrDirectly()
        {
            var wrapper = new EasyOcrWrapper(
                pythonExe: @"D:\Installed\Python312\python.exe",
                modelDir: Path.Combine(AppContext.BaseDirectory, "models")
            );

            var result = await wrapper.ExtractTextFromImageAsync(
                @"D:\OOOTEST\OcrInput\111-222-333-001\0001.jpg",
                new[] { "ch_sim" }
            );

            MessageBox.Show(
                $"Lines found: {result.Lines.Count}\n" +
                string.Join("\n", result.Lines.Select(l => l.Text)),
                "EasyOcrWrapper Test");
        }
    }
}