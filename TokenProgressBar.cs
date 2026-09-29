namespace VFL.GeradorWebMToken;

internal sealed class TokenProgressBar : Control
{
    private int _value;
    public int Value { get => _value; set { _value = Math.Clamp(value, 0, 100); Invalidate(); } }
    public TokenProgressBar() { DoubleBuffered = true; Height = 8; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Field);
        using var brush = new SolidBrush(Theme.Primary);
        e.Graphics.FillRectangle(brush, 0, 0, (int)(Width * _value / 100d), Height);
    }
}
