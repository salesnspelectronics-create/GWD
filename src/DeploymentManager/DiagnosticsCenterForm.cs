using GenericWeb.DeploymentManager.Models;
using GenericWeb.DeploymentManager.Services;

namespace GenericWeb.DeploymentManager;

public sealed class DiagnosticsCenterForm : Form
{
    private readonly AppDataService _appData;
    private readonly DeploymentHistoryService _history;
    private readonly DeploymentProfile _profile;
    private readonly DeploymentService _deployment;
    private readonly string _currentLog;
    private readonly DiagnosticBundleService _bundle = new();
    private readonly RuntimeMetricsService _runtimeMetrics;
    private readonly System.Windows.Forms.Timer _metricsTimer = new() { Interval = 2000 };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<TabPage, DiagnosticExportSection> _sectionByTab = new();

    public DiagnosticsCenterForm(AppDataService appData, DeploymentHistoryService history, DeploymentProfile profile, DeploymentService deployment, string currentLog, RuntimeMetricsService runtimeMetrics)
    {
        _appData = appData;
        _history = history;
        _profile = profile;
        _deployment = deployment;
        _currentLog = currentLog;
        _runtimeMetrics = runtimeMetrics;

        Text = $"Diagnostics & Deployment History Center — {ProductInfo.DisplayVersion}";
        Width = 1260;
        Height = 820;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 8, 10, 6), WrapContents = true };
        var exportTab = new Button { Text = "Current Tab → Desktop", Width = 165, Height = 34 };
        var exportTabOther = new Button { Text = "Current Tab → Other...", Width = 170, Height = 34 };
        var exportAll = new Button { Text = "All Diagnostics → Desktop", Width = 180, Height = 34 };
        var exportAllOther = new Button { Text = "All Diagnostics → Other...", Width = 185, Height = 34 };
        exportTab.Click += (_, _) => ExportCurrentTab(toDesktop: true);
        exportTabOther.Click += (_, _) => ExportCurrentTab(toDesktop: false);
        exportAll.Click += (_, _) => ExportSection(DiagnosticExportSection.All, toDesktop: true);
        exportAllOther.Click += (_, _) => ExportSection(DiagnosticExportSection.All, toDesktop: false);
        toolbar.Controls.Add(exportTab);
        toolbar.Controls.Add(exportTabOther);
        toolbar.Controls.Add(exportAll);
        toolbar.Controls.Add(exportAllOther);
        toolbar.Controls.Add(new Label
        {
            Text = "Exports are actual ZIP files. Desktop is the one-click default; Other Location opens a file-save dialog.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(12, 8, 0, 0)
        });
        root.Controls.Add(toolbar, 0, 0);

        AddTab(CreateHistoryTab(), DiagnosticExportSection.DeploymentHistory);
        AddTab(CreateLogTab(), DiagnosticExportSection.CurrentLog);
        AddTab(CreateFilesTab("Crash Reports", _appData.CrashReports, "*.txt"), DiagnosticExportSection.CrashReports);
        AddTab(CreateFilesTab("Error Reports", _appData.ErrorReports, "*.txt"), DiagnosticExportSection.ErrorReports);
        AddTab(CreatePerformanceTab(), DiagnosticExportSection.BuildPerformance);
        AddTab(CreateRuntimeResourcesTab(), DiagnosticExportSection.RuntimeResources);
        AddTab(CreateFilesTab("Internal Diagnostics", _appData.InternalDiagnosticsFolder, "*.log"), DiagnosticExportSection.InternalDiagnostics);

        root.Controls.Add(_tabs, 0, 1);
        Controls.Add(root);
        FormClosed += (_, _) => { _metricsTimer.Stop(); _metricsTimer.Dispose(); };
    }

    private void AddTab(TabPage tab, DiagnosticExportSection section)
    {
        _tabs.TabPages.Add(tab);
        _sectionByTab[tab] = section;
    }

    private TabPage CreateHistoryTab()
    {
        var tab = new TabPage("Deployment History & Timeline");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        var projects = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 520 };
        var header = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        header.Controls.Add(new Label { Text = "Project", AutoSize = true, Padding = new Padding(0, 7, 8, 0) });
        header.Controls.Add(projects);
        root.Controls.Add(header, 0, 0);

        var summary = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        grid.Columns.Add("Started", "Started");
        grid.Columns.Add("Result", "Result");
        grid.Columns.Add("Duration", "Duration");
        grid.Columns.Add("Mode", "Build mode");
        grid.Columns.Add("Version", "Version");
        grid.Columns.Add("Target", "Target");
        root.Controls.Add(summary, 0, 1);
        root.Controls.Add(grid, 0, 2);

        var summaries = _history.GetProjectSummaries();
        foreach (var item in summaries) projects.Items.Add(new ProjectChoice(item.ProjectId, DisplayProjectName(item)));
        if (projects.Items.Count > 0)
        {
            var preferred = _history.GetProjectId(_profile);
            var selected = Enumerable.Range(0, projects.Items.Count)
                .FirstOrDefault(i => (projects.Items[i] as ProjectChoice)?.ProjectId == preferred);
            projects.SelectedIndex = selected;
        }

        void LoadProject()
        {
            if (projects.SelectedItem is not ProjectChoice choice) { summary.Text = "No deployment history has been recorded yet."; return; }
            var s = _history.GetSummary(choice.ProjectId);
            if (s is null) return;
            summary.Text = BuildSummaryText(s);
            grid.Rows.Clear();
            foreach (var a in s.Attempts.OrderByDescending(x => x.StartedAtUtc))
            {
                var duration = a.EndedAtUtc.HasValue ? TimeSpan.FromSeconds(Math.Max(0, a.ElapsedSeconds)).ToString(@"hh\:mm\:ss") : "—";
                grid.Rows.Add(a.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), a.EndedAtUtc.HasValue ? a.Result : "Interrupted", duration, a.BuildMode,
                    string.IsNullOrWhiteSpace(a.ApplicationVersion) ? "—" : a.ApplicationVersion, a.DeploymentTargetPath);
            }
        }
        projects.SelectedIndexChanged += (_, _) => LoadProject();
        LoadProject();
        tab.Controls.Add(root);
        return tab;
    }

    private TabPage CreateLogTab()
    {
        var tab = new TabPage("Current Log");
        var box = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 9.5F) };
        try
        {
            if (File.Exists(_currentLog))
            {
                using var fs = new FileStream(_currentLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(Math.Max(0, fs.Length - 500_000), SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                box.Text = reader.ReadToEnd();
                box.SelectionStart = box.TextLength;
                box.ScrollToCaret();
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Diagnostics current log tab"); box.Text = GenericWeb.DeploymentManager.Services.SecretRedactor.Redact(ex.Message); }
        tab.Controls.Add(box);
        return tab;
    }

    private TabPage CreateFilesTab(string title, string folder, string pattern)
    {
        var tab = new TabPage(title);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 430 };
        var list = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
        var preview = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 9F) };
        try
        {
            if (Directory.Exists(folder))
                foreach (var file in Directory.EnumerateFiles(folder, pattern).OrderByDescending(File.GetLastWriteTime).Take(500)) list.Items.Add(file);
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Diagnostics file list: " + folder); }
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is not string file) return;
            try { preview.Text = File.ReadAllText(file); }
            catch (Exception ex) { InternalDiagnostics.Record(ex, "Diagnostics file preview: " + file); preview.Text = GenericWeb.DeploymentManager.Services.SecretRedactor.Redact(ex.Message); }
        };
        list.DoubleClick += (_, _) => { if (list.SelectedItem is string file) OpenTarget(file); };
        split.Panel1.Controls.Add(list);
        split.Panel2.Controls.Add(preview);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage CreatePerformanceTab()
    {
        var tab = new TabPage("Build Performance");
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9F) };
        box.Text = _deployment.LastPerformanceSnapshot?.ToPlainText() ?? "No build performance snapshot is available in the current session. Historical stage timings remain available in Deployment History.";
        tab.Controls.Add(box);
        return tab;
    }

    private TabPage CreateRuntimeResourcesTab()
    {
        var tab = new TabPage("CPU / Memory");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var current = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 11F), Text = "Sampling..." };
        var history = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9F) };
        var note = new Label { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(1000, 0), Text = "History is bounded to the most recent 720 samples. Deployment Manager does not run periodic forced GC; UI/log buffers are bounded and page controls are disposed normally so real leaks are not hidden." };
        root.Controls.Add(current, 0, 0); root.Controls.Add(history, 0, 1); root.Controls.Add(note, 0, 2);
        void Refresh()
        {
            var sample = _runtimeMetrics.Sample();
            current.Text = sample.ToDisplayText();
            var lines = _runtimeMetrics.GetHistory().TakeLast(240).Select(x => $"{x.At:HH:mm:ss} | {x.ToDisplayText()}");
            history.Text = string.Join(Environment.NewLine, lines);
            history.SelectionStart = history.TextLength;
            history.ScrollToCaret();
        }
        _metricsTimer.Tick += (_, _) => { if (!tab.IsDisposed) Refresh(); };
        _metricsTimer.Start();
        Refresh();
        tab.Controls.Add(root);
        return tab;
    }

    private void ExportCurrentTab(bool toDesktop)
    {
        if (_tabs.SelectedTab is null || !_sectionByTab.TryGetValue(_tabs.SelectedTab, out var section)) return;
        ExportSection(section, toDesktop);
    }

    private void ExportSection(DiagnosticExportSection section, bool toDesktop)
    {
        try
        {
            string destinationFile;
            if (toDesktop)
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
                    desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
                Directory.CreateDirectory(desktop);
                destinationFile = Path.Combine(desktop, DiagnosticBundleService.GetSuggestedFileName(section));
            }
            else
            {
                using var dialog = new SaveFileDialog
                {
                    Title = section == DiagnosticExportSection.All ? "Save complete diagnostic bundle" : $"Save {section} diagnostic export",
                    Filter = "ZIP diagnostic package (*.zip)|*.zip|All files (*.*)|*.*",
                    DefaultExt = "zip",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = DiagnosticBundleService.GetSuggestedFileName(section),
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    RestoreDirectory = true
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                destinationFile = dialog.FileName;
            }

            var path = _bundle.ExportSectionToFile(destinationFile, section, _profile, _history, _currentLog, _deployment.LastPerformanceSnapshot, _runtimeMetrics);
            ShowExportComplete(path);
        }
        catch (Exception ex)
        {
            InternalDiagnostics.Record(ex, "Diagnostic export: " + section);
            MessageBox.Show(this, GenericWeb.DeploymentManager.Services.SecretRedactor.Redact(ex.Message), "Diagnostic export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowExportComplete(string path)
    {
        using var dialog = new Form
        {
            Text = "Diagnostic export complete",
            Width = 720,
            Height = 210,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            KeyPreview = true
        };
        dialog.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) dialog.Close(); };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "Diagnostic file created successfully:", AutoSize = true, Font = new Font("Segoe UI Semibold", 10F) }, 0, 0);
        var pathBox = new TextBox { Text = path, ReadOnly = true, Dock = DockStyle.Fill };
        root.Controls.Add(pathBox, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", Width = 100 };
        var openFolder = new Button { Text = "Open Folder", Width = 120 };
        var openFile = new Button { Text = "Open File", Width = 110 };
        close.Click += (_, _) => dialog.Close();
        openFolder.Click += (_, _) => OpenTarget(Path.GetDirectoryName(path) ?? path);
        openFile.Click += (_, _) => OpenTarget(path);
        buttons.Controls.Add(close); buttons.Controls.Add(openFolder); buttons.Controls.Add(openFile);
        root.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(root);
        dialog.AcceptButton = openFile;
        dialog.CancelButton = close;
        dialog.ShowDialog(this);
    }

    private static string BuildSummaryText(DeploymentProjectSummary s)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"PROJECT LIFETIME — {s.ApplicationName}",
            new string('=', 80),
            $"IIS Site                  : {Dash(s.SiteName)}",
            $"Database                  : {Dash(s.DatabaseName)}",
            $"Project ID                : {s.ProjectId}",
            $"Total attempts            : {s.TotalAttempts}",
            $"Successful / Failed       : {s.SuccessfulAttempts} / {s.FailedAttempts}",
            $"Cancelled / Interrupted   : {s.CancelledAttempts} / {s.IncompleteAttempts}",
            $"Distinct released versions: {s.DistinctVersions}",
            $"First deployment          : {FormatDate(s.FirstDeploymentUtc)}",
            $"Latest deployment         : {FormatDate(s.LatestDeploymentUtc)}",
            $"Calendar lifetime         : {FormatDuration(s.CalendarLifetime)}",
            $"Cumulative deploy runtime : {FormatDuration(s.CumulativeRuntime)}",
            $"Average deployment        : {FormatDuration(s.AverageRuntime)}",
            $"Average successful deploy : {FormatDuration(s.AverageSuccessfulRuntime)}",
            $"Fastest / Slowest         : {FormatDuration(s.FastestRuntime)} / {FormatDuration(s.SlowestRuntime)}",
            $"Fast / Full / Prepared    : {s.SmartDeployCount} / {s.FullDeployCount} / {s.PreparedDeployCount}"
        });
    }

    private static string DisplayProjectName(DeploymentProjectSummary s) => string.IsNullOrWhiteSpace(s.SiteName) ? s.ApplicationName : $"{s.ApplicationName} — {s.SiteName}";
    private static string Dash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string FormatDate(DateTime? utc) => utc.HasValue ? utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—";
    private static string FormatDuration(TimeSpan duration) => duration.TotalDays >= 1 ? $"{duration.TotalDays:0.0} days" : duration.ToString(@"hh\:mm\:ss");

    private static void OpenTarget(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = target, UseShellExecute = true }); }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Open diagnostics target: " + target); }
    }

    private sealed record ProjectChoice(string ProjectId, string Display)
    {
        public override string ToString() => Display;
    }
}
