using System.Drawing.Drawing2D;

namespace VFL.GeradorWebMToken;

internal sealed class ValueSlider : Control
{
    private decimal _minimum;
    private decimal _maximum = 100;
    private decimal _value;
    private decimal _increment = 1;
    private bool _dragging;

    public event EventHandler? ValueChanged;

    public decimal Minimum
    {
        get => _minimum;
        set { _minimum = value; Value = _value; Invalidate(); }
    }

    public decimal Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(value, _minimum); Value = _value; Invalidate(); }
    }

    public decimal Increment
    {
        get => _increment;
        set => _increment = value <= 0 ? 1 : value;
    }

    public int DecimalPlaces { get; set; }

    public decimal Value
    {
        get => _value;
        set
        {
            var next = Math.Min(_maximum, Math.Max(_minimum, value));
            if (_increment > 0)
                next = _minimum + Math.Round((next - _minimum) / _increment, MidpointRounding.AwayFromZero) * _increment;
            next = Math.Min(_maximum, Math.Max(_minimum, next));
            if (_value == next) return;
            _value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ValueSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable |
                 ControlStyles.SupportsTransparentBackColor, true);
        Height = 30;
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        const int valueWidth = 58;
        const int knobSize = 14;
        var trackLeft = 7;
        var trackRight = Math.Max(trackLeft + 1, Width - valueWidth - 13);
        var centerY = Height / 2;
        var track = new Rectangle(trackLeft, centerY - 3, trackRight - trackLeft, 6);
        var ratio = _maximum == _minimum ? 0d : (double)((_value - _minimum) / (_maximum - _minimum));
        var knobX = track.Left + (int)Math.Round(track.Width * ratio);

        using var trackPath = RoundedRectangle(track, 3);
        using var trackBrush = new SolidBrush(Theme.Border);
        e.Graphics.FillPath(trackBrush, trackPath);

        if (knobX > track.Left)
        {
            var fill = new Rectangle(track.Left, track.Top, knobX - track.Left, track.Height);
            using var fillPath = RoundedRectangle(fill, 3);
            using var fillBrush = new SolidBrush(Enabled ? Theme.Primary : Theme.Dim);
            e.Graphics.FillPath(fillBrush, fillPath);
        }

        using var knobBrush = new SolidBrush(Enabled ? Color.White : Theme.Muted);
        using var knobBorder = new Pen(Enabled ? Theme.Primary : Theme.Dim, 2);
        var knob = new Rectangle(knobX - knobSize / 2, centerY - knobSize / 2, knobSize, knobSize);
        e.Graphics.FillEllipse(knobBrush, knob);
        e.Graphics.DrawEllipse(knobBorder, knob);

        var valueBox = new Rectangle(Width - valueWidth, 2, valueWidth - 1, Height - 4);
        using var valuePath = RoundedRectangle(valueBox, 6);
        using var valueBrush = new SolidBrush(Theme.Field);
        using var valueBorder = new Pen(ContainsFocus ? Theme.Primary : Theme.Border);
        e.Graphics.FillPath(valueBrush, valuePath);
        e.Graphics.DrawPath(valueBorder, valuePath);

        var format = DecimalPlaces <= 0 ? "0" : "0." + new string('0', DecimalPlaces);
        TextRenderer.DrawText(e.Graphics, _value.ToString(format), Font, valueBox, Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        _dragging = true;
        SetFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Enabled) Value += e.Delta > 0 ? Increment : -Increment;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Right or Keys.Up) { Value += Increment; e.Handled = true; }
        else if (e.KeyCode is Keys.Left or Keys.Down) { Value -= Increment; e.Handled = true; }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    private void SetFromMouse(int mouseX)
    {
        const int valueWidth = 58;
        var left = 7;
        var right = Math.Max(left + 1, Width - valueWidth - 13);
        var ratio = Math.Clamp((mouseX - left) / (double)(right - left), 0, 1);
        Value = _minimum + (decimal)ratio * (_maximum - _minimum);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 1) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
