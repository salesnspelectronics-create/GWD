using System.Net;
using System.Net.Sockets;
using System.Diagnostics.CodeAnalysis;

namespace GenericWeb.DeploymentManager;

public sealed class Ipv4AddressControl : UserControl
{
    private readonly TextBox[] _parts = new TextBox[4];
    private bool _updating;

    public Ipv4AddressControl()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(205, 28);
        MaximumSize = new Size(205, 30);
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.White;
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Padding = new Padding(2), Margin = Padding.Empty };
        for (var i = 0; i < 4; i++)
        {
            var index = i;
            var box = new TextBox { Width = 43, MaxLength = 3, BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, Margin = new Padding(1, 4, 1, 2) };
            box.KeyPress += (_, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
                if (e.KeyChar == '.' && index < 3) { e.Handled = true; _parts[index + 1].Focus(); _parts[index + 1].SelectAll(); }
            };
            box.TextChanged += (_, _) => NormalizePart(index);
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Back && box.SelectionStart == 0 && index > 0) { _parts[index - 1].Focus(); _parts[index - 1].SelectionStart = _parts[index - 1].TextLength; }
                if (e.KeyCode == Keys.Right && box.SelectionStart == box.TextLength && index < 3) _parts[index + 1].Focus();
                if (e.KeyCode == Keys.Left && box.SelectionStart == 0 && index > 0) _parts[index - 1].Focus();
            };
            _parts[i] = box;
            row.Controls.Add(box);
            if (i < 3) row.Controls.Add(new Label { Text = ".", AutoSize = true, Font = new Font("Segoe UI Semibold", 12F), Margin = new Padding(0, 2, 0, 0) });
        }
        Controls.Add(row);
    }

    [AllowNull]
    public override string Text
    {
        get => string.Join('.', _parts.Select(x => x.Text));
        set
        {
            _updating = true;
            var values = (value ?? string.Empty).Split('.');
            for (var i = 0; i < 4; i++) _parts[i].Text = i < values.Length && int.TryParse(values[i], out var part) ? Math.Clamp(part, 0, 255).ToString() : string.Empty;
            _updating = false;
            OnTextChanged(EventArgs.Empty);
        }
    }

    public bool IsComplete => IPAddress.TryParse(Text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && Text.Count(c => c == '.') == 3;

    private void NormalizePart(int index)
    {
        if (_updating) return;
        _updating = true;
        var digits = new string(_parts[index].Text.Where(char.IsDigit).Take(3).ToArray());
        if (int.TryParse(digits, out var number) && number > 255) digits = "255";
        _parts[index].Text = digits;
        _parts[index].SelectionStart = digits.Length;
        _updating = false;
        OnTextChanged(EventArgs.Empty);
        if (digits.Length == 3 && index < 3) { _parts[index + 1].Focus(); _parts[index + 1].SelectAll(); }
    }
}
