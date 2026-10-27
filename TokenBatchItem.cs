namespace VFL.GeradorWebMToken;

internal enum TokenBatchStatus
{
    Aguardando,
    Analisando,
    Pronto,
    Processando,
    Concluido,
    Erro
}

internal sealed class TokenBatchItem
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; set; }
    public AnalysisResult? Analysis { get; set; }
    public decimal Similarity { get; set; } = 0.260m;
    public decimal Blend { get; set; } = 0.30m;
    public decimal Zoom { get; set; } = 100m;
    public decimal PositionX { get; set; }
    public decimal PositionY { get; set; }
    public decimal Rotation { get; set; }
    public QualityPreset Quality { get; set; } = QualityPreset.Alta;
    public bool KeepAudio { get; set; } = true;
    public TokenBatchStatus Status { get; set; } = TokenBatchStatus.Aguardando;
    public string Error { get; set; } = "";
    public ListViewItem? Row { get; set; }

    public string DisplayStatus => Status switch
    {
        TokenBatchStatus.Aguardando => "Aguardando",
        TokenBatchStatus.Analisando => "Analisando...",
        TokenBatchStatus.Pronto => "Pronto",
        TokenBatchStatus.Processando => "Gerando...",
        TokenBatchStatus.Concluido => "Concluído",
        TokenBatchStatus.Erro => "Erro",
        _ => Status.ToString()
    };
}
