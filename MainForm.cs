using System.Diagnostics;

namespace VFL.GeradorWebMToken;

internal sealed class MainForm : Form
{
    private readonly TextBox _input = new();
    private readonly TextBox _output = new();
    private readonly ValueSlider _similarity = Slider(0.001m, 0.5m, 0.260m, 0.001m, 3);
    private readonly ValueSlider _blend = Slider(0, 0.3m, 0.30m, 0.01m, 2);
    private readonly ValueSlider _zoom = Slider(50, 180, 100, 5, 0);
    private readonly ValueSlider _positionX = Slider(-400, 400, 0, 10, 0);
    private readonly ValueSlider _positionY = Slider(-400, 400, 0, 10, 0);
    private readonly ComboBox _quality = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _keepAudio = new() { Text = "Manter áudio quando existir", Checked = true, AutoSize = true };
    private readonly CheckerPreview _preview = new() { Dock = DockStyle.Fill };
    private readonly Panel _colorSwatch = new() { Width = 32, Height = 24 };
    private readonly Label _detected = new() { AutoSize = true, ForeColor = Theme.Muted };
    private readonly Label _status = new() { Text = "Selecione um MP4 para começar.", AutoSize = true, ForeColor = Theme.Muted };
    private readonly TokenProgressBar _progress = new() { Dock = DockStyle.Fill };
    private readonly Button _render = new() { Text = "GERAR WEBM", Tag = "primary" };
    private readonly Button _clear = new() { Text = "LIMPAR / PRÓXIMO" };
    private readonly Button _cancel = new() { Text = "CANCELAR", Enabled = false };
    private readonly Button _selectVideo = new() { Text = "SELECIONAR VÍDEO", Tag = "primary" };
    private readonly Button _selectOutput = new() { Text = "..." };
    private readonly Label _videoTitle = new() { Text = "Nenhum vídeo carregado", AutoEllipsis = true, ForeColor = Theme.AccentCyan };
    private readonly Label _videoMeta = new() { Text = "Selecione um arquivo para começar", AutoEllipsis = true, ForeColor = Theme.Muted };
    private readonly Label _outputFolder = new() { Text = "Destino automático", AutoEllipsis = true, ForeColor = Theme.Muted };
    private readonly TrackBar _timeline = new()
    {
        Minimum = 0, Maximum = 1_000, Value = 0, TickStyle = TickStyle.None,
        Dock = DockStyle.Fill, Enabled = false, SmallChange = 1, LargeChange = 100
    };
    private readonly Button _play = new() { Text = "PLAY", Enabled = false };
    private readonly Button _pause = new() { Text = "PAUSA", Enabled = false };
    private readonly Button _stop = new() { Text = "STOP", Enabled = false };
    private readonly Label _previewTime = new()
    {
        Text = "00:00.000 / 00:00.000", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.Muted, Font = new Font("Segoe UI Semibold", 8f)
    };
    private readonly System.Windows.Forms.Timer _previewDebounce = new() { Interval = 280 };
    private AnalysisResult? _analysis;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _playbackCts;
    private double _previewPositionSeconds;
    private bool _isPlaying;

    public MainForm()
    {
        Text = "VFL Gerador WebM Token";
        Size = new Size(1440, 860);
        MinimumSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Background;
        Theme.ApplyIcon(this);
        BuildProfessionalUi();
        Theme.Apply(this);
        _quality.Items.AddRange(Enum.GetNames<QualityPreset>());
        LoadSettings();
        _similarity.ValueChanged += (_, _) => SettingChanged();
        _blend.ValueChanged += (_, _) => SettingChanged();
        _zoom.ValueChanged += (_, _) => SettingChanged();
        _positionX.ValueChanged += (_, _) => SettingChanged();
        _positionY.ValueChanged += (_, _) => SettingChanged();
        _quality.SelectedIndexChanged += (_, _) => SaveSettings();
        _keepAudio.CheckedChanged += (_, _) => SaveSettings();
        _selectVideo.Click += async (_, _) => await SelectVideoAsync();
        _selectOutput.Click += (_, _) => SelectOutput();
        _render.Click += async (_, _) => await RenderAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _previewDebounce.Tick += async (_, _) =>
        {
            _previewDebounce.Stop();
            await RefreshPreviewAsync();
        };
        _timeline.Scroll += (_, _) =>
        {
            PausePlayback();
            _previewPositionSeconds = TimelineToSeconds();
            UpdatePreviewTime();
            SchedulePreviewRefresh();
        };
        _play.Click += async (_, _) => await StartPlaybackAsync();
        _pause.Click += (_, _) => PausePlayback();
        _stop.Click += async (_, _) => await StopPlaybackAsync();
        _clear.Click += (_, _) => ResetVideoState(true);
        FormClosed += (_, _) =>
        {
            SaveSettings();
            _previewDebounce.Stop();
            _playbackCts?.Cancel();
            _previewCts?.Cancel();
            _previewCts?.Dispose();
        };
        Shown += (_, _) => FitToScreen();
    }

    private void BuildProfessionalUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Tag = "background", Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        var topbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(16, 10, 16, 9), Tag = "header", Margin = Padding.Empty };
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        topbar.Controls.Add(CreateBrand("VFL Gerador WebM Token", "VP9 TRANSPARENTE 1:1  •  v2.1"), 0, 0);
        _selectVideo.Dock = DockStyle.Fill; _selectVideo.Margin = new Padding(12, 5, 12, 5); topbar.Controls.Add(_selectVideo, 1, 0);

        var videoCard = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Tag = "card", Padding = new Padding(12, 4, 12, 4), Margin = new Padding(0, 2, 14, 2) };
        videoCard.RowStyles.Add(new RowStyle(SizeType.Percent, 54)); videoCard.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        _videoTitle.Dock = DockStyle.Fill; _videoTitle.TextAlign = ContentAlignment.BottomLeft; _videoTitle.Font = new Font("Segoe UI Semibold", 9); _videoTitle.Margin = Padding.Empty;
        _videoMeta.Dock = DockStyle.Fill; _videoMeta.TextAlign = ContentAlignment.TopLeft; _videoMeta.Font = new Font("Segoe UI", 7.8f); _videoMeta.Margin = Padding.Empty;
        videoCard.Controls.Add(_videoTitle, 0, 0); videoCard.Controls.Add(_videoMeta, 0, 1); topbar.Controls.Add(videoCard, 2, 0);

        var destination = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Tag = "header", Margin = Padding.Empty };
        destination.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); destination.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        var destinationCard = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Tag = "card", Padding = new Padding(10, 4, 8, 4), Margin = new Padding(0, 2, 6, 2) };
        destinationCard.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); destinationCard.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        destinationCard.Controls.Add(new Label { Text = "SALVAR WEBM EM", Dock = DockStyle.Fill, ForeColor = Theme.Dim, Font = new Font("Segoe UI Semibold", 7), TextAlign = ContentAlignment.BottomLeft, Margin = Padding.Empty }, 0, 0);
        _outputFolder.Dock = DockStyle.Fill; _outputFolder.Font = new Font("Consolas", 8); _outputFolder.Margin = Padding.Empty; destinationCard.Controls.Add(_outputFolder, 0, 1);
        destination.Controls.Add(destinationCard, 0, 0); _selectOutput.Dock = DockStyle.Fill; _selectOutput.Margin = new Padding(0, 5, 0, 5); destination.Controls.Add(_selectOutput, 1, 0);
        topbar.Controls.Add(destination, 3, 0); root.Controls.Add(topbar, 0, 0);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Tag = "background", Margin = Padding.Empty };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310));
        root.Controls.Add(main, 0, 1);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 10, Padding = new Padding(16, 14, 16, 14), Tag = "sidebar", Margin = Padding.Empty, AutoScroll = true, AutoScrollMinSize = new Size(0, 620) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        for (var index = 0; index < 5; index++) left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        left.Controls.Add(SectionTitle("CHROMA E ENQUADRAMENTO", "◆"), 0, 0);
        left.Controls.Add(new Label { Text = "Ajuste a remoção do fundo e posicione o personagem dentro do quadro 1:1.", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.3f) }, 0, 1);
        var detectedCard = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Tag = "card", Padding = new Padding(10, 7, 10, 7), Margin = Padding.Empty };
        detectedCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); detectedCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42)); detectedCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detectedCard.Controls.Add(new Label { Text = "FUNDO DETECTADO", Dock = DockStyle.Fill, ForeColor = Theme.Dim, Font = new Font("Segoe UI Semibold", 7.3f), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _colorSwatch.Dock = DockStyle.Fill; _colorSwatch.Margin = new Padding(4); detectedCard.Controls.Add(_colorSwatch, 1, 0);
        _detected.Dock = DockStyle.Fill; _detected.TextAlign = ContentAlignment.MiddleLeft; _detected.Font = new Font("Consolas", 8); detectedCard.Controls.Add(_detected, 2, 0); left.Controls.Add(detectedCard, 0, 2);
        left.Controls.Add(StackedNumber("TOLERÂNCIA", _similarity, "maior remove mais variações"), 0, 3);
        left.Controls.Add(StackedNumber("SUAVIZAÇÃO", _blend, "suaviza cabelos e contornos"), 0, 4);
        left.Controls.Add(StackedNumber("ZOOM", _zoom, "50% afasta  •  180% aproxima"), 0, 5);
        left.Controls.Add(StackedNumber("POSIÇÃO X", _positionX, "− esquerda  •  + direita"), 0, 6);
        left.Controls.Add(StackedNumber("POSIÇÃO Y", _positionY, "+ cima  •  − baixo"), 0, 7);
        left.Controls.Add(new Label { Text = "REFERÊNCIA FIXA\nA área quadriculada representa exatamente o WebM 1080×1080.", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.3f), Padding = new Padding(10), Tag = "card" }, 0, 8);
        main.Controls.Add(left, 0, 0);

        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Tag = "background", Margin = Padding.Empty };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        var previewHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(20, 10, 20, 6), Tag = "background" };
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        previewHeader.Controls.Add(new Label { Text = "PRÉ-VISUALIZAÇÃO TRANSPARENTE", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 10), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        previewHeader.Controls.Add(new Label { Text = "SAÍDA 1:1  •  VP9 ALPHA", Dock = DockStyle.Fill, ForeColor = Theme.AccentCyan, Font = new Font("Segoe UI Semibold", 8), TextAlign = ContentAlignment.MiddleRight }, 1, 0);
        workspace.Controls.Add(previewHeader, 0, 0); _preview.Margin = new Padding(18, 0, 18, 6); workspace.Controls.Add(_preview, 0, 1);
        workspace.Controls.Add(CreatePlaybackPanel(), 0, 2); main.Controls.Add(workspace, 1, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, Padding = new Padding(16, 14, 16, 14), Tag = "sidebar", Margin = Padding.Empty };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 138)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        right.Controls.Add(SectionTitle("EXPORTAÇÃO", "↗"), 0, 0);
        right.Controls.Add(StackedCombo("QUALIDADE DO WEBM", _quality), 0, 1);
        _keepAudio.Dock = DockStyle.Fill; _keepAudio.Padding = new Padding(8, 0, 0, 0); _keepAudio.Tag = "card"; right.Controls.Add(_keepAudio, 0, 2);
        var specs = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, Tag = "card", Padding = new Padding(12), Margin = new Padding(0, 8, 0, 8) };
        foreach (var text in new[] { "1080 × 1080 pixels", "24 quadros por segundo", "WebM VP9 transparente", "Miniatura PNG automática", "Processamento 100% local" })
            specs.Controls.Add(new Label { Text = "•  " + text, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.3f), TextAlign = ContentAlignment.MiddleLeft });
        right.Controls.Add(specs, 0, 3);
        right.Controls.Add(new Label { Text = "O arquivo original permanece intacto.\n\nAlguns reprodutores mostram verde; o Foundry interpreta corretamente o canal alfa.", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.4f), Padding = new Padding(4, 12, 4, 4) }, 0, 4);
        _render.Dock = DockStyle.Fill; _render.Tag = "success"; _render.Margin = new Padding(0, 4, 0, 2); _render.Enabled = false; right.Controls.Add(_render, 0, 5);
        _clear.Dock = DockStyle.Fill; _clear.Margin = new Padding(0, 6, 0, 0); right.Controls.Add(_clear, 0, 6); main.Controls.Add(right, 2, 0);

        var statusbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(16, 4, 16, 5), Tag = "header", Margin = Padding.Empty };
        statusbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); statusbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); statusbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); statusbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        statusbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); statusbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 5));
        statusbar.Controls.Add(new Label { Text = "●  PROCESSAMENTO LOCAL", Dock = DockStyle.Fill, ForeColor = Theme.AccentCyan, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 7.5f) }, 0, 0);
        _status.Dock = DockStyle.Fill; _status.TextAlign = ContentAlignment.MiddleLeft; statusbar.Controls.Add(_status, 1, 0);
        _cancel.Dock = DockStyle.Fill; _cancel.Margin = new Padding(0, 2, 10, 2); statusbar.Controls.Add(_cancel, 2, 0);
        statusbar.Controls.Add(new Label { Text = "ORIGINAL PRESERVADO", Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI Semibold", 8) }, 3, 0);
        statusbar.Controls.Add(_progress, 0, 1); statusbar.SetColumnSpan(_progress, 4); root.Controls.Add(statusbar, 0, 2);

        void SyncOutput()
        {
            _outputFolder.Text = string.IsNullOrWhiteSpace(_output.Text) ? "Destino automático" : _output.Text;
        }
        _output.TextChanged += (_, _) => SyncOutput(); SyncOutput();
    }

    private Control CreateBrand(string title, string badge)
    {
        var brand = new Panel { Dock = DockStyle.Fill, Tag = "header", Margin = Padding.Empty };
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "vfl-suite-logo.png");
        Control mark;
        if (File.Exists(logoPath))
            mark = new PictureBox { Image = Image.FromFile(logoPath), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 5), Size = new Size(44, 44) };
        else
            mark = new Label { Text = "V", Location = new Point(0, 5), Size = new Size(44, 44), BackColor = Theme.Primary, Tag = "accent", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Black", 15) };
        brand.Controls.Add(mark);
        brand.Controls.Add(new Label { Text = title, Location = new Point(50, 3), Size = new Size(235, 25), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Semibold", 11.5f), Margin = Padding.Empty });
        brand.Controls.Add(new Label { Text = badge, Location = new Point(50, 29), Size = new Size(235, 16), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right, Tag = "brand-badge", TextAlign = ContentAlignment.TopLeft, Font = new Font("Segoe UI Semibold", 6.8f), Margin = Padding.Empty });
        return brand;
    }

    private Control CreatePlaybackPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2, Padding = new Padding(24, 6, 24, 8), Tag = "header", Margin = Padding.Empty };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        _timeline.Margin = new Padding(0, 3, 10, 0); panel.Controls.Add(_timeline, 0, 0); panel.Controls.Add(_previewTime, 1, 0);
        var controls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Tag = "header", Margin = Padding.Empty };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var item in new[] { (_play, 1), (_pause, 2), (_stop, 3) }) { item.Item1.Dock = DockStyle.Fill; item.Item1.Margin = new Padding(4); controls.Controls.Add(item.Item1, item.Item2, 0); }
        panel.Controls.Add(controls, 0, 1); panel.SetColumnSpan(controls, 2); return panel;
    }

    private static Label SectionTitle(string text, string icon) => new()
    {
        Text = $"{icon}  {text}", Dock = DockStyle.Fill, ForeColor = Theme.Text,
        Font = new Font("Segoe UI Semibold", 9), TextAlign = ContentAlignment.MiddleLeft
    };

    private static Control StackedNumber(string title, ValueSlider slider, string hint)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Tag = "sidebar", Margin = Padding.Empty };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 15));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.3f), TextAlign = ContentAlignment.BottomLeft }, 0, 0);
        row.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, ForeColor = Theme.Dim, Font = new Font("Segoe UI", 7.3f), TextAlign = ContentAlignment.TopLeft }, 0, 1);
        slider.Dock = DockStyle.Fill; slider.Margin = new Padding(0, 1, 0, 1); row.Controls.Add(slider, 0, 2);
        return row;
    }

    private static Control StackedCombo(string title, ComboBox combo)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Tag = "sidebar", Margin = Padding.Empty };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.3f), TextAlign = ContentAlignment.BottomLeft }, 0, 0);
        combo.Dock = DockStyle.Fill; combo.Margin = new Padding(0, 6, 0, 4); row.Controls.Add(combo, 0, 1); return row;
    }

    private async Task SelectVideoAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Vídeos MP4|*.mp4|Vídeos|*.mp4;*.mov;*.mkv;*.webm", Title = "Selecione o vídeo com fundo sólido" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ResetVideoState(false); _input.Text = dialog.FileName;
        _output.Text = GetAvailableOutputPath(Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileNameWithoutExtension(dialog.FileName) + "_TOKEN.webm"));
        _videoTitle.Text = Path.GetFileName(dialog.FileName); _videoMeta.Text = "Detectando o fundo e preparando a prévia...";
        await AnalyzeAsync();
    }

    private void SelectOutput()
    {
        using var dialog = new SaveFileDialog { Filter = "WebM transparente|*.webm", DefaultExt = "webm", AddExtension = true, Title = "Salvar token WebM em", FileName = string.IsNullOrWhiteSpace(_output.Text) ? "token.webm" : Path.GetFileName(_output.Text), InitialDirectory = string.IsNullOrWhiteSpace(_output.Text) ? null : Path.GetDirectoryName(_output.Text) };
        if (dialog.ShowDialog(this) == DialogResult.OK) _output.Text = dialog.FileName;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Tag = "background" };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(28, 14, 28, 12), Tag = "header" };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        header.Controls.Add(new Label { Text = "VFL", Dock = DockStyle.Fill, BackColor = Theme.Primary, Tag = "accent", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Black", 16) }, 0, 0);
        var title = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(18, 0, 0, 0), Tag = "header" };
        title.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        title.Controls.Add(new Label
        {
            Text = "VFL Gerador WebM Token",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 19),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty
        }, 0, 0);
        title.Controls.Add(new Label
        {
            Text = "Transforme vídeos com fundo sólido em tokens VP9 transparentes.",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty
        }, 0, 1);
        header.Controls.Add(title, 1, 0);
        header.Controls.Add(new Label { Text = "PROCESSAMENTO LOCAL  |  v2.0", Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 8.5f) }, 2, 0);
        root.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(28, 20, 28, 8), Tag = "background" };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.Controls.Add(content, 0, 1);

        var settings = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 12, Padding = new Padding(22, 16, 22, 16),
            Tag = "rounded-surface", Margin = new Padding(0, 0, 12, 0),
            AutoScroll = true, AutoScrollMinSize = new Size(0, 540)
        };
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settings.Controls.Add(new Label { Text = "CONFIGURAÇÃO AUTOMÁTICA", ForeColor = Theme.Primary, Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 9) }, 0, 0);
        settings.Controls.Add(FileRow("Vídeo MP4 de entrada", _input, false), 0, 1);
        settings.Controls.Add(FileRow("Salvar WebM em", _output, true), 0, 2);
        var detectedRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Tag = "surface", WrapContents = false };
        detectedRow.Controls.Add(new Label { Text = "FUNDO DETECTADO", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 10, 0) });
        detectedRow.Controls.Add(_colorSwatch); _colorSwatch.Margin = new Padding(0, 2, 10, 0);
        detectedRow.Controls.Add(_detected); _detected.Margin = new Padding(0, 6, 0, 0);
        settings.Controls.Add(detectedRow, 0, 3);
        settings.Controls.Add(SettingRow("Tolerância", _similarity, "Maior remove mais variações"), 0, 4);
        settings.Controls.Add(SettingRow("Suavização", _blend, "Suaviza cabelo e contornos"), 0, 5);
        settings.Controls.Add(SettingRow("Zoom", _zoom, "50% afasta  •  180% aproxima"), 0, 6);
        settings.Controls.Add(SettingRow("Posição X", _positionX, "− esquerda  •  + direita"), 0, 7);
        settings.Controls.Add(SettingRow("Posição Y", _positionY, "+ cima  •  − baixo"), 0, 8);
        var qualityRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Tag = "surface" };
        qualityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); qualityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        qualityRow.Controls.Add(new Label { Text = "QUALIDADE", Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _quality.Dock = DockStyle.Fill; _quality.Margin = new Padding(0, 7, 0, 7); qualityRow.Controls.Add(_quality, 1, 0);
        settings.Controls.Add(qualityRow, 0, 9);
        _keepAudio.Dock = DockStyle.Fill; settings.Controls.Add(_keepAudio, 0, 10);
        settings.Controls.Add(new Label { Text = "Prévia automática • Saída 1080×1080 • 24 FPS • VP9 transparente\nZoom 100% e posições X/Y 0 mantêm o enquadramento automático.", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5f) }, 0, 11);
        content.Controls.Add(settings, 0, 0);

        var previewCard = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(16), Tag = "rounded-surface", Margin = new Padding(12, 0, 0, 0) };
        previewCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        previewCard.Controls.Add(new Label { Text = "PRÉ-VISUALIZAÇÃO TRANSPARENTE", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI Semibold", 8.5f) }, 0, 0);
        previewCard.Controls.Add(_preview, 0, 1);

        var playbackHost = new Panel { Dock = DockStyle.Fill, Tag = "surface", Margin = new Padding(0, 6, 0, 0) };
        var playback = new TableLayoutPanel { RowCount = 2, ColumnCount = 2, Tag = "surface", Margin = Padding.Empty };
        playback.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        playback.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _timeline.Margin = new Padding(0, 2, 8, 0); playback.Controls.Add(_timeline, 0, 0);
        playback.Controls.Add(_previewTime, 1, 0);
        var buttonCenter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Tag = "surface", Margin = Padding.Empty };
        buttonCenter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttonCenter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 312));
        buttonCenter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var playbackButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, Tag = "surface", WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = Padding.Empty, Margin = Padding.Empty };
        foreach (var button in new[] { _play, _pause, _stop })
        {
            button.Width = 96; button.Height = 34; button.Margin = new Padding(0, 2, 8, 0);
            playbackButtons.Controls.Add(button);
        }
        buttonCenter.Controls.Add(playbackButtons, 1, 0);
        playback.Controls.Add(buttonCenter, 0, 1); playback.SetColumnSpan(buttonCenter, 2);
        playbackHost.Controls.Add(playback);
        void CenterPlayback()
        {
            var width = Math.Max(360, Math.Min(500, Math.Min(_preview.ClientSize.Width, _preview.ClientSize.Height)));
            playback.Size = new Size(Math.Min(width, playbackHost.ClientSize.Width), 76);
            playback.Location = new Point(Math.Max(0, (playbackHost.ClientSize.Width - playback.Width) / 2), 0);
        }
        playbackHost.SizeChanged += (_, _) => CenterPlayback();
        _preview.SizeChanged += (_, _) => CenterPlayback();
        previewCard.Controls.Add(playbackHost, 0, 2);
        content.Controls.Add(previewCard, 1, 0);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Padding = new Padding(28, 10, 28, 16), Tag = "background" };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        _render.Dock = DockStyle.Fill; _render.Margin = new Padding(0, 2, 12, 2); _render.Click += async (_, _) => await RenderAsync(); footer.Controls.Add(_render, 0, 0);
        _clear.Dock = DockStyle.Fill; _clear.Margin = new Padding(0, 2, 12, 2); footer.Controls.Add(_clear, 1, 0);
        _cancel.Dock = DockStyle.Fill; _cancel.Margin = new Padding(0, 2, 12, 2); _cancel.Click += (_, _) => _cts?.Cancel(); footer.Controls.Add(_cancel, 2, 0);
        _status.Dock = DockStyle.Fill; _status.TextAlign = ContentAlignment.MiddleLeft; footer.Controls.Add(_status, 3, 0);
        footer.Controls.Add(new Label { Text = "ORIGINAL PRESERVADO", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted, Font = new Font("Segoe UI Semibold", 8) }, 4, 0);
        footer.Controls.Add(_progress, 0, 1); footer.SetColumnSpan(_progress, 5); root.Controls.Add(footer, 0, 2);
    }

    private Control FileRow(string label, TextBox box, bool save)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2, Tag = "surface" };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); row.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        row.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, ForeColor = Theme.Muted }, 0, 0);
        box.Dock = DockStyle.Fill; box.Margin = new Padding(0, 4, 10, 2); row.Controls.Add(box, 0, 1);
        var browse = new Button { Text = "PROCURAR", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 2) };
        browse.Click += async (_, _) =>
        {
            if (save)
            {
                using var dialog = new SaveFileDialog { Filter = "WebM transparente|*.webm", DefaultExt = "webm", AddExtension = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) _output.Text = dialog.FileName;
            }
            else
            {
                using var dialog = new OpenFileDialog { Filter = "Vídeos MP4|*.mp4|Vídeos|*.mp4;*.mov;*.mkv;*.webm" };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ResetVideoState(false);
                _input.Text = dialog.FileName;
                var suggested = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileNameWithoutExtension(dialog.FileName) + "_TOKEN.webm");
                _output.Text = GetAvailableOutputPath(suggested);
                await AnalyzeAsync();
            }
        };
        row.Controls.Add(browse, 1, 1);
        return row;
    }

    private static Control SettingRow(string label, ValueSlider slider, string hint)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Tag = "surface" };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        row.Controls.Add(new Label { Text = label.ToUpperInvariant(), Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        slider.Dock = DockStyle.Fill; slider.Margin = new Padding(0, 8, 10, 8); row.Controls.Add(slider, 1, 0);
        row.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8) }, 2, 0);
        return row;
    }

    private async Task AnalyzeAsync()
    {
        if (!File.Exists(_input.Text)) { TokenDialog.ShowMessage(this, "Vídeo necessário", "Selecione um vídeo válido."); return; }
        PausePlayback();
        _cts = new CancellationTokenSource(); SetBusy(true);
        try
        {
            var analyzer = new VideoAnalyzer();
            var messages = new Progress<string>(message => _status.Text = message);
            _analysis = await analyzer.AnalyzeAsync(_input.Text, (double)_similarity.Value, (double)_blend.Value,
                (double)_zoom.Value, (int)_positionX.Value, (int)_positionY.Value, messages, _cts.Token);
            _colorSwatch.BackColor = _analysis.BackgroundColor;
            _detected.Text = $"#{_analysis.BackgroundColor.R:X2}{_analysis.BackgroundColor.G:X2}{_analysis.BackgroundColor.B:X2}  •  confiança {_analysis.Confidence:P0}";
            _preview.PreviewImage = LoadImage(_analysis.PreviewPath);
            try { File.Delete(_analysis.PreviewPath); } catch { }
            ConfigureTimeline();
            SetPreviewPosition(0);
            SetPlaybackControlsEnabled(true);
            _videoTitle.Text = Path.GetFileName(_input.Text);
            _videoMeta.Text = $"{FormatTime(_analysis.Video.Duration)}  •  fundo {_detected.Text}";
            _status.Text = $"Prévia pronta • Zoom {_zoom.Value:0}% • X {_positionX.Value:+0;-0;0} • Y {_positionY.Value:+0;-0;0}";
        }
        catch (OperationCanceledException) { _status.Text = "Análise cancelada."; _videoMeta.Text = "Análise cancelada"; }
        catch (Exception exception) { _status.Text = "Falha na análise."; _videoMeta.Text = "Não foi possível analisar este arquivo"; TokenDialog.ShowMessage(this, "Erro na análise", exception.Message); }
        finally { SetBusy(false); _cts.Dispose(); _cts = null; }
    }

    private async Task RenderAsync()
    {
        if (_analysis is null || !File.Exists(_input.Text)) { TokenDialog.ShowMessage(this, "Análise necessária", "Analise o vídeo antes de gerar."); return; }
        if (string.IsNullOrWhiteSpace(_output.Text)) { TokenDialog.ShowMessage(this, "Destino necessário", "Escolha o arquivo de saída."); return; }
        if (File.Exists(_output.Text))
        {
            _output.Text = GetAvailableOutputPath(_output.Text);
            _status.Text = "Novo nome criado para evitar o cache do Foundry.";
        }
        PausePlayback();
        _cts = new CancellationTokenSource(); SetBusy(true);
        try
        {
            var preset = Enum.Parse<QualityPreset>(_quality.SelectedItem?.ToString() ?? QualityPreset.Alta.ToString());
            var progress = new Progress<RenderProgress>(item => { _progress.Value = (int)item.Percent; _status.Text = item.Message; });
            await new WebmRenderer().RenderAsync(_input.Text, _output.Text, _analysis,
                (double)_similarity.Value, (double)_blend.Value, (double)_zoom.Value,
                (int)_positionX.Value, (int)_positionY.Value,
                preset, _keepAudio.Checked, progress, _cts.Token);
            var thumbnailPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(_output.Text)) ?? "",
                Path.GetFileNameWithoutExtension(_output.Text) + "_PREVIA.png");
            var thumbnailCreated = _preview.SaveThumbnail(thumbnailPath);
            var completedMessage = thumbnailCreated
                ? $"WebM transparente e miniatura concluídos!\n\nMiniatura: {Path.GetFileName(thumbnailPath)}\n\nDeseja abrir a pasta?"
                : "WebM transparente concluído!\n\nDeseja abrir a pasta?";
            if (TokenDialog.Confirm(this, "WebM concluído", completedMessage))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_output.Text}\"") { UseShellExecute = true });
        }
        catch (OperationCanceledException) { _status.Text = "Conversão cancelada."; }
        catch (Exception exception) { _status.Text = "Falha na conversão."; TokenDialog.ShowMessage(this, "Erro na conversão", exception.Message); }
        finally { SetBusy(false); _cts.Dispose(); _cts = null; }
    }

    private void SetBusy(bool busy)
    {
        _render.Enabled = !busy && _analysis is not null; _clear.Enabled = !busy; _cancel.Enabled = busy;
        _selectVideo.Enabled = !busy; _selectOutput.Enabled = !busy;
        _similarity.Enabled = !busy; _blend.Enabled = !busy; _zoom.Enabled = !busy;
        _positionX.Enabled = !busy; _positionY.Enabled = !busy; _quality.Enabled = !busy; _keepAudio.Enabled = !busy;
        _timeline.Enabled = !busy && _analysis is not null;
        if (busy)
        {
            PausePlayback();
            _play.Enabled = false; _pause.Enabled = false; _stop.Enabled = false;
        }
        else SetPlaybackControlsEnabled(_analysis is not null);
    }

    private void SchedulePreviewRefresh()
    {
        if (_analysis is null || _cts is not null) return;
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private void SettingChanged()
    {
        SaveSettings();
        PausePlayback();
        if (_analysis is not null) _videoMeta.Text = "Enquadramento alterado • atualizando prévia";
        SchedulePreviewRefresh();
    }

    private void ResetVideoState(bool clearPaths)
    {
        PausePlayback();
        _previewDebounce.Stop();
        _previewCts?.Cancel();
        _analysis = null;
        _previewPositionSeconds = 0;
        _preview.PreviewImage = null;
        _colorSwatch.BackColor = Theme.Field;
        _detected.Text = "";
        _timeline.Value = 0;
        _timeline.Maximum = 1_000;
        _progress.Value = 0;
        if (clearPaths)
        {
            _input.Clear();
            _output.Clear();
            _videoTitle.Text = "Nenhum vídeo carregado";
            _videoMeta.Text = "Selecione um arquivo para começar";
        }
        UpdatePreviewTime();
        SetPlaybackControlsEnabled(false);
        _status.Text = "Pronto para o próximo vídeo. Seus ajustes foram mantidos.";
    }

    private void LoadSettings()
    {
        var settings = AppSettings.Load();
        _similarity.Value = Clamp(settings.Similarity, _similarity.Minimum, _similarity.Maximum);
        _blend.Value = Clamp(settings.Blend, _blend.Minimum, _blend.Maximum);
        _zoom.Value = Clamp(settings.Zoom, _zoom.Minimum, _zoom.Maximum);
        _positionX.Value = Clamp(settings.PositionX, _positionX.Minimum, _positionX.Maximum);
        _positionY.Value = Clamp(settings.PositionY, _positionY.Minimum, _positionY.Maximum);
        _quality.SelectedItem = Enum.TryParse<QualityPreset>(settings.Quality, out var quality)
            ? quality.ToString()
            : QualityPreset.Alta.ToString();
        _keepAudio.Checked = settings.KeepAudio;
    }

    private void SaveSettings()
    {
        new AppSettings
        {
            Similarity = _similarity.Value,
            Blend = _blend.Value,
            Zoom = _zoom.Value,
            PositionX = _positionX.Value,
            PositionY = _positionY.Value,
            Quality = _quality.SelectedItem?.ToString() ?? QualityPreset.Alta.ToString(),
            KeepAudio = _keepAudio.Checked
        }.Save();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_analysis is null || _cts is not null || !File.Exists(_input.Text)) return;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        var previewCts = new CancellationTokenSource();
        _previewCts = previewCts;
        var token = previewCts.Token;
        string? previewPath = null;
        try
        {
            _status.Text = "Atualizando prévia...";
            previewPath = await new VideoAnalyzer().CreatePreviewAsync(_input.Text, _analysis,
                (double)_similarity.Value, (double)_blend.Value, (double)_zoom.Value,
                (int)_positionX.Value, (int)_positionY.Value, _previewPositionSeconds, token);
            if (token.IsCancellationRequested) return;
            _preview.PreviewImage = LoadImage(previewPath);
            _status.Text = $"Prévia {FormatTime(_previewPositionSeconds)} • Zoom {_zoom.Value:0}% • X {_positionX.Value:+0;-0;0} • Y {_positionY.Value:+0;-0;0}";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _status.Text = "Não foi possível atualizar a prévia: " + exception.Message.Split('\n')[0];
        }
        finally
        {
            if (previewPath is not null) try { File.Delete(previewPath); } catch { }
            if (ReferenceEquals(_previewCts, previewCts))
            {
                _previewCts.Dispose();
                _previewCts = null;
            }
        }
    }

    private async Task StartPlaybackAsync()
    {
        if (_analysis is null || _cts is not null) return;
        if (_previewPositionSeconds >= _analysis.Video.Duration - 0.05) SetPreviewPosition(0);

        var previousPlayback = _playbackCts;
        PausePlayback();
        if (previousPlayback is not null && ReferenceEquals(_playbackCts, previousPlayback))
        {
            _playbackCts = null;
            previousPlayback.Dispose();
        }
        _previewDebounce.Stop();
        _previewCts?.Cancel();
        var playbackCts = new CancellationTokenSource();
        _playbackCts = playbackCts;
        var token = playbackCts.Token;
        var startPosition = _previewPositionSeconds;
        _isPlaying = true;
        _play.Enabled = false; _pause.Enabled = true; _stop.Enabled = true;
        _status.Text = "Reproduzindo prévia fluida a 24 FPS...";
        try
        {
            await new VideoAnalyzer().StreamPreviewAsync(_input.Text, _analysis,
                (double)_similarity.Value, (double)_blend.Value, (double)_zoom.Value,
                (int)_positionX.Value, (int)_positionY.Value, startPosition,
                async (bitmap, seconds) =>
                {
                    if (token.IsCancellationRequested) { bitmap.Dispose(); return; }
                    var updated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        BeginInvoke(() =>
                        {
                            if (token.IsCancellationRequested)
                            {
                                bitmap.Dispose();
                                updated.TrySetResult(false);
                            }
                            else
                            {
                                _preview.PreviewImage = bitmap;
                                SetPreviewPosition(seconds);
                                updated.TrySetResult(true);
                            }
                        });
                        await updated.Task;
                    }
                    catch
                    {
                        bitmap.Dispose();
                        throw;
                    }
                }, token);

            if (!token.IsCancellationRequested)
            {
                SetPreviewPosition(_analysis.Video.Duration);
                _status.Text = "Fim da prévia — enquadramento conferido até o último quadro.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _status.Text = "Não foi possível reproduzir a prévia: " + exception.Message.Split('\n')[0];
        }
        finally
        {
            if (ReferenceEquals(_playbackCts, playbackCts))
            {
                _isPlaying = false;
                _playbackCts.Dispose();
                _playbackCts = null;
                SetPlaybackControlsEnabled(_analysis is not null && _cts is null);
            }
        }
    }

    private void PausePlayback()
    {
        _isPlaying = false;
        _playbackCts?.Cancel();
        SetPlaybackControlsEnabled(_analysis is not null && _cts is null);
    }

    private async Task StopPlaybackAsync()
    {
        PausePlayback();
        if (_analysis is null) return;
        SetPreviewPosition(0);
        await RefreshPreviewAsync();
    }

    private void SetPreviewPosition(double seconds)
    {
        if (_analysis is null) return;
        _previewPositionSeconds = Math.Clamp(seconds, 0, _analysis.Video.Duration);
        _timeline.Value = _analysis.Video.Duration <= 0
            ? 0
            : Math.Clamp((int)Math.Round(_previewPositionSeconds / _analysis.Video.Duration * _timeline.Maximum), 0, _timeline.Maximum);
        UpdatePreviewTime();
    }

    private double TimelineToSeconds() => _analysis is null || _timeline.Maximum == 0
        ? 0
        : _timeline.Value / (double)_timeline.Maximum * _analysis.Video.Duration;

    private void ConfigureTimeline()
    {
        if (_analysis is null) return;
        _timeline.Maximum = Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(_analysis.Video.Duration * 1000)));
        _timeline.SmallChange = 1;
        _timeline.LargeChange = 100;
    }

    private void UpdatePreviewTime()
    {
        var duration = _analysis?.Video.Duration ?? 0;
        _previewTime.Text = $"{FormatTime(_previewPositionSeconds)} / {FormatTime(duration)}";
    }

    private void SetPlaybackControlsEnabled(bool enabled)
    {
        _timeline.Enabled = enabled;
        _play.Enabled = enabled && !_isPlaying;
        _pause.Enabled = enabled && _isPlaying;
        _stop.Enabled = enabled;
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss\.fff") : time.ToString(@"mm\:ss\.fff");
    }

    private static Image LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static string GetAvailableOutputPath(string desiredPath)
    {
        if (!File.Exists(desiredPath)) return desiredPath;
        var directory = Path.GetDirectoryName(desiredPath) ?? "";
        var extension = Path.GetExtension(desiredPath);
        var baseName = Path.GetFileNameWithoutExtension(desiredPath);
        var rootName = baseName;
        var suffix = 2;
        var separator = baseName.LastIndexOf('_');
        if (separator > 0 && int.TryParse(baseName[(separator + 1)..], out var currentSuffix))
        {
            rootName = baseName[..separator];
            suffix = currentSuffix + 1;
        }
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{rootName}_{suffix}{extension}");
            suffix++;
        } while (File.Exists(candidate));
        return candidate;
    }

    private void FitToScreen()
    {
        var area = Screen.FromControl(this).WorkingArea;
        Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    private static ValueSlider Slider(decimal min, decimal max, decimal value, decimal increment, int decimalPlaces) =>
        new() { Minimum = min, Maximum = max, Value = value, Increment = increment, DecimalPlaces = decimalPlaces, Tag = "surface" };

    private static decimal Clamp(decimal value, decimal minimum, decimal maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));
}
