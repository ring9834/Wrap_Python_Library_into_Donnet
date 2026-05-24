using EasyOCR_Services.Services;

namespace Ocr_2_PDF_Tests_Large;

/// <summary>
/// Simple one-click installer dialog.
/// Show this on startup if OcrEnvironmentInstaller.IsInstalled() returns false.
/// </summary>
public partial class InstallOcrForm : Form
{
    //private readonly OcrEnvironmentInstaller _installer;
    //private bool _installComplete;

    public InstallOcrForm()
    {
        InitializeComponent();
    }

    private async void btnInstall_Click(object sender, EventArgs e)
    {
        btnInstall.Enabled = false;
        progressBar.Value  = 0;

        try
        {
            // Progress<T> created HERE on the UI thread
            // It captures the UI SynchronizationContext correctly
            var progress = new Progress<(int Percent, string Message)>(report =>
            {
                if (!IsDisposed)
                {
                    progressBar.Value = report.Percent;
                    lblStatus.Text = report.Message;
                }
            });

            var installer = new OcrEnvironmentInstaller(progress);

            // Only the actual work goes in Task.Run
            await Task.Run(() => installer.InstallAsync());

            //_installComplete = true;
            MessageBox.Show(
                "OCR environment installed successfully!\nThe application will now start.",
                "Installation Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Installation failed:\n\n{ex.Message}\n\nPlease contact support.",
                "Installation Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            btnInstall.Enabled = true;
        }
    }

    // -----------------------------------------------------------------------
    // Designer-equivalent layout — no .Designer.cs file needed
    // -----------------------------------------------------------------------
    private ProgressBar progressBar = null!;
    private Label       lblStatus   = null!;
    private Button      btnInstall  = null!;

    private void InitializeComponent()
    {
        Text            = "OCR Setup";
        Size            = new Size(540, 290);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;

        var lblTitle = new Label
        {
            Text      = "OCR Environment Setup",
            Font      = new Font("Segoe UI", 12, FontStyle.Bold),
            Location  = new Point(20, 20),
            Size      = new Size(440, 28),
        };

        var lblDesc = new Label
        {
            Text     = "Click Install to set up Python and EasyOCR automatically.\n" +
                       "This only needs to be done once. Internet required for first install.",
            Location = new Point(20, 55),
            Size     = new Size(440, 40),
        };

        progressBar = new ProgressBar
        {
            Location = new Point(20, 110),
            Size     = new Size(440, 22),
            Minimum  = 0,
            Maximum  = 100,
        };

        lblStatus = new Label
        {
            Text     = "Ready to install.",
            Location = new Point(20, 138),
            Size     = new Size(440, 20),
            ForeColor = Color.DimGray,
        };

        btnInstall = new Button
        {
            Text     = "Install",
            Location = new Point(360, 165),
            Size     = new Size(100, 32),
            Font     = new Font("Segoe UI", 9),
        };
        btnInstall.Click += btnInstall_Click;

        Controls.AddRange(new Control[]
            { lblTitle, lblDesc, progressBar, lblStatus, btnInstall });
    }
}
