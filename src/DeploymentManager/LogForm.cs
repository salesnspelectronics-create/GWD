using System.Text;

namespace GenericWeb.DeploymentManager;

public sealed class LogForm : Form
{
    private const int MaxUiChars = 1_000_000;
    private readonly RichTextBox _box = new() { ReadOnly=true, ScrollBars=RichTextBoxScrollBars.Both, WordWrap=false, Dock=DockStyle.Fill, Font=new Font("Consolas",9.5F), BackColor=Color.White };
    private readonly string _logFile;
    private long _offset;
    private bool _followLatest = true;

    public LogForm(string logFile)
    {
        _logFile = logFile;
        Text="Deployment Manager — View Log"; Width=1100; Height=720; StartPosition=FormStartPosition.CenterParent;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        Controls.Add(_box);
        var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=44,Padding=new Padding(8)};
        var refresh=new Button{Text="Refresh",AutoSize=true};
        var open=new Button{Text="Open log folder",AutoSize=true};
        var dump=new Button{Text="Dump current log to Desktop",AutoSize=true};
        var latest=new Button{Text="Jump to Latest",AutoSize=true};
        top.Controls.Add(refresh); top.Controls.Add(latest); top.Controls.Add(open); top.Controls.Add(dump); Controls.Add(top); top.BringToFront();

        refresh.Click += (_,_) => { _offset = 0; _box.Clear(); _followLatest = true; AppendNewLog(initial:true); };
        latest.Click += (_,_) => { _followLatest = true; _box.SelectionStart = _box.TextLength; _box.ScrollToCaret(); };
        _box.VScroll += (_,_) => { if (_box.SelectionStart < Math.Max(0, _box.TextLength - 2)) _followLatest = false; };
        open.Click += (_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(logFile)!){UseShellExecute=true});
        dump.Click += (_,_) => DumpCurrentLog();
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_,_) => AppendNewLog();
        Shown += (_,_) => { AppendNewLog(initial:true); timer.Start(); };
        FormClosed += (_,_) => { timer.Stop(); timer.Dispose(); };
    }

    private void AppendColorized(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (var raw in lines)
        {
            if (raw.Length == 0) continue;
            var color = Color.DimGray;
            var fontStyle = FontStyle.Regular;
            var low = raw.ToLowerInvariant();
            if (low.Contains("[stage-start]")) { color = Color.RoyalBlue; fontStyle = FontStyle.Bold; }
            else if (low.Contains("[stage-end]")) { color = Color.ForestGreen; fontStyle = FontStyle.Bold; }
            else if (low.Contains("[stage-separator]")) color = Color.SteelBlue;
            else if (low.Contains("error") || low.Contains("failed") || low.Contains("unsuccessful")) { color = Color.Firebrick; fontStyle = FontStyle.Bold; }
            else if (low.Contains("warning") || low.Contains("warn") || low.Contains("cleanup warning")) color = Color.DarkOrange;
            else if (low.Contains("successful") || low.Contains("success") || low.Contains("exit code 0")) color = Color.ForestGreen;
            else if (low.Contains("[cancel]")) color = Color.DarkOrange;
            _box.SelectionStart = _box.TextLength;
            _box.SelectionColor = color;
            _box.SelectionFont = new Font(_box.Font, fontStyle);
            _box.AppendText(raw + Environment.NewLine);
        }
        _box.SelectionColor = _box.ForeColor;
        _box.SelectionFont = _box.Font;
    }

    private void AppendNewLog(bool initial = false)
    {
        try
        {
            if (!File.Exists(_logFile)) { if (_box.TextLength == 0) _box.Text = "No log entries yet."; return; }
            var info = new FileInfo(_logFile);
            if (info.Length < _offset) { _offset = 0; _box.Clear(); }
            if (info.Length == _offset) return;
            if (initial && _offset == 0)
            {
                // Open directly on a compact tail so a large log never visibly races from line one to the end.
                _offset = Math.Max(0, info.Length - 200_000);
                if (_offset > 0) AppendColorized("[... opened at the latest log lines; use the log file to inspect older content ...]\r\n");
            }
            using var fs = new FileStream(_logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(_offset, SeekOrigin.Begin);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
            var chunk = sr.ReadToEnd();
            _offset = fs.Position;
            if (chunk.Length == 0) return;
            AppendColorized(chunk);
            if (_box.TextLength > MaxUiChars)
            {
                var keepFrom = _box.TextLength - (MaxUiChars / 2);
                _box.Select(0, keepFrom);
                _box.SelectedText = "[... older log lines hidden in this window; complete log is still on disk ...]" + Environment.NewLine;
            }
            if (_followLatest || initial) { _box.SelectionStart=_box.TextLength; _box.ScrollToCaret(); }
        }
        catch(Exception ex) { AppendColorized($"[Log viewer error] {ex.Message}"); }
    }

    private void DumpCurrentLog()
    {
        try
        {
            if (!File.Exists(_logFile)) { MessageBox.Show(this, "There is no current log file yet.", "Log dump", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var destination = Path.Combine(desktop, $"NSP-Deployment-Log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.Copy(_logFile, destination, overwrite: true);
            MessageBox.Show(this, $"Current deployment log was copied to Desktop:\r\n{destination}", "Log dump created", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Unable to create log dump", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
