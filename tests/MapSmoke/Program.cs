using VFL.GeradorWebMToken;

if (args.Length is < 3 or > 5) throw new ArgumentException("Informe vídeo de entrada, WebM comum, WebM alpha, áudio opcional e estabilização opcional.");
var input = Path.GetFullPath(args[0]);
var opaqueOutput = Path.GetFullPath(args[1]);
var alphaOutput = args[2] == "-" ? null : Path.GetFullPath(args[2]);
var keepAudio = args.Length >= 4 && bool.Parse(args[3]);
var stabilize = args.Length >= 5 && bool.Parse(args[4]);
var service = new DynamicMapService();
var messages = new Progress<string>(Console.WriteLine);
var analysis = await service.AnalyzeAsync(input, messages, CancellationToken.None);
Console.WriteLine($"INFO={analysis.Video.Width}x{analysis.Video.Height} {analysis.Video.FrameRate:0.##}fps");
Console.WriteLine($"COLOR=#{analysis.BackgroundColor.R:X2}{analysis.BackgroundColor.G:X2}{analysis.BackgroundColor.B:X2}");
var progress = new Progress<RenderProgress>(item => Console.WriteLine($"{item.Percent:0}% {item.Message}"));
var previewFrames = 0;
await service.StreamLoopPreviewAsync(input, analysis, true, 0.260, 0.30, true, 1.0,
    (bitmap, _) => { bitmap.Dispose(); previewFrames++; return Task.CompletedTask; }, CancellationToken.None);
Console.WriteLine($"LOOP_PREVIEW_FRAMES={previewFrames}");
await service.RenderAsync(input, opaqueOutput, analysis, false, 0.260, 0.30,
    MapQualityPreset.Equilibrada, 24, keepAudio, false, 1.0, stabilize, progress, CancellationToken.None);
if (alphaOutput is not null)
    await service.RenderAsync(input, alphaOutput, analysis, true, 0.260, 0.30,
        MapQualityPreset.Equilibrada, 24, keepAudio, true, 1.0, true, progress, CancellationToken.None);
Console.WriteLine($"OPAQUE={opaqueOutput}");
if (alphaOutput is not null) Console.WriteLine($"ALPHA={alphaOutput}");
