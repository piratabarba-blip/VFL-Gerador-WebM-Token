namespace VFL.GeradorWebMToken;

internal sealed record VideoInfo(
    int Width,
    int Height,
    double Duration,
    double FrameRate,
    bool HasAudio,
    bool HasAlpha = false,
    string Codec = "");

internal sealed record AnalysisResult(
    Color BackgroundColor,
    Rectangle SubjectBounds,
    VideoInfo Video,
    string PreviewPath,
    double Confidence);

internal sealed record RenderProgress(double Percent, string Message);

internal sealed record MapAnalysis(
    Color BackgroundColor,
    VideoInfo Video,
    double Confidence);

internal enum QualityPreset
{
    Leve,
    Equilibrada,
    Alta
}

internal enum MapQualityPreset
{
    Compacta,
    Equilibrada,
    Alta
}
