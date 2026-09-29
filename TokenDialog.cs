namespace VFL.GeradorWebMToken;

internal sealed class TokenDialog : Form
{
    private TokenDialog(string title, string message, bool confirm)
    {
        Text = title;
        var detailed = !confirm && (message.Length > 260 || message.Count(character => character == '\n') > 5);
        ClientSize = detailed ? new Size(700, 400) : new Size(520, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
        Font = new Font("Segoe UI", 10); BackColor = Theme.Background;
        Theme.ApplyIcon(this);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(24), Tag = "background" };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(new Label { Text = title.ToUpperInvariant(), Dock = DockStyle.Fill, ForeColor = Theme.Primary, Font = new Font("Segoe UI Semibold", 11) }, 0, 0);
        if (detailed)
        {
            root.Controls.Add(new TextBox
            {
                Text = message, Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BackColor = Theme.Field, ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle, WordWrap = true
            }, 0, 1);
        }
        else
        {
            root.Controls.Add(new Label { Text = message, Dock = DockStyle.Fill, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        }
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Tag = "background" };
        var ok = new Button { Text = confirm ? "SIM" : "OK", Width = 120, Height = 40, DialogResult = DialogResult.Yes, Tag = "primary" };
        actions.Controls.Add(ok);
        if (confirm) actions.Controls.Add(new Button { Text = "NÃO", Width = 110, Height = 40, DialogResult = DialogResult.No });
        root.Controls.Add(actions, 0, 2); Controls.Add(root); AcceptButton = ok;
        Theme.Apply(this);
    }

    public static void ShowMessage(IWin32Window owner, string title, string message)
    {
        using var dialog = new TokenDialog(title, message, false); dialog.ShowDialog(owner);
    }

    public static bool Confirm(IWin32Window owner, string title, string message)
    {
        using var dialog = new TokenDialog(title, message, true); return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}
