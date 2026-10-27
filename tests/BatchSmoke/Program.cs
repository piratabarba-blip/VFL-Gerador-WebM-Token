using VFL.GeradorWebMToken;

if (args.Length < 2) throw new ArgumentException("Informe a pasta de saída e pelo menos um vídeo.");
var outputDirectory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);
var analyzer = new VideoAnalyzer();
var renderer = new WebmRenderer();
var completed = 0;

foreach (var inputArgument in args.Skip(1))
{
    var input = Path.GetFullPath(inputArgument);
    var output = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(input) + "_TOKEN.webm");
    var messages = new Progress<string>(message => Console.WriteLine($"ANALYZE {Path.GetFileName(input)} {message}"));
    var analysis = await analyzer.AnalyzeAsync(input, 0.260, 0.30, 100, 0, 0, 0, messages, CancellationToken.None);
    try
    {
        var progress = new Progress<RenderProgress>(item => Console.WriteLine($"RENDER {Path.GetFileName(input)} {item.Percent:0}%"));
        await renderer.RenderAsync(input, output, analysis, 0.260, 0.30, 100, 0, 0, 0,
            QualityPreset.Leve, false, progress, CancellationToken.None);
    }
    finally { try { File.Delete(analysis.PreviewPath); } catch { } }
    if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new InvalidOperationException("Saída não criada: " + output);
    completed++;
}

Console.WriteLine($"BATCH_COMPLETED={completed}");
