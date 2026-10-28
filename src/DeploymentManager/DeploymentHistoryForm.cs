using GenericWeb.DeploymentManager.Models;
using GenericWeb.DeploymentManager.Services;

namespace GenericWeb.DeploymentManager;

public sealed class DeploymentHistoryForm : Form
{
    private readonly DeploymentHistoryService _history;
    private readonly string? _preferredProjectId;
    private readonly DeploymentProfile? _profile;
    private readonly string? _currentLog;
    private readonly ComboBox _projects = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    private readonly RichTextBox _summary = new() { ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F), BackColor = Color.White };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false
    };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 8, 8, 0) };
    private IReadOnlyList<DeploymentHistoryRecord> _currentAttempts = Array.Empty<DeploymentHistoryRecord>();
    private readonly HashSet<string> _expandedAttemptIds = new(StringComparer.OrdinalIgnoreCase);

    public DeploymentHistoryForm(DeploymentHistoryService history, string? preferredProjectId = null, DeploymentProfile? profile = null, string? currentLog = null)
    {
        _history = history;
        _preferredProjectId = preferredProjectId;
        _profile = profile;
        _currentLog = currentLog;

        Text = $"Deployment History / Stage Timing — {ProductInfo.DisplayVersion}";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        Font = new Font("Segoe UI", 9.5F);

        BuildUi();
        LoadProjects();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        header.Controls.Add(new Label { Text = "Project:", AutoSize = true, Padding = new Padding(0, 8, 6, 0), Font = new Font("Segoe UI Semibold", 9.5F) });
        header.Controls.Add(_projects);
        var refresh = new Button { Text = "Refresh", Width = 95, Height = 32, Margin = new Padding(10, 2, 0, 2) };
        refresh.Click += (_, _) => LoadProjects();
        header.Controls.Add(refresh);
        var openFolder = new Button { Text = "Open History Folder", Width = 150, Height = 32, Margin = new Padding(10, 2, 0, 2) };
        openFolder.Click += (_, _) => OpenTarget(_history.Root);
        header.Controls.Add(openFolder);
        var export = new Button { Text = "Export Diagnostic Bundle", Width = 190, Height = 32, Margin = new Padding(10, 2, 0, 2), Enabled = _profile is not null };
        export.Click += (_, _) => ExportDiagnosticBundle();
        header.Controls.Add(export);
        root.Controls.Add(header, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var summaryTab = new TabPage("Lifetime Summary & Timeline");
        summaryTab.Controls.Add(_summary);
        tabs.TabPages.Add(summaryTab);

        var historyTab = new TabPage("Deployment History");
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.Columns.Add("Expand", "");
        _grid.Columns.Add("Started", "Started");
        _grid.Columns.Add("Result", "Result / Stage status");
        _grid.Columns.Add("Duration", "Duration");
        _grid.Columns.Add("BuildMode", "Build mode / Stage");
        _grid.Columns.Add("DependencyMode", "Dependency mode");
        _grid.Columns.Add("Detail", "Detail / Target");
        _grid.Columns[0].Width = 42;
        _grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns[6].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        var historyRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        historyRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        historyRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        historyRoot.Controls.Add(_grid, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(6) };
        var openLog = new Button { Text = "Open Session Log", Width = 145, Height = 32 };
        openLog.Click += (_, _) => OpenSelectedLog();
        actions.Controls.Add(openLog);
        actions.Controls.Add(new Label { Text = "Click a deployment row to expand/collapse its recorded Build Performance stages. Double-click opens its log.", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(10, 8, 0, 0) });
        historyRoot.Controls.Add(actions, 0, 1);
        historyTab.Controls.Add(historyRoot);
        tabs.TabPages.Add(historyTab);
        root.Controls.Add(tabs, 0, 1);

        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);

        _projects.SelectedIndexChanged += (_, _) => LoadSelectedProject();
        _grid.CellClick += (_, e) => ToggleSelectedPerformance(e.RowIndex);
        _grid.CellDoubleClick += (_, _) => OpenSelectedLog();
    }

    private void ExportDiagnosticBundle()
    {
        if (_profile is null) return;
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Directory.CreateDirectory(desktop);
            var path = new DiagnosticBundleService().Export(desktop, _profile, _history, _currentLog ?? string.Empty);
            MessageBox.Show(this, "Diagnostic bundle created with sensitive values redacted:\r\n" + path, "Diagnostic bundle", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Diagnostic export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadProjects()
    {
        var summaries = _history.GetProjectSummaries();
        var selectedId = (_projects.SelectedItem as ProjectChoice)?.ProjectId ?? _preferredProjectId;
        _projects.BeginUpdate();
        try
        {
            _projects.Items.Clear();
            foreach (var item in summaries)
                _projects.Items.Add(new ProjectChoice(item.ProjectId, DisplayProjectName(item)));
        }
        finally { _projects.EndUpdate(); }

        if (_projects.Items.Count == 0)
        {
            _summary.Text = "No deployment history has been recorded yet.\r\n\r\nHistory is written when a deployment starts and completed with Successful / Failed / Cancelled outcome.";
            _grid.DataSource = null;
            _status.Text = "History folder: " + _history.Root;
            return;
        }

        var index = 0;
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            for (var i = 0; i < _projects.Items.Count; i++)
            {
                if ((_projects.Items[i] as ProjectChoice)?.ProjectId == selectedId) { index = i; break; }
            }
        }
        _projects.SelectedIndex = index;
        LoadSelectedProject();
    }

    private void LoadSelectedProject()
    {
        if (_projects.SelectedItem is not ProjectChoice choice) return;
        var summary = _history.GetSummary(choice.ProjectId);
        if (summary is null) return;

        _currentAttempts = summary.Attempts.OrderByDescending(x => x.StartedAtUtc).ToArray();
        _summary.Text = BuildSummaryText(summary);
        _expandedAttemptIds.Clear();
        PopulateHistoryGrid();
        _status.Text = $"{summary.TotalAttempts} attempt(s) recorded. Persistent history: {_history.Root}";
    }

    private void PopulateHistoryGrid()
    {
        _grid.Rows.Clear();
        foreach (var attempt in _currentAttempts)
        {
            var performance = attempt.BuildPerformance;
            var canExpand = performance?.Stages.Count > 0;
            var expanded = canExpand && _expandedAttemptIds.Contains(attempt.AttemptId);
            var total = performance is null ? (attempt.EndedAtUtc.HasValue ? TimeSpan.FromSeconds(attempt.ElapsedSeconds) : TimeSpan.Zero) : TimeSpan.FromMilliseconds(performance.TotalMilliseconds);
            var index = _grid.Rows.Add(canExpand ? (expanded ? "▼" : "▶") : "", ToLocal(attempt.StartedAtUtc),
                attempt.EndedAtUtc.HasValue ? attempt.Result : "In progress / interrupted", FormatDuration(total), attempt.BuildMode,
                performance?.DependencyMode ?? (attempt.OfflineBuild ? "Offline Only" : "—"), attempt.DeploymentTargetPath);
            var parent = _grid.Rows[index];
            parent.Tag = attempt;
            parent.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            if (!expanded || performance is null) continue;

            foreach (var stage in performance.Stages)
            {
                var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, stage.ElapsedMilliseconds));
                var childIndex = _grid.Rows.Add("", "", stage.Status, elapsed.ToString(@"hh\:mm\:ss\.fff"), "    " + stage.Name, performance.DependencyMode, stage.Detail);
                var child = _grid.Rows[childIndex];
                child.Tag = attempt;
                child.DefaultCellStyle.BackColor = Color.FromArgb(245, 248, 252);
                child.DefaultCellStyle.ForeColor = stage.Status.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ? Color.Firebrick : Color.DimGray;
            }
        }
    }

    private void ToggleSelectedPerformance(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
        if (_grid.Rows[rowIndex].Tag is not DeploymentHistoryRecord attempt || attempt.BuildPerformance?.Stages.Count is not > 0) return;
        var isParent = string.Equals(_grid.Rows[rowIndex].Cells["Started"].Value?.ToString(), ToLocal(attempt.StartedAtUtc), StringComparison.Ordinal);
        if (!isParent) return;
        if (!_expandedAttemptIds.Add(attempt.AttemptId)) _expandedAttemptIds.Remove(attempt.AttemptId);
        PopulateHistoryGrid();
    }

    private static string BuildSummaryText(DeploymentProjectSummary s)
    {
        var lines = new List<string>
        {
            $"PROJECT LIFETIME — {s.ApplicationName}",
            new string('=', 72),
            $"IIS Site                 : {BlankAsDash(s.SiteName)}",
            $"Database                 : {BlankAsDash(s.DatabaseName)}",
            $"Project ID               : {s.ProjectId}",
            "",
            $"Total deployment attempts: {s.TotalAttempts}",
            $"Successful               : {s.SuccessfulAttempts}",
            $"Failed                   : {s.FailedAttempts}",
            $"Cancelled                : {s.CancelledAttempts}",
            $"Incomplete/interrupted   : {s.IncompleteAttempts}",
            $"Distinct released versions: {(s.DistinctVersions > 0 ? s.DistinctVersions.ToString() : "Version metadata unavailable")}",
            "",
            $"First deployment         : {FormatDate(s.FirstDeploymentUtc)}",
            $"Latest deployment        : {FormatDate(s.LatestDeploymentUtc)}",
            $"Calendar lifetime        : {FormatLifetime(s.CalendarLifetime)}",
            $"Cumulative deploy runtime: {FormatDuration(s.CumulativeRuntime)}",
            $"Average deployment       : {FormatDuration(s.AverageRuntime)}",
            $"Average successful deploy: {FormatDuration(s.AverageSuccessfulRuntime)}",
            $"Fastest deployment       : {FormatDuration(s.FastestRuntime)}",
            $"Slowest deployment       : {FormatDuration(s.SlowestRuntime)}",
            $"Average deploy interval  : {FormatDuration(s.AverageInterval)}",
            "",
            $"Fast local/IIS count      : {s.SmartDeployCount}",
            $"Full IIS Deploy count     : {s.FullDeployCount}",
            $"Prepared deploy count    : {s.PreparedDeployCount}",
            "",
            "RELEASE / DEPLOYMENT TIMELINE",
            new string('-', 72)
        };

        foreach (var item in s.Attempts.OrderBy(x => x.StartedAtUtc))
        {
            var version = string.IsNullOrWhiteSpace(item.ApplicationVersion) ? "version ?" : "V" + item.ApplicationVersion.TrimStart('V', 'v');
            var result = item.EndedAtUtc.HasValue ? item.Result : "In progress / interrupted";
            var duration = item.EndedAtUtc.HasValue ? FormatDuration(TimeSpan.FromSeconds(item.ElapsedSeconds)) : "—";
            var interval = item.IntervalSincePreviousSeconds.HasValue ? $" | since previous: {FormatDuration(TimeSpan.FromSeconds(item.IntervalSincePreviousSeconds.Value))}" : string.Empty;
            lines.Add($"{ToLocal(item.StartedAtUtc)} | {version} | {result} | {item.BuildMode} | {duration}{interval}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void OpenSelectedLog()
    {
        if (_grid.CurrentRow?.Tag is not DeploymentHistoryRecord row || string.IsNullOrWhiteSpace(row.SessionLog)) return;
        if (!File.Exists(row.SessionLog))
        {
            MessageBox.Show(this, "The session log no longer exists at:\r\n" + row.SessionLog, "Session log not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        new LogForm(row.SessionLog).ShowDialog(this);
    }

    private static void OpenTarget(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open:\r\n" + target + "\r\n\r\n" + ex.Message, "Open failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string DisplayProjectName(DeploymentProjectSummary s)
    {
        var name = string.IsNullOrWhiteSpace(s.ApplicationName) ? "Unknown application" : s.ApplicationName;
        return string.IsNullOrWhiteSpace(s.SiteName) ? name : $"{name} — {s.SiteName}";
    }

    private static string ToLocal(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    private static string FormatDate(DateTime? utc) => utc.HasValue ? ToLocal(utc.Value) : "—";
    private static string BlankAsDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays}d {duration:hh\\:mm\\:ss}";
        return duration.ToString(@"hh\:mm\:ss");
    }

    private static string FormatLifetime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (duration.TotalDays >= 1) return $"{duration.TotalDays:0.0} days";
        return FormatDuration(duration);
    }

    private sealed record ProjectChoice(string ProjectId, string Display)
    {
        public override string ToString() => Display;
    }

}
