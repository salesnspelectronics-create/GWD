using System.IO.Compression;
using System.Text;

namespace GenericWeb.DeploymentManager;

public sealed class ErrorReportForm : Form
{
    private readonly string _errorReportPath;
    private readonly string _sessionLogPath;
    private readonly string _stateFilePath;
    private readonly RichTextBox _reportBox = new()
    {
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = RichTextBoxScrollBars.Both,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9.5F),
        BackColor = Color.White
    };

    public ErrorReportForm(string errorReportPath, string sessionLogPath, string stateFilePath)
    {
        _errorReportPath = errorReportPath;
        _sessionLogPath = sessionLogPath;
        _stateFilePath = stateFilePath;

        Text = "Deployment Manager — Error Report";
        Width = 1120;
        Height = 760;
        MinimumSize = new Size(880, 560);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8),
            WrapContents = false
        };
        var dump = new Button { Text = "Dump Error Report", AutoSize = true, Height = 30 };
        var openFolder = new Button { Text = "Open Report Folder", AutoSize = true, Height = 30 };
        var close = new Button { Text = "Close", AutoSize = true, Height = 30 };
        top.Controls.Add(dump);
        top.Controls.Add(openFolder);
        top.Controls.Add(close);

        Controls.Add(_reportBox);
        Controls.Add(top);
        top.BringToFront();

        dump.Click += (_, _) => DumpDiagnosticBundle();
        openFolder.Click += (_, _) => OpenReportFolder();
        close.Click += (_, _) => Close();
        Shown += (_, _) => LoadReport();
    }

    private void LoadReport()
    {
        try
        {
            if (!File.Exists(_errorReportPath))
            {
                _reportBox.Text = "The diagnostic error report file no longer exists.";
                return;
            }
            _reportBox.Text = File.ReadAllText(_errorReportPath);
            _reportBox.SelectionStart = 0;
            _reportBox.SelectionLength = 0;
        }
        catch (Exception ex)
        {
            _reportBox.Text = "Unable to read the diagnostic error report.\r\n\r\n" + GenericWeb.DeploymentManager.Services.SecretRedactor.Redact(ex.Message);
        }
    }

    private void DumpDiagnosticBundle()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var destination = Path.Combine(desktop, $"Deployment-Error-Dump-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            var temp = Path.Combine(Path.GetTempPath(), "GenericWebDeploymentManager", "ErrorDump-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            try
            {
                CopyIfExists(_errorReportPath, Path.Combine(temp, "ErrorReport.txt"));
                CopyIfExists(_sessionLogPath, Path.Combine(temp, "Session.log"));
                CopyIfExists(_stateFilePath, Path.Combine(temp, "deployment-state.json"));

                var manifest = new StringBuilder();
                manifest.AppendLine("Generic Web Deployment Manager — Diagnostic Dump");
                manifest.AppendLine($"Created: {DateTime.Now:O}");
                manifest.AppendLine("Contents are intended for deployment debugging.");
                manifest.AppendLine("Passwords and SQL credentials are not intentionally included by the diagnostic report generator.");
                manifest.AppendLine();
                manifest.AppendLine("Included files:");
                foreach (var file in Directory.EnumerateFiles(temp).OrderBy(file => Path.GetFileName(file)))
                    manifest.AppendLine("- " + Path.GetFileName(file));
                File.WriteAllText(Path.Combine(temp, "manifest.txt"), manifest.ToString());

                if (File.Exists(destination)) File.Delete(destination);
                ZipFile.CreateFromDirectory(temp, destination, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch (Exception ex) { Services.InternalDiagnostics.Record(ex, "Cleanup legacy error dump temp folder"); }
            }

            MessageBox.Show(this,
                "Diagnostic error dump created on Desktop:\r\n" + destination,
                "Error dump created", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, GenericWeb.DeploymentManager.Services.SecretRedactor.Redact(ex.Message), "Unable to create error dump", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenReportFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(_errorReportPath);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, GenericWeb.DeploymentManager.Services.SecretRedactor.Redact(ex.Message), "Unable to open report folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void CopyIfExists(string source, string destination)
    {
        if (File.Exists(source)) File.WriteAllText(destination, Services.SecretRedactor.Redact(File.ReadAllText(source)));
    }
}
