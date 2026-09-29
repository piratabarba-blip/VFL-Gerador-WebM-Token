using System.Drawing.Drawing2D;

namespace VFL.GeradorWebMToken;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(10, 13, 18);
    public static readonly Color Header = Color.FromArgb(14, 18, 24);
    public static readonly Color Surface = Color.FromArgb(25, 29, 37);
    public static readonly Color Field = Color.FromArgb(34, 39, 49);
    public static readonly Color Border = Color.FromArgb(61, 69, 84);
    public static readonly Color Text = Color.FromArgb(218, 224, 234);
    public static readonly Color Muted = Color.FromArgb(166, 177, 197);
    public static readonly Color Primary = Color.FromArgb(237, 46, 71);
    public static readonly Color Button = Color.FromArgb(43, 49, 62);

    public static void Apply(Control root)
    {
        root.BackColor = (root.Tag as string) switch
        {
            "background" => Background,
            "header" => Header,
            "surface" or "rounded-surface" => Surface,
            "accent" => Primary,
            _ => root is Form ? Background : root is Panel ? Surface : Background
        };
        root.ForeColor = Text;
        switch (root)
        {
            case TextBox box:
                box.BackColor = Field; box.ForeColor = Text; box.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox combo:
                combo.BackColor = Field; combo.ForeColor = Text; combo.FlatStyle = FlatStyle.Flat;
                break;
            case NumericUpDown number:
                number.BackColor = Field; number.ForeColor = Text; number.BorderStyle = BorderStyle.FixedSingle;
                break;
            case CheckBox check:
                check.BackColor = Color.Transparent; check.ForeColor = Text;
                break;
            case Button button when button.Tag as string == "primary":
                button.BackColor = Primary; button.ForeColor = Text; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; Round(button, 6);
                break;
            case Button button:
                button.BackColor = Button; button.ForeColor = Text; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Border; Round(button, 6);
                break;
            case Label label:
                if (label.Tag as string != "accent") label.BackColor = Color.Transparent;
                break;
        }
        if (root.Tag as string == "rounded-surface") Round(root, 10);
        foreach (Control child in root.Controls) Apply(child);
    }

    public static void ApplyIcon(Form form)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null) form.Icon = (Icon)icon.Clone();
        }
        catch { }
    }

    private static void Round(Control control, int radius)
    {
        void Update()
        {
            if (control.Width < 2 || control.Height < 2) return;
            var d = radius * 2;
            using var path = new GraphicsPath();
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(control.Width - d - 1, 0, d, d, 270, 90);
            path.AddArc(control.Width - d - 1, control.Height - d - 1, d, d, 0, 90);
            path.AddArc(0, control.Height - d - 1, d, d, 90, 90);
            path.CloseFigure();
            var old = control.Region;
            control.Region = new Region(path);
            old?.Dispose();
        }
        Update();
        control.SizeChanged += (_, _) => Update();
    }
}
