using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VFL.GeradorWebMToken;

internal sealed class WebmRenderer
{
    private readonly string _ffmpeg = VideoAnalyzer.FindTool("ffmpeg") ?? throw new InvalidOperationException("FFmpeg não encontrado.");

    public async Task RenderAsync(string input, string output, AnalysisResult analysis, double similarity,
        double blend, double zoomPercent, int horizontalOffset, int verticalOffset,
        QualityPreset quality, bool keepAudio,
        IProgress<RenderProgress> progress, CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output))
            ?? throw new InvalidOperationException("Pasta de saída inválida.");
        Directory.CreateDirectory(outputDirectory);
        var temporaryOutput = Path.Combine(outputDirectory,
            $".{Path.GetFileNameWithoutExtension(output)}-{Guid.NewGuid():N}.rendering.webm");
        var crf = quality switch { QualityPreset.Leve => 30, QualityPreset.Alta => 5, _ => 15 };
        var filter = VideoAnalyzer.BuildVideoFilter(analysis.BackgroundColor, analysis.SubjectBounds,
            similarity, blend, zoomPercent, horizontalOffset, verticalOffset);
        var args = new List<string>
        {
            "-y", "-hide_banner", "-progress", "pipe:1", "-nostats", "-i", input,
            "-vf", filter, "-r", "24", "-f", "webm", "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p",
            "-b:v", "20M", "-crf", crf.ToString(CultureInfo.InvariantCulture),
            "-qcomp", "1", "-g", "15"
        };
        if (keepAudio && analysis.Video.HasAudio)
            args.AddRange(["-map", "0:v:0", "-map", "0:a?", "-c:a", "libvorbis", "-q:a", "4"]);
        else args.Add("-an");
        args.Add(temporaryOutput);

        var psi = new ProcessStartInfo(_ffmpeg)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in args) psi.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = psi };
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) errors.AppendLine(eventArgs.Data); };
        var completed = false;
        try
        {
            process.Start(); process.BeginErrorReadLine();
            using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken) ?? "";
                if (line.StartsWith("out_time_ms=") && long.TryParse(line[12..], out var micros))
                {
                    var percent = Math.Clamp(micros / 1_000_000d / analysis.Video.Duration * 100, 0, 99.5);
                    progress.Report(new RenderProgress(percent, $"Gerando WebM... {percent:0}%"));
                }
            }
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) throw new InvalidOperationException("Falha ao gerar o WebM.\n" + errors.ToString()[^Math.Min(errors.Length, 3000)..]);
            try
            {
                File.Move(temporaryOutput, output, true);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    "O WebM foi renderizado, mas o arquivo de destino está em uso.\n\n" +
                    "Feche o vídeo no Foundry, navegador ou reprodutor e tente novamente.", exception);
            }
            completed = true;
            progress.Report(new RenderProgress(100, "WebM transparente concluído!"));
        }
        finally
        {
            if (!completed) try { File.Delete(temporaryOutput); } catch { }
        }
    }
}
