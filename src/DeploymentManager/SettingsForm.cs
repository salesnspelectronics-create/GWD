using GenericWeb.DeploymentManager.Services;

namespace GenericWeb.DeploymentManager;

public sealed class SettingsForm : Form
{
    private readonly AppDataService _appData;
    private readonly TextBox _releaseRoot = new() { Dock = DockStyle.Fill };
    private readonly TextBox _repository = new() { Dock = DockStyle.Fill };
    private readonly TextBox _workspace = new() { Dock = DockStyle.Fill };

    public SettingsForm(AppDataService appData)
    {
        _appData = appData;
        Text = $"Deployment Manager Settings — {ProductInfo.DisplayVersion}";
        Width = 900;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        var settings = appData.GetSettings();
        _releaseRoot.Text = string.IsNullOrWhiteSpace(settings.ManagerReleaseRoot) ? appData.GetManagerReleaseRoot() : settings.ManagerReleaseRoot;
        _repository.Text = appData.GetDeploymentRepositoryFolder();
        _workspace.Text = appData.GetWorkspaceRoot();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "Paths below are persisted in settings.json. Build scripts read the Manager release/output folder, so no G:\\... path is hard-coded.",
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            ForeColor = Color.DimGray
        }, 0, 0);

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(0, 14, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddPathRow(grid, "Manager release/output folder", _releaseRoot, BrowseContext.ReleaseOutput);
        AddPathRow(grid, "Shared dependency / deployment repository", _repository, BrowseContext.Repository);
        AddPathRow(grid, "Stable build workspace", _workspace, BrowseContext.Workspace);
        root.Controls.Add(grid, 0, 1);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Save", Width = 110, Height = 36 };
        var cancel = new Button { Text = "Cancel", Width = 110, Height = 36 };
        save.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = cancel; // Esc closes this modal.
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
    }

    private void AddPathRow(TableLayoutPanel grid, string label, TextBox text, BrowseContext context)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 8, 0) }, 0, row);
        text.Margin = new Padding(3, 4, 3, 8);
        grid.Controls.Add(text, 1, row);
        var browse = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(6, 3, 3, 8) };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = label,
                SelectedPath = AppDataService.ResolveExistingFolder(text.Text) is { Length: > 0 } p ? p : _appData.GetLastBrowseFolder(context)
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            text.Text = dialog.SelectedPath;
            _appData.SaveLastBrowseFolder(dialog.SelectedPath, context);
        };
        grid.Controls.Add(browse, 2, row);
    }

    private void SaveAndClose()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_releaseRoot.Text)) _appData.SaveManagerReleaseRoot(_releaseRoot.Text.Trim());
            if (!string.IsNullOrWhiteSpace(_repository.Text)) _appData.SaveDeploymentRepositoryFolder(_repository.Text.Trim());
            if (!string.IsNullOrWhiteSpace(_workspace.Text)) _appData.SaveWorkspaceRoot(_workspace.Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            InternalDiagnostics.Record(ex, "Settings form save");
            MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
