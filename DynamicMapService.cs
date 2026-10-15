using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace VFL.GeradorWebMToken;

internal sealed class DynamicMapService
{
    private readonly string _ffmpeg = VideoAnalyzer.FindTool("ffmpeg") ?? throw new InvalidOperationException(
        "FFmpeg portátil não encontrado. Extraia novamente a pasta completa do programa.");
    private readonly string _ffprobe = VideoAnalyzer.FindTool("ffprobe") ?? throw new InvalidOperationException(
        "FFprobe portátil não encontrado. Extraia novamente a pasta completa do programa.");

    public async Task<MapAnalysis> AnalyzeAsync(string input, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var video = await ProbeAsync(input, cancellationToken);
        var tempFolder = Path.Combine(Path.GetTempPath(), "VFL-Mapa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            progress.Report("Analisando o vídeo e detectando a cor das bordas...");
            var arguments = DecoderArguments(video);
            arguments.AddRange(["-i", input, "-vf", "fps=0.5,scale=960:-2", "-frames:v", "12", Path.Combine(tempFolder, "frame-%02d.png")]);
            await RunAsync(_ffmpeg, arguments, cancellationToken);
            var frames = Directory.GetFiles(tempFolder, "frame-*.png").OrderBy(path => path).ToArray();
            if (frames.Length == 0)
            {
                var frame = Path.Combine(tempFolder, "frame-01.png");
                arguments = DecoderArguments(video);
                arguments.AddRange(["-ss", "0", "-i", input, "-frames:v", "1", "-vf", "scale=960:-2", frame]);
                await RunAsync(_ffmpeg, arguments, cancellationToken);
                frames = [frame];
            }
            var (background, confidence) = DetectBackground(frames);
            progress.Report($"Vídeo pronto • fundo provável #{background.R:X2}{background.G:X2}{background.B:X2}");
            return new MapAnalysis(background, video, confidence);
        }
        finally
        {
            try { Directory.Delete(tempFolder, true); } catch { }
        }
    }

    public async Task<string> CreatePreviewAsync(string input, MapAnalysis analysis, bool removeBackground,
        double similarity, double blend, double positionSeconds, CancellationToken cancellationToken)
    {
        var output = Path.Combine(Path.GetTempPath(), "VFL-Mapa-preview-" + Guid.NewGuid().ToString("N") + ".png");
        var filter = BuildPreviewFilter(analysis, removeBackground, similarity, blend);
        var finalFrameSecond = Math.Max(0, analysis.Video.Duration - 1 / Math.Max(1, analysis.Video.FrameRate));
        var seek = Math.Clamp(positionSeconds, 0, finalFrameSecond);
        var arguments = DecoderArguments(analysis.Video);
        arguments.AddRange(["-y", "-ss", seek.ToString("0.###", CultureInfo.InvariantCulture), "-i", input,
            "-frames:v", "1", "-vf", filter, output]);
        try
        {
            await RunAsync(_ffmpeg, arguments, cancellationToken);
            return output;
        }
        catch
        {
            try { File.Delete(output); } catch { }
            throw;
        }
    }

    public async Task StreamPreviewAsync(string input, MapAnalysis analysis, bool removeBackground,
        double similarity, double blend, bool stabilize, double positionSeconds, Func<Bitmap, double, Task> showFrame,
        CancellationToken cancellationToken)
    {
        const double previewFps = 24;
        var (width, height) = FitWithin(analysis.Video.Width, analysis.Video.Height, 900, 500);
        var finalFrameSecond = Math.Max(0, analysis.Video.Duration - 1 / Math.Max(1, analysis.Video.FrameRate));
        var seek = Math.Clamp(positionSeconds, 0, finalFrameSecond);
        var filter = BuildBaseFilter(analysis, removeBackground, similarity, blend,
                         stabilize ? "deshake=rx=16:ry=16:edge=mirror" : null) +
                     $",scale={width}:{height}:flags=fast_bilinear,fps={previewFps.ToString(CultureInfo.InvariantCulture)},format=bgra";
        var arguments = DecoderArguments(analysis.Video);
        arguments.AddRange(["-hide_banner", "-loglevel", "error", "-re", "-ss",
            seek.ToString("0.###", CultureInfo.InvariantCulture), "-i", input,
            "-an", "-sn", "-dn", "-vf", filter, "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1"]);
        var psi = CreateProcess(_ffmpeg, arguments, redirectOutput: true);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar a prévia.");
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var errorTask = process.StandardError.ReadToEndAsync();
        var buffer = new byte[width * height * 4];
        var frameIndex = 0L;
        try
        {
            while (await ReadFrameAsync(process.StandardOutput.BaseStream, buffer, cancellationToken))
            {
                var bitmap = CreateBitmap(buffer, width, height);
                try { await showFrame(bitmap, Math.Min(analysis.Video.Duration, seek + frameIndex / previewFps)); }
                catch { bitmap.Dispose(); throw; }
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

    public async Task StreamLoopPreviewAsync(string input, MapAnalysis analysis, bool removeBackground,
        double similarity, double blend, bool stabilize, double loopDuration, Func<Bitmap, double, Task> showFrame,
        CancellationToken cancellationToken)
    {
        const double previewFps = 24;
        var fade = ClampLoopDuration(analysis.Video.Duration, loopDuration);
        var context = Math.Min(1d, Math.Max(0.35d, (analysis.Video.Duration - fade * 2) / 2));
        var tailStart = Math.Max(0, analysis.Video.Duration - fade - context);
        var afterEnd = Math.Min(analysis.Video.Duration, fade + context);
        var (width, height) = FitWithin(analysis.Video.Width, analysis.Video.Height, 900, 500);
        var ci = CultureInfo.InvariantCulture;
        var baseFilter = BuildBaseFilter(analysis, removeBackground, similarity, blend,
            stabilize ? "deshake=rx=16:ry=16:edge=mirror" : null);
        var filter =
            $"[0:v]{baseFilter},fps={previewFps.ToString(ci)},settb=AVTB,split=3[tail][head][after];" +
            $"[tail]trim=start={tailStart.ToString("0.######", ci)}:end={analysis.Video.Duration.ToString("0.######", ci)},setpts=PTS-STARTPTS[vt];" +
            $"[head]trim=start=0:end={fade.ToString("0.######", ci)},setpts=PTS-STARTPTS[vh];" +
            $"[after]trim=start={fade.ToString("0.######", ci)}:end={afterEnd.ToString("0.######", ci)},setpts=PTS-STARTPTS[va];" +
            $"[vt][vh]xfade=transition=fade:duration={fade.ToString("0.######", ci)}:offset={context.ToString("0.######", ci)}[vx];" +
            $"[vx][va]concat=n=2:v=1:a=0,scale={width}:{height}:flags=fast_bilinear,format=bgra[vout]";
        var arguments = DecoderArguments(analysis.Video);
        arguments.AddRange(["-hide_banner", "-loglevel", "error", "-i", input, "-an", "-sn", "-dn",
            "-filter_complex", filter, "-map", "[vout]", "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1"]);
        var psi = CreateProcess(_ffmpeg, arguments, redirectOutput: true);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o teste de loop.");
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var errorTask = process.StandardError.ReadToEndAsync();
        var buffer = new byte[width * height * 4];
        var frameIndex = 0L;
        try
        {
            while (await ReadFrameAsync(process.StandardOutput.BaseStream, buffer, cancellationToken))
            {
                var bitmap = CreateBitmap(buffer, width, height);
                try { await showFrame(bitmap, frameIndex / previewFps); }
                catch { bitmap.Dispose(); throw; }
                frameIndex++;
            }
            await process.WaitForExitAsync(cancellationToken);
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Falha ao testar o loop.\n" + error[^Math.Min(error.Length, 2000)..]);
        }
        catch (OperationCanceledException) { throw; }
        catch when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
    }

    public async Task RenderAsync(string input, string output, MapAnalysis analysis, bool removeBackground,
        double similarity, double blend, MapQualityPreset quality, double? frameRate, bool keepAudio,
        bool seamlessLoop, double loopDuration, bool stabilize,
        IProgress<RenderProgress> progress, CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output))
            ?? throw new InvalidOperationException("Pasta de saída inválida.");
        Directory.CreateDirectory(outputDirectory);
        var temporaryOutput = Path.Combine(outputDirectory,
            $".{Path.GetFileNameWithoutExtension(output)}-{Guid.NewGuid():N}.rendering.webm");
        var hasAlpha = removeBackground || analysis.Video.HasAlpha;
        var crf = quality switch { MapQualityPreset.Alta => 20, MapQualityPreset.Compacta => 34, _ => 28 };
        var cpuUsed = quality switch { MapQualityPreset.Alta => 2, MapQualityPreset.Compacta => 4, _ => 3 };
        string? transformsPath = null;
        try
        {
            string? stabilizationFilter = null;
            if (stabilize)
            {
                transformsPath = Path.Combine(Path.GetTempPath(), "VFL-estabilizacao-" + Guid.NewGuid().ToString("N") + ".trf");
                progress.Report(new RenderProgress(1, "Analisando tremidas e movimentos da câmera..."));
                await DetectStabilizationAsync(input, analysis.Video, transformsPath, cancellationToken);
                stabilizationFilter = $"vidstabtransform=input='{EscapeFilterPath(transformsPath)}':smoothing=15:optzoom=2:zoomspeed=0.15:interpol=bicubic";
            }

            var arguments = DecoderArguments(analysis.Video);
            arguments.AddRange(["-y", "-hide_banner", "-progress", "pipe:1", "-nostats", "-i", input]);
            var effectiveDuration = analysis.Video.Duration;
            if (seamlessLoop)
            {
                var fade = ClampLoopDuration(analysis.Video.Duration, loopDuration);
                effectiveDuration -= fade;
                var targetFps = frameRate ?? Math.Clamp(analysis.Video.FrameRate, 12, 60);
                var filter = BuildSeamlessFilter(analysis, removeBackground, similarity, blend, targetFps, fade,
                    keepAudio && analysis.Video.HasAudio, stabilizationFilter);
                arguments.AddRange(["-filter_complex", filter, "-map", "[vout]"]);
                if (keepAudio && analysis.Video.HasAudio)
                    arguments.AddRange(["-map", "[aout]", "-c:a", "libopus", "-b:a", "96k"]);
                else
                    arguments.Add("-an");
            }
            else
            {
                var filter = BuildRenderFilter(analysis, removeBackground, similarity, blend, frameRate, stabilizationFilter);
                if (!string.IsNullOrWhiteSpace(filter)) arguments.AddRange(["-vf", filter]);
                arguments.AddRange(["-map", "0:v:0"]);
                if (keepAudio && analysis.Video.HasAudio)
                    arguments.AddRange(["-map", "0:a?", "-c:a", "libopus", "-b:a", "96k"]);
                else
                    arguments.Add("-an");
            }
            arguments.AddRange(["-c:v", "libvpx-vp9", "-pix_fmt", hasAlpha ? "yuva420p" : "yuv420p",
                "-b:v", "0", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-deadline", "good",
                "-cpu-used", cpuUsed.ToString(CultureInfo.InvariantCulture), "-row-mt", "1", "-tile-columns", "2", "-g", "120"]);
            arguments.Add(temporaryOutput);

            var psi = CreateProcess(_ffmpeg, arguments, redirectOutput: true);
            using var process = new Process { StartInfo = psi };
            var errors = new StringBuilder();
            process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) errors.AppendLine(eventArgs.Data); };
            var completed = false;
            try
            {
                process.Start();
                process.BeginErrorReadLine();
                using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken) ?? "";
                    if (line.StartsWith("out_time_ms=") && long.TryParse(line[12..], out var micros))
                    {
                        var percent = Math.Clamp(micros / 1_000_000d / effectiveDuration * 100, 0, 99.5);
                        progress.Report(new RenderProgress(percent, $"Otimizando mapa para Foundry... {percent:0}%"));
                    }
                }
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Falha ao gerar o mapa WebM.\n" + errors.ToString()[^Math.Min(errors.Length, 3000)..]);
                File.Move(temporaryOutput, output, true);
                completed = true;
                progress.Report(new RenderProgress(100, "Mapa WebM otimizado concluído!"));
            }
            finally
            {
                if (!completed) try { File.Delete(temporaryOutput); } catch { }
            }
        }
        finally
        {
            if (transformsPath is not null) try { File.Delete(transformsPath); } catch { }
        }
    }

    private static string BuildPreviewFilter(MapAnalysis analysis, bool removeBackground, double similarity, double blend) =>
        BuildBaseFilter(analysis, removeBackground, similarity, blend) +
        ",scale=960:540:force_original_aspect_ratio=decrease:flags=lanczos";

    private static string BuildRenderFilter(MapAnalysis analysis, bool removeBackground, double similarity,
        double blend, double? frameRate, string? stabilizationFilter)
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(stabilizationFilter)) filters.Add(stabilizationFilter);
        if (removeBackground)
            filters.Add(BuildChromaFilter(analysis.BackgroundColor, similarity, blend));
        else if (analysis.Video.HasAlpha)
            filters.Add("format=rgba," + CleanTransparentRgb);
        if (frameRate.HasValue)
            filters.Add("fps=" + frameRate.Value.ToString("0.###", CultureInfo.InvariantCulture));
        filters.Add(removeBackground || analysis.Video.HasAlpha ? "format=yuva420p" : "format=yuv420p");
        return string.Join(',', filters);
    }

    private static string BuildSeamlessFilter(MapAnalysis analysis, bool removeBackground, double similarity,
        double blend, double frameRate, double fade, bool includeAudio, string? stabilizationFilter)
    {
        var ci = CultureInfo.InvariantCulture;
        var duration = analysis.Video.Duration;
        var offset = duration - fade * 2;
        var pixelFormat = removeBackground || analysis.Video.HasAlpha ? "yuva420p" : "yuv420p";
        var video =
            $"[0:v]{BuildBaseFilter(analysis, removeBackground, similarity, blend, stabilizationFilter)}," +
            $"fps={frameRate.ToString("0.###", ci)},settb=AVTB,split=2[main][head];" +
            $"[main]trim=start={fade.ToString("0.######", ci)},setpts=PTS-STARTPTS[vm];" +
            $"[head]trim=start=0:end={fade.ToString("0.######", ci)},setpts=PTS-STARTPTS[vh];" +
            $"[vm][vh]xfade=transition=fade:duration={fade.ToString("0.######", ci)}:" +
            $"offset={offset.ToString("0.######", ci)},format={pixelFormat}[vout]";
        if (!includeAudio) return video;
        return video +
            $";[0:a]asplit=2[amain][ahead];" +
            $"[amain]atrim=start={fade.ToString("0.######", ci)},asetpts=PTS-STARTPTS[am];" +
            $"[ahead]atrim=start=0:end={fade.ToString("0.######", ci)},asetpts=PTS-STARTPTS[ah];" +
            $"[am][ah]acrossfade=d={fade.ToString("0.######", ci)}:c1=qsin:c2=qsin[aout]";
    }

    private static double ClampLoopDuration(double duration, double requested)
    {
        if (duration < 0.75)
            throw new InvalidOperationException("O vídeo precisa ter pelo menos 0,75 segundo para criar um loop suave.");
        return Math.Clamp(requested, 0.10, duration / 3d);
    }

    private static string BuildBaseFilter(MapAnalysis analysis, bool removeBackground, double similarity, double blend,
        string? stabilizationFilter = null)
    {
        var prefix = string.IsNullOrWhiteSpace(stabilizationFilter) ? "" : stabilizationFilter + ",";
        if (removeBackground) return prefix + BuildChromaFilter(analysis.BackgroundColor, similarity, blend);
        return prefix + (analysis.Video.HasAlpha ? "format=rgba," + CleanTransparentRgb : "format=rgba");
    }

    private async Task DetectStabilizationAsync(string input, VideoInfo video, string transformsPath,
        CancellationToken cancellationToken)
    {
        var arguments = DecoderArguments(video);
        arguments.AddRange(["-y", "-hide_banner", "-loglevel", "error", "-i", input, "-an", "-sn", "-dn",
            "-vf", $"vidstabdetect=shakiness=5:accuracy=9:stepsize=6:mincontrast=0.25:result='{EscapeFilterPath(transformsPath)}'",
            "-f", "null", "-"]);
        await RunAsync(_ffmpeg, arguments, cancellationToken);
    }

    private static string EscapeFilterPath(string path) =>
        path.Replace("\\", "/", StringComparison.Ordinal).Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

    private static string BuildChromaFilter(Color color, double similarity, double blend)
    {
        var ci = CultureInfo.InvariantCulture;
        var key = $"0x{color.R:X2}{color.G:X2}{color.B:X2}";
        return $"format=rgba,colorkey={key}:{similarity.ToString("0.###", ci)}:{blend.ToString("0.###", ci)}," +
               "lut=a='if(lt(val,64),0,if(gt(val,128),255,(val-64)*255/64))'," + CleanTransparentRgb;
    }

    private const string CleanTransparentRgb =
        "geq=r='if(eq(alpha(X,Y),0),0,r(X,Y))':" +
        "g='if(eq(alpha(X,Y),0),0,g(X,Y))':" +
        "b='if(eq(alpha(X,Y),0),0,b(X,Y))':a='alpha(X,Y)'";

    private async Task<VideoInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var output = await RunCaptureAsync(_ffprobe,
            ["-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,width,height,avg_frame_rate,pix_fmt:stream_tags=alpha_mode", "-of", "json", path], cancellationToken);
        using var document = JsonDocument.Parse(output);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.First(stream => stream.GetProperty("codec_type").GetString() == "video");
        var codec = video.TryGetProperty("codec_name", out var codecProperty) ? codecProperty.GetString() ?? "" : "";
        var pixelFormat = video.TryGetProperty("pix_fmt", out var pixelProperty) ? pixelProperty.GetString() ?? "" : "";
        var tagAlpha = video.TryGetProperty("tags", out var tags) && tags.TryGetProperty("alpha_mode", out var alphaMode)
            && alphaMode.GetString() == "1";
        var hasAlpha = tagAlpha || pixelFormat.StartsWith("yuva", StringComparison.OrdinalIgnoreCase)
            || pixelFormat.StartsWith("rgba", StringComparison.OrdinalIgnoreCase)
            || pixelFormat.StartsWith("bgra", StringComparison.OrdinalIgnoreCase)
            || pixelFormat.StartsWith("argb", StringComparison.OrdinalIgnoreCase)
            || pixelFormat.StartsWith("abgr", StringComparison.OrdinalIgnoreCase)
            || pixelFormat.StartsWith("gbrap", StringComparison.OrdinalIgnoreCase);
        var durationText = document.RootElement.GetProperty("format").GetProperty("duration").GetString() ?? "0";
        return new VideoInfo(video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(),
            double.Parse(durationText, CultureInfo.InvariantCulture), ParseRate(video.GetProperty("avg_frame_rate").GetString()),
            streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio"), hasAlpha, codec);
    }

    private static (Color Color, double Confidence) DetectBackground(IEnumerable<string> frames)
    {
        var bins = new Dictionary<int, (long R, long G, long B, int Count)>();
        var total = 0;
        foreach (var frame in frames)
        {
            using var bitmap = new Bitmap(frame);
            var border = Math.Max(8, Math.Min(bitmap.Width, bitmap.Height) / 24);
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

    private static List<string> DecoderArguments(VideoInfo video) =>
        video.HasAlpha && video.Codec.Equals("vp9", StringComparison.OrdinalIgnoreCase)
            ? ["-c:v", "libvpx-vp9"]
            : [];

    private static (int Width, int Height) FitWithin(int width, int height, int maximumWidth, int maximumHeight)
    {
        var scale = Math.Min(1d, Math.Min(maximumWidth / (double)width, maximumHeight / (double)height));
        var fittedWidth = Math.Max(2, (int)Math.Round(width * scale));
        var fittedHeight = Math.Max(2, (int)Math.Round(height * scale));
        fittedWidth -= fittedWidth % 2;
        fittedHeight -= fittedHeight % 2;
        return (fittedWidth, fittedHeight);
    }

    private static double ParseRate(string? value)
    {
        var parts = (value ?? "24/1").Split('/');
        return parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b != 0 ? a / b : 24;
    }

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
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(bgra, 0, data.Scan0, bgra.Length); }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    private static ProcessStartInfo CreateProcess(string executable, IEnumerable<string> arguments, bool redirectOutput)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (redirectOutput) psi.StandardOutputEncoding = Encoding.UTF8;
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        return psi;
    }

    private static async Task RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using var process = Process.Start(CreateProcess(executable, arguments, redirectOutput: false))
            ?? throw new InvalidOperationException("Não foi possível iniciar o FFmpeg.");
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Falha ao processar o vídeo.\n" + error[^Math.Min(error.Length, 2000)..]);
    }

    private static async Task<string> RunCaptureAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using var process = Process.Start(CreateProcess(executable, arguments, redirectOutput: true))
            ?? throw new InvalidOperationException("Não foi possível iniciar o FFprobe.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output;
    }
}
