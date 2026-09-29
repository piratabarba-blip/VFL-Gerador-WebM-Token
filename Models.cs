namespace VFL.GeradorWebMToken;

internal sealed record VideoInfo(int Width, int Height, double Duration, double FrameRate, bool HasAudio);

internal sealed record AnalysisResult(
    Color BackgroundColor,
    Rectangle SubjectBounds,
    VideoInfo Video,
    string PreviewPath,
    double Confidence);

internal sealed record RenderProgress(double Percent, string Message);

internal enum QualityPreset
{
    Leve,
    Equilibrada,
    Alta
}
