using System.Drawing.Drawing2D;

namespace VFL.GeradorWebMToken;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(14, 17, 23);
    public static readonly Color Header = Color.FromArgb(21, 25, 34);
    public static readonly Color Surface = Color.FromArgb(26, 32, 44);
    public static readonly Color Card = Color.FromArgb(34, 41, 56);
    public static readonly Color Field = Color.FromArgb(34, 41, 56);
    public static readonly Color Border = Color.FromArgb(45, 55, 72);
    public static readonly Color Text = Color.FromArgb(248, 250, 252);
    public static readonly Color Muted = Color.FromArgb(148, 163, 184);
    public static readonly Color Dim = Color.FromArgb(100, 116, 139);
    public static readonly Color Primary = Color.FromArgb(6, 182, 212);
    public static readonly Color Success = Color.FromArgb(16, 185, 129);
    public static readonly Color AccentCyan = Color.FromArgb(34, 211, 238);
    public static readonly Color Button = Color.FromArgb(34, 41, 56);

    public static void Apply(Control root)
    {
        root.BackColor = (root.Tag as string) switch
        {
            "background" => Background,
            "header" => Header,
            "surface" or "rounded-surface" or "sidebar" => Surface,
            "card" => Card,
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
                button.BackColor = Primary; button.ForeColor = Color.White; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; Round(button, 10);
                break;
            case Button button when button.Tag as string == "success":
                button.BackColor = Success; button.ForeColor = Color.White; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; Round(button, 10);
                break;
            case Button button:
                button.BackColor = Button; button.ForeColor = Text; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Border; Round(button, 10);
                break;
            case Label label:
                if (label.Tag as string != "accent") label.BackColor = Color.Transparent;
                if (label.Tag as string == "brand-badge") label.ForeColor = Primary;
                break;
        }
        if (root is TextBox or ComboBox or NumericUpDown) Round(root, 6);
        else if (root.Tag as string == "rounded-surface") Round(root, 14);
        else if (root.Tag as string == "card") Round(root, 12);
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
