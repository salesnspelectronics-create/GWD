using GenericWeb.DeploymentManager.CodeQuality;
using GenericWeb.DeploymentManager.Services;

namespace GenericWeb.DeploymentManager;

public sealed partial class DiagnosticsCenterForm
{
    private TabPage CreateCodeQualityTab()
    {
        var tab = new TabPage("Code Quality");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var builds = new ComboBox { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
        var counters = new Label { AutoSize = true, Padding = new Padding(4, 6, 4, 0) };
        bar.Controls.Add(counters);
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        bar.Controls.Add(builds); bar.Controls.Add(refresh);
        var filters = new[] { "Project", "Tool", "Severity", "Code" }.Select(name => new TextBox { Width = 120, PlaceholderText = name + " filter" }).ToArray();
        foreach (var filter in filters) bar.Controls.Add(filter);
        var summary = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 5) };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
        foreach (var column in new[] { "Code", "Severity", "Category", "File", "Line", "Occurrences" }) grid.Columns.Add(column, column);
        var detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        BuildDiagnostics? Selected() => (builds.SelectedItem as QualityBuildChoice)?.Build;
        void Filter()
        {
            grid.Rows.Clear(); detail.Clear();
            var build = Selected(); if (build is null) return;
            bool Contains(string? value, int index) => (value ?? "").Contains(filters[index].Text.Trim(), StringComparison.OrdinalIgnoreCase);
            foreach (var issue in build.Items.Where(i => Contains(build.ProjectId, 0) && i.Occurrences.Any(o => Contains(o.Tool, 1)) && Contains(i.Diagnostic.Severity, 2) && Contains(i.Diagnostic.Code, 3)))
            {
                var d = issue.Diagnostic;
                var row = grid.Rows[grid.Rows.Add(d.Code, d.Severity, d.Category, d.File ?? "", d.Line?.ToString() ?? "", issue.Occurrences.Count)];
                row.Tag = issue;
            }
        }
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.CurrentRow?.Tag is not Issue issue) return;
            var d = issue.Diagnostic;
            detail.Text = $"{d.Code} | Risk: {d.Risk} | {d.File}:{d.Line}\r\n{d.Message}\r\n\r\nSuggested fix: {d.Remedy}\r\n\r\n" +
                string.Join("\r\n", issue.Occurrences.Select(o => $"{o.At:u} | stage {o.StageId} | {o.Tool} | {o.Stream}\r\n" + string.Join("\r\n", o.Context)));
        };
        void LoadBuilds()
        {
            try
            {
                builds.Items.Clear();
                var all = CodeQualitySession.Store(_appData).Read().ToArray();
                foreach (var build in all) builds.Items.Add(new QualityBuildChoice(build));
                if (builds.Items.Count > 0) builds.SelectedIndex = 0;
                var project = _history.GetProjectId(_profile);
                var projectBuilds = all.Where(b => b.ProjectId == project).ToArray();
                var success = projectBuilds.FirstOrDefault(b => b.Result == "Successful");
                var failed = projectBuilds.FirstOrDefault(b => b.Result == "Failed");
                summary.Text = $"Current project: {project} | Last successful: {success?.Started.ToLocalTime().ToString("g") ?? "—"} | Last failed: {failed?.Started.ToLocalTime().ToString("g") ?? "—"}";
                if (all.Length == 0) summary.Text += " | No collected builds yet.";
            }
            catch (Exception ex) { InternalDiagnostics.Record(ex, "Code Quality UI"); summary.Text = "Code Quality records unavailable: " + SecretRedactor.Redact(ex.Message); }
        }
        builds.SelectedIndexChanged += (_, _) =>
        {
            Filter();
            if (Selected() is not { } build) return;
            counters.Text = $"Error: {build.Items.Count(i => i.Diagnostic.Severity == "Error")} | Warning: {build.Items.Count(i => i.Diagnostic.Severity == "Warning")} | Security: {build.Items.Count(i => i.Diagnostic.Category == "Security")} | Performance: {build.Items.Count(i => i.Diagnostic.Category == "Performance")}";
            detail.Text = $"Build {build.BuildId}\r\nAttempt {build.AttemptId}\r\nProject {build.ProjectId}\r\nCollection: {build.CollectionStatus}\r\n" +
                $"Error: {build.Items.Count(i => i.Diagnostic.Severity == "Error")} | Warning: {build.Items.Count(i => i.Diagnostic.Severity == "Warning")} | Security: {build.Items.Count(i => i.Diagnostic.Category == "Security")} | Performance: {build.Items.Count(i => i.Diagnostic.Category == "Performance")}\r\n\r\n" +
                string.Join("\r\n", build.StageCoverage.Select(s => $"{s.Status} | {s.Name} | exit {s.ExitCode} | {s.Started:u} → {s.Ended:u}"));
        };
        foreach (var filter in filters) filter.TextChanged += (_, _) => Filter();
        refresh.Click += (_, _) => LoadBuilds();
        foreach (var format in new[] { "JSON", "Markdown", "Copy Development Handoff" })
        {
            var button = new Button { Text = format, AutoSize = true };
            button.Click += (_, _) =>
            {
                if (Selected() is not { } build) return;
                try
                {
                    if (format.StartsWith("Copy")) { Clipboard.SetText(SecretRedactor.Redact(DiagnosticExport.Handoff(build))); return; }
                    using var dialog = new SaveFileDialog { Filter = format == "JSON" ? "JSON|*.json" : "Markdown|*.md", FileName = "code-quality-" + build.BuildId, AddExtension = true };
                    if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, SecretRedactor.Redact(format == "JSON" ? DiagnosticExport.Json(build) : DiagnosticExport.Markdown(build)));
                }
                catch (Exception ex) { InternalDiagnostics.Record(ex, "Code Quality export"); MessageBox.Show(this, SecretRedactor.Redact(ex.Message), "Export failed"); }
            };
            bar.Controls.Add(button);
        }
        root.Controls.Add(bar, 0, 0); root.Controls.Add(summary, 0, 1); root.Controls.Add(grid, 0, 2); root.Controls.Add(detail, 0, 3);
        tab.Controls.Add(root); LoadBuilds(); return tab;
    }
    private sealed record QualityBuildChoice(BuildDiagnostics Build)
    {
        public override string ToString() => $"{Build.Started.ToLocalTime():g} | {Build.ProjectId} | {Build.Result} | {Build.BuildId[..8]}";
    }
}
