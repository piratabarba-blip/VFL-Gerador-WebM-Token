using VFL.GeradorWebMToken;

if (args.Length is < 3 or > 4) throw new ArgumentException("Informe vídeo de entrada, WebM comum, WebM alpha e áudio opcional.");
var input = Path.GetFullPath(args[0]);
var opaqueOutput = Path.GetFullPath(args[1]);
var alphaOutput = Path.GetFullPath(args[2]);
var keepAudio = args.Length == 4 && bool.Parse(args[3]);
var service = new DynamicMapService();
var messages = new Progress<string>(Console.WriteLine);
var analysis = await service.AnalyzeAsync(input, messages, CancellationToken.None);
Console.WriteLine($"INFO={analysis.Video.Width}x{analysis.Video.Height} {analysis.Video.FrameRate:0.##}fps");
Console.WriteLine($"COLOR=#{analysis.BackgroundColor.R:X2}{analysis.BackgroundColor.G:X2}{analysis.BackgroundColor.B:X2}");
var progress = new Progress<RenderProgress>(item => Console.WriteLine($"{item.Percent:0}% {item.Message}"));
await service.RenderAsync(input, opaqueOutput, analysis, false, 0.260, 0.30,
    MapQualityPreset.Equilibrada, 24, keepAudio, progress, CancellationToken.None);
await service.RenderAsync(input, alphaOutput, analysis, true, 0.260, 0.30,
    MapQualityPreset.Equilibrada, 24, keepAudio, progress, CancellationToken.None);
Console.WriteLine($"OPAQUE={opaqueOutput}");
Console.WriteLine($"ALPHA={alphaOutput}");
