namespace GenericWeb.DeploymentManager;

public sealed class AutoYesConfirmationForm : Form
{
    private readonly Button _yes = new() { Width = 120, Height = 34 };
    private readonly Button _no = new() { Text = "No", Width = 120, Height = 34 };
    private readonly Label _countdown = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private int _remainingSeconds;

    public AutoYesConfirmationForm(string title, string message, int seconds = 5)
    {
        _remainingSeconds = Math.Max(1, seconds);
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill };
        var body = new Label { Text = message, AutoSize = true, MaximumSize = new Size(620, 0), Padding = new Padding(0, 0, 0, 8) };
        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Dock = DockStyle.Fill };
        actions.Controls.Add(_no);
        actions.Controls.Add(_yes);
        root.Controls.Add(body, 0, 0);
        root.Controls.Add(_countdown, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);

        AcceptButton = _yes;
        CancelButton = _no;
        _yes.Cursor = Cursors.Hand;
        _no.Cursor = Cursors.Hand;
        _yes.Click += (_, _) => Finish(DialogResult.Yes);
        _no.Click += (_, _) => Finish(DialogResult.No);
        _timer.Tick += (_, _) =>
        {
            _remainingSeconds--;
            if (_remainingSeconds <= 0) Finish(DialogResult.Yes);
            else RefreshCountdown();
        };
        Shown += (_, _) => { RefreshCountdown(); _yes.Focus(); _timer.Start(); };
        FormClosed += (_, _) => _timer.Stop();
    }

    private void RefreshCountdown()
    {
        _yes.Text = $"Yes ({_remainingSeconds})";
        _countdown.Text = $"Continuing automatically in {_remainingSeconds} second{(_remainingSeconds == 1 ? "" : "s")}. Click No to stop.";
    }

    private void Finish(DialogResult result)
    {
        _timer.Stop();
        DialogResult = result;
        Close();
    }
}
