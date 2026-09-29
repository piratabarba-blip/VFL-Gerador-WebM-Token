namespace VFL.GeradorWebMToken;

internal sealed class CheckerPreview : Control
{
    private const int ReferenceSize = 500;
    private Image? _image;

    public Image? PreviewImage
    {
        get => _image;
        set { _image?.Dispose(); _image = value; Invalidate(); }
    }

    public CheckerPreview()
    {
        DoubleBuffered = true;
        BackColor = Theme.Field;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Surface);
        var side = Math.Min(ReferenceSize, Math.Min(ClientSize.Width, ClientSize.Height));
        var viewport = new Rectangle((ClientSize.Width - side) / 2, (ClientSize.Height - side) / 2, side, side);
        const int size = 18;
        using var light = new SolidBrush(Color.FromArgb(68, 73, 82));
        using var dark = new SolidBrush(Color.FromArgb(42, 47, 56));
        for (var y = viewport.Top; y < viewport.Bottom; y += size)
            for (var x = viewport.Left; x < viewport.Right; x += size)
            {
                var cell = new Rectangle(x, y, Math.Min(size, viewport.Right - x), Math.Min(size, viewport.Bottom - y));
                e.Graphics.FillRectangle((((x - viewport.Left) / size + (y - viewport.Top) / size) & 1) == 0 ? light : dark, cell);
            }
        if (_image is null) return;
        e.Graphics.DrawImage(_image, viewport);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
