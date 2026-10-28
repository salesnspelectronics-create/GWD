using GenericWeb.DeploymentManager.Services;

namespace GenericWeb.DeploymentManager;

public sealed class BuildPerformanceForm : Form
{
    public BuildPerformanceForm(BuildPerformanceTrace trace)
    {
        Text = $"Build Performance — {ProductInfo.DisplayVersion}";
        Width = 1050;
        Height = 720;
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        Font = new Font("Segoe UI", 9.5F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F),
            Text = $"Build Performance  |  {trace.BuildMode}  |  Total {trace.TotalElapsed:hh\\:mm\\:ss\\.fff}\r\n" +
                   $"Dependency Mode: {trace.DependencyMode}   Started: {trace.StartedAt:yyyy-MM-dd HH:mm:ss}"
        };
        root.Controls.Add(header, 0, 0);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false
        };
        grid.Columns.Add("Stage", "Stage");
        grid.Columns.Add("Time", "Time");
        grid.Columns.Add("Percent", "% of Build");
        grid.Columns.Add("Status", "Status");
        grid.Columns.Add("Detail", "Detail");
        grid.Columns[0].FillWeight = 170;
        grid.Columns[1].FillWeight = 75;
        grid.Columns[2].FillWeight = 60;
        grid.Columns[3].FillWeight = 60;
        grid.Columns[4].FillWeight = 260;

        var totalMs = Math.Max(1, trace.TotalElapsed.TotalMilliseconds);
        foreach (var stage in trace.Stages.OrderByDescending(x => x.Elapsed))
        {
            var pct = stage.Skipped ? 0 : stage.Elapsed.TotalMilliseconds / totalMs * 100.0;
            grid.Rows.Add(stage.Name, stage.Elapsed.ToString(@"hh\:mm\:ss\.fff"), stage.Skipped ? "-" : $"{pct:0.0}%", stage.Status, stage.Detail);
        }
        root.Controls.Add(grid, 0, 1);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var close = new Button { Text = "Close", Width = 110, Height = 36 };
        close.Click += (_, _) => Close();
        var copy = new Button { Text = "Copy Report", Width = 125, Height = 36 };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(trace.ToPlainText());
                MessageBox.Show(this, "Performance report copied. You can paste it directly into ChatGPT.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, GenericWeb.DeploymentManager.Services.SecretRedactor.Redact(ex.Message), "Copy failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
    }
}
