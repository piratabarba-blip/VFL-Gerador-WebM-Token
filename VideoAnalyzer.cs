using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VFL.GeradorWebMToken;

internal sealed class VideoAnalyzer
{
    private readonly string _ffmpeg;
    private readonly string _ffprobe;

    public VideoAnalyzer()
    {
        _ffmpeg = FindTool("ffmpeg") ?? throw new InvalidOperationException(
            "FFmpeg portátil não encontrado. Extraia novamente a pasta completa do programa.");
        _ffprobe = FindTool("ffprobe") ?? throw new InvalidOperationException(
            "FFprobe portátil não encontrado. Extraia novamente a pasta completa do programa.");
    }

    public async Task<AnalysisResult> AnalyzeAsync(string videoPath, double similarity, double blend, double zoomPercent,
        int horizontalOffset, int verticalOffset,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        var info = await ProbeAsync(videoPath, cancellationToken);
        var tempFolder = Path.Combine(Path.GetTempPath(), "VFL-WebM-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            progress.Report("Extraindo quadros para análise...");
            await RunAsync(_ffmpeg,
                ["-y", "-i", videoPath, "-vf", "fps=1,scale=960:-2", "-frames:v", "12", Path.Combine(tempFolder, "frame-%02d.png")],
                cancellationToken);
            var frames = Directory.GetFiles(tempFolder, "frame-*.png").OrderBy(path => path).ToArray();
            if (frames.Length == 0)
            {
                var single = Path.Combine(tempFolder, "frame-01.png");
                await RunAsync(_ffmpeg, ["-y", "-ss", "0", "-i", videoPath, "-frames:v", "1", "-vf", "scale=960:-2", single], cancellationToken);
                frames = [single];
            }

            progress.Report("Detectando a cor do fundo...");
            var (background, confidence) = DetectBackground(frames);
            progress.Report($"Fundo detectado: #{background.R:X2}{background.G:X2}{background.B:X2}");
            var bounds = DetectSubjectBounds(frames, background, info.Width, info.Height);

            var previewSecond = 0d;
            var preview = await CreatePreviewAsync(videoPath, background, bounds, info, similarity, blend,
                zoomPercent, horizontalOffset, verticalOffset, previewSecond, cancellationToken);
            return new AnalysisResult(background, bounds, info, preview, confidence);
        }
        finally
        {
            try { Directory.Delete(tempFolder, true); } catch { }
        }
    }

    public Task<string> CreatePreviewAsync(string videoPath, AnalysisResult analysis, double similarity,
        double blend, double zoomPercent, int horizontalOffset, int verticalOffset,
        double positionSeconds, CancellationToken cancellationToken) =>
        CreatePreviewAsync(videoPath, analysis.BackgroundColor, analysis.SubjectBounds, analysis.Video,
            similarity, blend, zoomPercent, horizontalOffset, verticalOffset, positionSeconds, cancellationToken);

    public async Task StreamPreviewAsync(string videoPath, AnalysisResult analysis, double similarity,
        double blend, double zoomPercent, int horizontalOffset, int verticalOffset,
        double positionSeconds, Func<Bitmap, double, Task> showFrame, CancellationToken cancellationToken)
    {
        const int previewSize = 500;
        const double previewFps = 24;
        var finalFrameSecond = Math.Max(0, analysis.Video.Duration - 1 / Math.Max(1, analysis.Video.FrameRate));
        var seekSecond = Math.Clamp(positionSeconds, 0, finalFrameSecond);
        var filter = BuildVideoFilter(analysis.BackgroundColor, analysis.SubjectBounds, similarity, blend,
            zoomPercent, horizontalOffset, verticalOffset) +
            $",scale={previewSize}:{previewSize}:flags=fast_bilinear,fps={previewFps.ToString(CultureInfo.InvariantCulture)},format=bgra";
        var psi = new ProcessStartInfo(_ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-re", "-ss",
            seekSecond.ToString("0.###", CultureInfo.InvariantCulture), "-i", videoPath,
            "-an", "-sn", "-dn", "-vf", filter, "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1"
        }) psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar a prévia do FFmpeg.");
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        });
        var errorTask = process.StandardError.ReadToEndAsync();
        var frameBytes = new byte[previewSize * previewSize * 4];
        var frameIndex = 0L;
        try
        {
            while (await ReadFrameAsync(process.StandardOutput.BaseStream, frameBytes, cancellationToken))
            {
                var bitmap = CreateBitmap(frameBytes, previewSize, previewSize);
                try
                {
                    await showFrame(bitmap, Math.Min(analysis.Video.Duration, seekSecond + frameIndex / previewFps));
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
                frameIndex++;
            }
            await process.WaitForExitAsync(cancellationToken);
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Falha ao reproduzir a prévia.\n" + error[^Math.Min(error.Length, 2000)..]);
        }
        catch (OperationCanceledException) { throw; }
        catch when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
    }

    private async Task<string> CreatePreviewAsync(string videoPath, Color background, Rectangle bounds,
        VideoInfo info, double similarity, double blend, double zoomPercent, int horizontalOffset,
        int verticalOffset, double positionSeconds, CancellationToken cancellationToken)
    {
        var preview = Path.Combine(Path.GetTempPath(), "VFL-WebM-preview-" + Guid.NewGuid().ToString("N") + ".png");
        var filter = BuildVideoFilter(background, bounds, similarity, blend, zoomPercent,
            horizontalOffset, verticalOffset);
        try
        {
            var finalFrameSecond = Math.Max(0, info.Duration - 1 / Math.Max(1, info.FrameRate));
            var seekSecond = Math.Clamp(positionSeconds, 0, finalFrameSecond);
            await RunAsync(_ffmpeg,
                ["-y", "-ss", seekSecond.ToString("0.###", CultureInfo.InvariantCulture),
                 "-i", videoPath, "-frames:v", "1", "-vf", filter, preview], cancellationToken);
            return preview;
        }
        catch
        {
            try { File.Delete(preview); } catch { }
            throw;
        }
    }

    public static string BuildVideoFilter(Color color, Rectangle bounds, double similarity, double blend,
        double zoomPercent = 100, int horizontalOffset = 0, int verticalOffset = 0)
    {
        var ci = CultureInfo.InvariantCulture;
        var key = $"0x{color.R:X2}{color.G:X2}{color.B:X2}";
        var targetSize = (int)Math.Round(972 * Math.Clamp(zoomPercent, 50, 180) / 100d);
        targetSize -= targetSize % 2;
        var wide = bounds.Width >= bounds.Height;
        var scaledWidth = wide ? targetSize : Even((int)Math.Round(bounds.Width * targetSize / (double)bounds.Height));
        var scaledHeight = wide ? Even((int)Math.Round(bounds.Height * targetSize / (double)bounds.Width)) : targetSize;
        var scale = wide ? $"scale={scaledWidth}:-2" : $"scale=-2:{scaledHeight}";
        var canvasWidth = Math.Max(1080, scaledWidth);
        var canvasHeight = Math.Max(1080, scaledHeight);
        var offsetX = Math.Clamp(horizontalOffset, -400, 400);
        // Valores positivos acompanham a seta para cima do controle e movem o personagem para cima.
        var offsetY = Math.Clamp(-verticalOffset, -400, 400);
        var padX = scaledWidth <= 1080
            ? Math.Clamp((1080 - scaledWidth) / 2 + offsetX, 0, 1080 - scaledWidth)
            : 0;
        var padY = scaledHeight <= 1080
            ? Math.Clamp((1080 - scaledHeight) / 2 + offsetY, 0, 1080 - scaledHeight)
            : 0;
        var cropX = scaledWidth > 1080
            ? Math.Clamp((scaledWidth - 1080) / 2 - offsetX, 0, scaledWidth - 1080)
            : 0;
        var cropY = scaledHeight > 1080
            ? Math.Clamp((scaledHeight - 1080) / 2 - offsetY, 0, scaledHeight - 1080)
            : 0;
        return $"format=rgba,colorkey={key}:{similarity.ToString("0.###", ci)}:{blend.ToString("0.###", ci)}," +
               "lut=a='if(lt(val,64),0,if(gt(val,128),255,(val-64)*255/64))'," +
               $"crop={bounds.Width}:{bounds.Height}:{bounds.X}:{bounds.Y}," +
               scale + "," +
               $"pad={canvasWidth}:{canvasHeight}:{padX}:{padY}:color=black@0," +
               $"crop=1080:1080:{cropX}:{cropY},format=yuva420p";
    }

    private static int Even(int value) => Math.Max(2, value - value % 2);

    private static async Task<bool> ReadFrameAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0) return false;
            read += count;
        }
        return true;
    }

    private static Bitmap CreateBitmap(byte[] bgra, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    private static (Color Color, double Confidence) DetectBackground(IEnumerable<string> framePaths)
    {
        var bins = new Dictionary<int, (long R, long G, long B, int Count)>();
        var total = 0;
        foreach (var path in framePaths)
        {
            using var bitmap = new Bitmap(path);
            var border = Math.Max(8, Math.Min(bitmap.Width, bitmap.Height) / 25);
            for (var y = 0; y < bitmap.Height; y += 4)
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                if (x >= border && x < bitmap.Width - border && y >= border && y < bitmap.Height - border) continue;
                var pixel = bitmap.GetPixel(x, y);
                var key = (pixel.R >> 5) << 10 | (pixel.G >> 5) << 5 | (pixel.B >> 5);
                bins.TryGetValue(key, out var value);
                bins[key] = (value.R + pixel.R, value.G + pixel.G, value.B + pixel.B, value.Count + 1);
                total++;
            }
        }
        var dominant = bins.Values.OrderByDescending(value => value.Count).First();
        return (Color.FromArgb((int)(dominant.R / dominant.Count), (int)(dominant.G / dominant.Count), (int)(dominant.B / dominant.Count)),
            dominant.Count / (double)Math.Max(1, total));
    }

    private static Rectangle DetectSubjectBounds(IEnumerable<string> framePaths, Color background, int originalWidth, int originalHeight)
    {
        var union = Rectangle.Empty;
        var sampleWidth = 0;
        var sampleHeight = 0;
        foreach (var path in framePaths)
        {
            using var bitmap = new Bitmap(path);
            sampleWidth = bitmap.Width; sampleHeight = bitmap.Height;
            var minX = bitmap.Width; var minY = bitmap.Height; var maxX = -1; var maxY = -1;
            for (var y = 0; y < bitmap.Height; y += 2)
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                var dr = pixel.R - background.R; var dg = pixel.G - background.G; var db = pixel.B - background.B;
                if (dr * dr + dg * dg + db * db < 75 * 75) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            if (maxX >= minX && maxY >= minY)
                union = union.IsEmpty ? Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1)
                    : Rectangle.Union(union, Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1));
        }
        if (union.IsEmpty) return new Rectangle(0, 0, originalWidth - originalWidth % 2, originalHeight - originalHeight % 2);
        var marginX = Math.Max(12, (int)(union.Width * 0.08));
        var marginY = Math.Max(12, (int)(union.Height * 0.08));
        union.Inflate(marginX, marginY);
        union.Intersect(new Rectangle(0, 0, sampleWidth, sampleHeight));
        var scaleX = originalWidth / (double)sampleWidth;
        var scaleY = originalHeight / (double)sampleHeight;
        var x0 = Math.Max(0, (int)Math.Floor(union.X * scaleX));
        var y0 = Math.Max(0, (int)Math.Floor(union.Y * scaleY));
        var x1 = Math.Min(originalWidth, (int)Math.Ceiling(union.Right * scaleX));
        var y1 = Math.Min(originalHeight, (int)Math.Ceiling(union.Bottom * scaleY));
        x0 -= x0 % 2; y0 -= y0 % 2; x1 -= x1 % 2; y1 -= y1 % 2;
        return new Rectangle(x0, y0, Math.Max(2, x1 - x0), Math.Max(2, y1 - y0));
    }

    private async Task<VideoInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var output = await RunCaptureAsync(_ffprobe,
            ["-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height,avg_frame_rate", "-of", "json", path], cancellationToken);
        using var document = JsonDocument.Parse(output);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.First(stream => stream.GetProperty("codec_type").GetString() == "video");
        var width = video.GetProperty("width").GetInt32();
        var height = video.GetProperty("height").GetInt32();
        var fps = ParseRate(video.GetProperty("avg_frame_rate").GetString());
        var duration = double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString() ?? "0", CultureInfo.InvariantCulture);
        return new VideoInfo(width, height, duration, fps, streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio"));
    }

    private static double ParseRate(string? value)
    {
        var parts = (value ?? "24/1").Split('/');
        return parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b != 0 ? a / b : 24;
    }

    internal static string? FindTool(string name)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Tools", name + ".exe"),
            name + ".exe"
        };
        foreach (var path in candidates)
        {
            try { using var process = Process.Start(new ProcessStartInfo(path, "-version") { UseShellExecute = false, CreateNoWindow = true }); if (process is not null) { process.WaitForExit(2000); if (process.ExitCode == 0) return path; } } catch { }
        }
        return null;
    }

    private static async Task RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o FFmpeg.");
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException("Falha ao analisar o vídeo.\n" + error[^Math.Min(error.Length, 2000)..]);
    }

    private static async Task<string> RunCaptureAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o FFprobe.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output;
    }
}
