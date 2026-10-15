using System.Drawing.Drawing2D;

namespace VFL.GeradorWebMToken;

internal sealed class AspectPreview : Control
{
    private static readonly Color CheckerLight = Color.FromArgb(68, 73, 82);
    private static readonly Color CheckerDark = Color.FromArgb(42, 47, 56);
    private Image? _image;

    public Image? PreviewImage
    {
        get => _image;
        set
        {
            _image?.Dispose();
            _image = value;
            Invalidate();
        }
    }

    public AspectPreview()
    {
        DoubleBuffered = true;
        BackColor = Theme.Background;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Background);
        var viewport = GetViewport();
        if (viewport.Width <= 0 || viewport.Height <= 0) return;

        const int checkerSize = 18;
        using var light = new SolidBrush(CheckerLight);
        using var dark = new SolidBrush(CheckerDark);
        for (var y = viewport.Top; y < viewport.Bottom; y += checkerSize)
        for (var x = viewport.Left; x < viewport.Right; x += checkerSize)
        {
            var cell = new Rectangle(x, y, Math.Min(checkerSize, viewport.Right - x), Math.Min(checkerSize, viewport.Bottom - y));
            e.Graphics.FillRectangle((((x - viewport.Left) / checkerSize + (y - viewport.Top) / checkerSize) & 1) == 0 ? light : dark, cell);
        }

        if (_image is null)
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, viewport.X, viewport.Y, viewport.Width - 1, viewport.Height - 1);
            return;
        }

        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.DrawImage(_image, viewport);
    }

    private Rectangle GetViewport()
    {
        var margin = 12;
        var availableWidth = Math.Max(1, ClientSize.Width - margin * 2);
        var availableHeight = Math.Max(1, ClientSize.Height - margin * 2);
        var aspect = _image is null || _image.Height == 0 ? 16d / 9d : _image.Width / (double)_image.Height;
        var width = availableWidth;
        var height = (int)Math.Round(width / aspect);
        if (height > availableHeight)
        {
            height = availableHeight;
            width = (int)Math.Round(height * aspect);
        }
        return new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
