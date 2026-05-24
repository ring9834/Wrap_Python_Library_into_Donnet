using EasyOCR_Services.Services;
using System.Diagnostics;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace Ocr_2_PDF_Tests_Large
{
    public partial class Form1 : Form
    {
        private bool isProcessing = false;
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {

            if (string.IsNullOrEmpty(textBox1.Text))
            {
                MessageBox.Show("请选择要生成PDF文件的图片源路径！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(textBox2.Text))
            {
                MessageBox.Show("请选择要存放PDF文件的目标路径！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (textBox1.Text == textBox2.Text)
            {
                MessageBox.Show("图片源路径和目标路径不能相同！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (isProcessing) return;

            isProcessing = true;
            label1.Text = "PDF文件正在生成中，请稍候...";
            // Schedules the lambda on a thread pool thread.Bypasses the current SynchronizationContext.
            Task.Run(async () => await TestMe_EasyOcr());
            // Captures and uses the current SynchronizationContext for continuations after await
            //_ = TestMe(); // Fire and forget
        }

        private async Task TestMe_EasyOcr()
        {
            var languages = new[] { "ch_sim", "en" };
            int maxConcurrency = Environment.ProcessorCount;   // parallel OCR workers

            bool pathType = false;// false:一致路径；true: 统一路径
            pathType = radioButton1.Checked ? true : false;

            Stopwatch stopwatch = Stopwatch.StartNew();
            stopwatch.Start();

            string inputRoot = textBox1.Text;
            string outputRoot = textBox2.Text;

            await using var ocrService = new OcrServicePool(maxConcurrency, languages);
            var pdfBuilder = new SearchablePdfWriter();
            var pipeline = new OcrPipeline(ocrService, pdfBuilder);
            var processor = new EasyOCR_Services.Services.BatchDirectoryProcessor();

            using var cts = new CancellationTokenSource();
            var progress = new Progress<string>(msg =>
            {
                //Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}")
                //label2.Text = msg;
                label2.Invoke((MethodInvoker)(() => label2.Text = msg));
            });            

            foreach (var batch in processor.GetBatches(inputRoot, outputRoot, pathType, 10))
            {
                await pipeline.ProcessGroupsAsync(batch, progress, cts.Token);
            }

            isProcessing = false;
            stopwatch.Stop();
            label1.Invoke((MethodInvoker)(() => label1.Text = $"PDF生成已完成，所用时间: {stopwatch.Elapsed.TotalMinutes.ToString("F2")}分钟"));
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            label1.Text = "";
        }

        private void textBox1_Click(object sender, EventArgs e)
        {
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                textBox1.Text = fbd.SelectedPath;
            }
        }

        private void textBox2_Click(object sender, EventArgs e)
        {
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                textBox2.Text = fbd.SelectedPath;
            }
        }
    }
}
