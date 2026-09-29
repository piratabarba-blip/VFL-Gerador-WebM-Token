namespace VFL.GeradorWebMToken;

internal sealed class CheckerPreview : Control
{
    private const int ReferenceSize = 500;
    private static readonly Color CheckerLight = Color.FromArgb(68, 73, 82);
    private static readonly Color CheckerDark = Color.FromArgb(42, 47, 56);
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
        using var light = new SolidBrush(CheckerLight);
        using var dark = new SolidBrush(CheckerDark);
        for (var y = viewport.Top; y < viewport.Bottom; y += size)
            for (var x = viewport.Left; x < viewport.Right; x += size)
            {
                var cell = new Rectangle(x, y, Math.Min(size, viewport.Right - x), Math.Min(size, viewport.Bottom - y));
                e.Graphics.FillRectangle((((x - viewport.Left) / size + (y - viewport.Top) / size) & 1) == 0 ? light : dark, cell);
            }
        if (_image is null) return;
        e.Graphics.DrawImage(_image, viewport);
    }

    public bool SaveThumbnail(string path)
    {
        if (_image is null) return false;

        const int outputSize = 1080;
        const int checkerSize = 36;
        using var bitmap = new Bitmap(outputSize, outputSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var light = new SolidBrush(CheckerLight);
        using var dark = new SolidBrush(CheckerDark);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        for (var y = 0; y < outputSize; y += checkerSize)
        for (var x = 0; x < outputSize; x += checkerSize)
        {
            var cell = new Rectangle(x, y, Math.Min(checkerSize, outputSize - x), Math.Min(checkerSize, outputSize - y));
            graphics.FillRectangle(((x / checkerSize + y / checkerSize) & 1) == 0 ? light : dark, cell);
        }

        graphics.DrawImage(_image, new Rectangle(0, 0, outputSize, outputSize));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
