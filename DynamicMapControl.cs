using System.Diagnostics;

namespace VFL.GeradorWebMToken;

internal sealed class DynamicMapControl : UserControl
{
    private readonly Button _selectVideo = new() { Text = "SELECIONAR VÍDEO", Tag = "primary" };
    private readonly Button _selectOutput = new() { Text = "..." };
    private readonly Button _render = new() { Text = "GERAR MAPA WEBM", Tag = "success", Enabled = false };
    private readonly Button _clear = new() { Text = "LIMPAR / PRÓXIMO" };
    private readonly Button _cancel = new() { Text = "CANCELAR", Enabled = false };
    private readonly Button _chooseColor = new() { Text = "ESCOLHER COR" };
    private readonly Button _play = new() { Text = "PLAY", Enabled = false };
    private readonly Button _pause = new() { Text = "PAUSA", Enabled = false };
    private readonly Button _stop = new() { Text = "STOP", Enabled = false };
    private readonly Button _testLoop = new() { Text = "TESTAR LOOP", Enabled = false };
    private readonly CheckBox _removeBackground = new() { Text = "Remover fundo sólido e criar transparência", AutoSize = true };
    private readonly CheckBox _keepAudio = new() { Text = "Manter áudio quando existir", AutoSize = true };
    private readonly CheckBox _seamlessLoop = new() { Text = "Criar loop contínuo sem salto", AutoSize = true };
    private readonly CheckBox _stabilize = new() { Text = "Travar câmera no cenário (tripé virtual)", AutoSize = true };
    private readonly ValueSlider _similarity = Slider(0.001m, 0.5m, 0.260m, 0.001m, 3);
    private readonly ValueSlider _blend = Slider(0, 0.3m, 0.30m, 0.01m, 2);
    private readonly ValueSlider _loopDuration = Slider(0.25m, 3.00m, 1.00m, 0.25m, 2);
    private readonly ComboBox _quality = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _frameRate = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly AspectPreview _preview = new() { Dock = DockStyle.Fill };
    private readonly Panel _colorSwatch = new() { Width = 38, Height = 28, BackColor = Theme.Field };
    private readonly Label _detected = new() { ForeColor = Theme.Muted, AutoEllipsis = true };
    private readonly Label _videoTitle = new() { Text = "Nenhum vídeo carregado", AutoEllipsis = true, ForeColor = Theme.AccentCyan };
    private readonly Label _videoMeta = new() { Text = "Selecione um arquivo para começar", AutoEllipsis = true, ForeColor = Theme.Muted };
    private readonly Label _outputFolder = new() { Text = "Destino automático", AutoEllipsis = true, ForeColor = Theme.Muted };
    private readonly Label _status = new() { Text = "Selecione um vídeo para criar um mapa dinâmico.", ForeColor = Theme.Muted };
    private readonly Label _previewTime = new()
    {
        Text = "00:00.000 / 00:00.000", TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.Muted, Font = new Font("Segoe UI Semibold", 8f)
    };
    private readonly Label _previewMode = new()
    {
        Text = "PROPORÇÃO ORIGINAL  •  VP9", Dock = DockStyle.Fill, ForeColor = Theme.AccentCyan,
        Font = new Font("Segoe UI Semibold", 8), TextAlign = ContentAlignment.MiddleRight
    };
    private readonly TrackBar _timeline = new()
    {
        Minimum = 0, Maximum = 1_000, TickStyle = TickStyle.None, Dock = DockStyle.Fill,
        Enabled = false, SmallChange = 1, LargeChange = 100
    };
    private readonly TokenProgressBar _progress = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _previewDebounce = new() { Interval = 280 };

    private MapAnalysis? _analysis;
    private string _inputPath = "";
    private string _outputPath = "";
    private double _previewPositionSeconds;
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _playbackCts;
    private bool _isPlaying;

    public DynamicMapControl()
    {
        BackColor = Theme.Background;
        Font = new Font("Segoe UI", 10);
        BuildUi();
        _quality.Items.AddRange(Enum.GetNames<MapQualityPreset>());
        _frameRate.Items.AddRange(["Original", "30 FPS", "24 FPS", "15 FPS"]);
        LoadSettings();
        WireEvents();
        UpdateChromaControls();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Tag = "background", Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        var topbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(16, 10, 16, 9), Tag = "header", Margin = Padding.Empty };
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        topbar.Controls.Add(CreateBrand(), 0, 0);
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
        destinationCard.Controls.Add(new Label { Text = "SALVAR MAPA WEBM EM", Dock = DockStyle.Fill, ForeColor = Theme.Dim, Font = new Font("Segoe UI Semibold", 7), TextAlign = ContentAlignment.BottomLeft, Margin = Padding.Empty }, 0, 0);
        _outputFolder.Dock = DockStyle.Fill; _outputFolder.Font = new Font("Consolas", 8); _outputFolder.Margin = Padding.Empty; destinationCard.Controls.Add(_outputFolder, 0, 1);
        destination.Controls.Add(destinationCard, 0, 0); _selectOutput.Dock = DockStyle.Fill; _selectOutput.Margin = new Padding(0, 5, 0, 5); destination.Controls.Add(_selectOutput, 1, 0);
        topbar.Controls.Add(destination, 3, 0); root.Controls.Add(topbar, 0, 0);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Tag = "background", Margin = Padding.Empty };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310));
        root.Controls.Add(main, 0, 1);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 8, Padding = new Padding(16, 14, 16, 14), Tag = "sidebar", Margin = Padding.Empty, AutoScroll = true, AutoScrollMinSize = new Size(0, 560) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Controls.Add(SectionTitle("CHROMA OPCIONAL", "◆"), 0, 0);
        left.Controls.Add(new Label { Text = "Use o vídeo inteiro. Ative o chroma somente quando houver fundo verde, azul ou outra cor sólida.", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.3f) }, 0, 1);
        _removeBackground.Dock = DockStyle.Fill; _removeBackground.Padding = new Padding(8, 0, 0, 0); _removeBackground.Tag = "card"; left.Controls.Add(_removeBackground, 0, 2);

        var detectedCard = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Tag = "card", Padding = new Padding(10, 7, 10, 7), Margin = Padding.Empty };
        detectedCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44)); detectedCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); detectedCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        _colorSwatch.Dock = DockStyle.Fill; _colorSwatch.Margin = new Padding(3); detectedCard.Controls.Add(_colorSwatch, 0, 0);
        _detected.Dock = DockStyle.Fill; _detected.TextAlign = ContentAlignment.MiddleLeft; _detected.Font = new Font("Consolas", 7.8f); detectedCard.Controls.Add(_detected, 1, 0);
        _chooseColor.Dock = DockStyle.Fill; _chooseColor.Margin = new Padding(4, 2, 0, 2); _chooseColor.Font = new Font("Segoe UI Semibold", 7); detectedCard.Controls.Add(_chooseColor, 2, 0);
        left.Controls.Add(detectedCard, 0, 3);
        left.Controls.Add(SliderRow("TOLERÂNCIA", _similarity, "maior remove mais variações"), 0, 4);
        left.Controls.Add(SliderRow("SUAVIZAÇÃO", _blend, "suaviza contornos e transparência"), 0, 5);
        left.Controls.Add(new Label { Text = "COR AUTOMÁTICA OU MANUAL", Dock = DockStyle.Fill, ForeColor = Theme.Dim, Font = new Font("Segoe UI Semibold", 7.3f), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), Tag = "card" }, 0, 6);
        left.Controls.Add(new Label { Text = "PROPORÇÃO PRESERVADA\nA saída mantém toda a largura e altura originais. Nenhum corte ou enquadramento 1:1 é aplicado.", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.3f), Padding = new Padding(10), Tag = "card" }, 0, 7);
        main.Controls.Add(left, 0, 0);

        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Tag = "background", Margin = Padding.Empty };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        var previewHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(20, 10, 20, 6), Tag = "background" };
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        previewHeader.Controls.Add(new Label { Text = "PRÉ-VISUALIZAÇÃO DO MAPA", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 10), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        previewHeader.Controls.Add(_previewMode, 1, 0); workspace.Controls.Add(previewHeader, 0, 0);
        _preview.Margin = new Padding(18, 0, 18, 6); workspace.Controls.Add(_preview, 0, 1);
        workspace.Controls.Add(CreatePlaybackPanel(), 0, 2); main.Controls.Add(workspace, 1, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 11, Padding = new Padding(16, 14, 16, 14), Tag = "sidebar", Margin = Padding.Empty, AutoScroll = true };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        right.Controls.Add(SectionTitle("OTIMIZAÇÃO PARA FOUNDRY", "↗"), 0, 0);
        right.Controls.Add(ComboCard("QUALIDADE", _quality), 0, 1);
        right.Controls.Add(ComboCard("QUADROS POR SEGUNDO", _frameRate), 0, 2);
        _keepAudio.Dock = DockStyle.Fill; _keepAudio.Padding = new Padding(8, 0, 0, 0); _keepAudio.Tag = "card"; right.Controls.Add(_keepAudio, 0, 3);
        _seamlessLoop.Dock = DockStyle.Fill; _seamlessLoop.Padding = new Padding(8, 0, 0, 0); _seamlessLoop.Tag = "card"; right.Controls.Add(_seamlessLoop, 0, 4);
        _stabilize.Dock = DockStyle.Fill; _stabilize.Padding = new Padding(8, 0, 0, 0); _stabilize.Tag = "card"; right.Controls.Add(_stabilize, 0, 5);
        right.Controls.Add(SliderRow("SUAVIZAÇÃO DO LOOP", _loopDuration, "0,25 a 3 segundos"), 0, 6);
        var specs = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, Tag = "card", Padding = new Padding(12), Margin = new Padding(0, 8, 0, 8) };
        foreach (var text in new[] { "Proporção e resolução preservadas", "WebM VP9 • alpha e áudio opcionais", "Processamento 100% local" })
            specs.Controls.Add(new Label { Text = "•  " + text, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.3f), TextAlign = ContentAlignment.MiddleLeft });
        right.Controls.Add(specs, 0, 7);
        right.Controls.Add(new Label { Text = "O loop une final e começo. O tripé virtual ancora o cenário em um quadro de referência e pode aplicar leve zoom para esconder bordas.", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.2f), Padding = new Padding(4, 8, 4, 2) }, 0, 8);
        _render.Dock = DockStyle.Fill; _render.Margin = new Padding(0, 4, 0, 2); right.Controls.Add(_render, 0, 9);
        _clear.Dock = DockStyle.Fill; _clear.Margin = new Padding(0, 6, 0, 0); right.Controls.Add(_clear, 0, 10); main.Controls.Add(right, 2, 0);

        var statusbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(16, 4, 16, 5), Tag = "header", Margin = Padding.Empty };
        statusbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); statusbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); statusbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); statusbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        statusbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); statusbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 5));
        statusbar.Controls.Add(new Label { Text = "●  PROCESSAMENTO LOCAL", Dock = DockStyle.Fill, ForeColor = Theme.AccentCyan, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 7.5f) }, 0, 0);
        _status.Dock = DockStyle.Fill; _status.TextAlign = ContentAlignment.MiddleLeft; statusbar.Controls.Add(_status, 1, 0);
        _cancel.Dock = DockStyle.Fill; _cancel.Margin = new Padding(0, 2, 10, 2); statusbar.Controls.Add(_cancel, 2, 0);
        statusbar.Controls.Add(new Label { Text = "ORIGINAL PRESERVADO", Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI Semibold", 8) }, 3, 0);
        statusbar.Controls.Add(_progress, 0, 1); statusbar.SetColumnSpan(_progress, 4); root.Controls.Add(statusbar, 0, 2);
    }

    private Control CreateBrand()
    {
        var brand = new Panel { Dock = DockStyle.Fill, Tag = "header", Margin = Padding.Empty };
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "vfl-suite-logo.png");
        Control mark = File.Exists(logoPath)
            ? new PictureBox { Image = Image.FromFile(logoPath), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 5), Size = new Size(44, 44) }
            : new Label { Text = "V", Location = new Point(0, 5), Size = new Size(44, 44), BackColor = Theme.Primary, Tag = "accent", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Black", 15) };
        brand.Controls.Add(mark);
        brand.Controls.Add(new Label { Text = "VFL Mapas Dinâmicos", Location = new Point(50, 3), Size = new Size(235, 25), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Semibold", 11.5f), Margin = Padding.Empty });
        brand.Controls.Add(new Label { Text = "WEBM VP9 PARA VTT  •  v3.1", Location = new Point(50, 29), Size = new Size(235, 16), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right, Tag = "brand-badge", TextAlign = ContentAlignment.TopLeft, Font = new Font("Segoe UI Semibold", 6.8f), Margin = Padding.Empty });
        return brand;
    }

    private Control CreatePlaybackPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(18, 4, 18, 8), Tag = "header", Margin = Padding.Empty };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _timeline.Margin = new Padding(0, 2, 0, 0); panel.Controls.Add(_timeline, 0, 0);
        _previewTime.Dock = DockStyle.Fill; _previewTime.Margin = Padding.Empty; panel.Controls.Add(_previewTime, 0, 1);
        var controls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, Tag = "header", Margin = Padding.Empty };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var index = 0; index < 4; index++) controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var item in new[] { (_play, 1), (_pause, 2), (_stop, 3), (_testLoop, 4) }) { item.Item1.Dock = DockStyle.Fill; item.Item1.Margin = new Padding(4); controls.Controls.Add(item.Item1, item.Item2, 0); }
        panel.Controls.Add(controls, 0, 2);
        return panel;
    }

    private static Control SliderRow(string title, ValueSlider slider, string hint)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Tag = "sidebar", Margin = Padding.Empty };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var labels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Tag = "sidebar", Margin = Padding.Empty };
        labels.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104)); labels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        labels.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 8), TextAlign = ContentAlignment.BottomLeft }, 0, 0);
        labels.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 7.5f), TextAlign = ContentAlignment.BottomRight }, 1, 0);
        row.Controls.Add(labels, 0, 0); slider.Dock = DockStyle.Fill; slider.Margin = new Padding(0, 5, 0, 4); row.Controls.Add(slider, 0, 1);
        return row;
    }

    private static Control ComboCard(string title, ComboBox combo)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Tag = "card", Padding = new Padding(10, 6, 10, 7), Margin = new Padding(0, 3, 0, 3) };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Theme.Dim, Font = new Font("Segoe UI Semibold", 7.3f), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        combo.Dock = DockStyle.Fill; combo.Margin = Padding.Empty; card.Controls.Add(combo, 0, 1);
        return card;
    }

    private static Label SectionTitle(string text, string icon) => new()
    {
        Text = $"{icon}  {text}", Dock = DockStyle.Fill, ForeColor = Theme.Text,
        Font = new Font("Segoe UI Semibold", 9), TextAlign = ContentAlignment.MiddleLeft
    };

    private void WireEvents()
    {
        _selectVideo.Click += async (_, _) => await SelectVideoAsync();
        _selectOutput.Click += (_, _) => SelectOutput();
        _render.Click += async (_, _) => await RenderAsync();
        _clear.Click += (_, _) => ResetVideo();
        _cancel.Click += (_, _) => _operationCts?.Cancel();
        _chooseColor.Click += (_, _) => ChooseColor();
        _removeBackground.CheckedChanged += (_, _) => ChromaChanged();
        _similarity.ValueChanged += (_, _) => PreviewSettingChanged();
        _blend.ValueChanged += (_, _) => PreviewSettingChanged();
        _quality.SelectedIndexChanged += (_, _) => SaveSettings();
        _frameRate.SelectedIndexChanged += (_, _) => SaveSettings();
        _keepAudio.CheckedChanged += (_, _) => SaveSettings();
        _seamlessLoop.CheckedChanged += (_, _) => LoopChanged();
        _loopDuration.ValueChanged += (_, _) => LoopSettingChanged();
        _stabilize.CheckedChanged += (_, _) =>
        {
            SaveSettings(); PausePlayback(); UpdatePreviewMode(); SchedulePreview();
            _status.Text = _stabilize.Checked
                ? "Tripé virtual ativado — o cenário será ancorado ao gerar o mapa."
                : "Tripé virtual desativado.";
        };
        _timeline.Scroll += (_, _) => { PausePlayback(); _previewPositionSeconds = TimelineToSeconds(); UpdatePreviewTime(); SchedulePreview(); };
        _play.Click += async (_, _) => await StartPlaybackAsync();
        _pause.Click += (_, _) => PausePlayback();
        _stop.Click += async (_, _) => await StopPlaybackAsync();
        _testLoop.Click += async (_, _) => await TestLoopAsync();
        _previewDebounce.Tick += async (_, _) => { _previewDebounce.Stop(); await RefreshPreviewAsync(); };
    }

    private async Task SelectVideoAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Vídeos|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v|Todos os arquivos|*.*" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        ResetVideo();
        _inputPath = dialog.FileName;
        _outputPath = AvailableOutput(Path.Combine(Path.GetDirectoryName(_inputPath)!, Path.GetFileNameWithoutExtension(_inputPath) + "_MAPA_FOUNDRY.webm"));
        _outputFolder.Text = _outputPath;
        _videoTitle.Text = Path.GetFileName(_inputPath);
        await AnalyzeAsync();
    }

    private void SelectOutput()
    {
        using var dialog = new SaveFileDialog { Filter = "WebM para Foundry|*.webm", DefaultExt = "webm", AddExtension = true, FileName = string.IsNullOrWhiteSpace(_outputPath) ? "MAPA_FOUNDRY.webm" : Path.GetFileName(_outputPath) };
        if (!string.IsNullOrWhiteSpace(_outputPath)) dialog.InitialDirectory = Path.GetDirectoryName(_outputPath);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _outputPath = dialog.FileName;
        _outputFolder.Text = _outputPath;
    }

    private async Task AnalyzeAsync()
    {
        _operationCts = new CancellationTokenSource(); SetBusy(true);
        try
        {
            var progress = new Progress<string>(message => _status.Text = message);
            _analysis = await new DynamicMapService().AnalyzeAsync(_inputPath, progress, _operationCts.Token);
            _colorSwatch.BackColor = _analysis.BackgroundColor;
            _detected.Text = $"#{_analysis.BackgroundColor.R:X2}{_analysis.BackgroundColor.G:X2}{_analysis.BackgroundColor.B:X2}\n{_analysis.Confidence:P0} confiança";
            ConfigureTimeline(); SetPreviewPosition(0);
            _videoMeta.Text = $"{_analysis.Video.Width}×{_analysis.Video.Height}  •  {_analysis.Video.FrameRate:0.##} FPS  •  {FormatTime(_analysis.Video.Duration)}" + (_analysis.Video.HasAlpha ? "  •  alpha detectado" : "");
            _render.Enabled = true; SetPlaybackEnabled(true);
            await RefreshPreviewAsync();
        }
        catch (OperationCanceledException) { _status.Text = "Análise cancelada."; }
        catch (Exception exception) { _status.Text = "Falha na análise."; TokenDialog.ShowMessage(FindForm()!, "Erro na análise", exception.Message); }
        finally { SetBusy(false); _operationCts.Dispose(); _operationCts = null; }
    }

    private async Task RefreshPreviewAsync()
    {
        if (_analysis is null || _operationCts is not null || !File.Exists(_inputPath)) return;
        _previewCts?.Cancel(); _previewCts?.Dispose();
        var cts = new CancellationTokenSource(); _previewCts = cts;
        string? previewPath = null;
        try
        {
            _status.Text = "Atualizando prévia do mapa...";
            previewPath = await new DynamicMapService().CreatePreviewAsync(_inputPath, _analysis, _removeBackground.Checked,
                (double)_similarity.Value, (double)_blend.Value, _previewPositionSeconds, cts.Token);
            if (cts.IsCancellationRequested) return;
            _preview.PreviewImage = LoadImage(previewPath);
            UpdatePreviewMode();
            _status.Text = $"Prévia pronta • {_analysis.Video.Width}×{_analysis.Video.Height} • proporção preservada";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _status.Text = "Falha na prévia: " + exception.Message.Split('\n')[0]; }
        finally
        {
            if (previewPath is not null) try { File.Delete(previewPath); } catch { }
            if (ReferenceEquals(_previewCts, cts)) { _previewCts.Dispose(); _previewCts = null; }
        }
    }

    private async Task StartPlaybackAsync()
    {
        if (_analysis is null || _operationCts is not null) return;
        if (_previewPositionSeconds >= _analysis.Video.Duration - 0.05) SetPreviewPosition(0);
        PausePlayback(); _previewDebounce.Stop(); _previewCts?.Cancel();
        var cts = new CancellationTokenSource(); _playbackCts = cts; _isPlaying = true;
        _play.Enabled = false; _pause.Enabled = true; _stop.Enabled = true;
        var start = _previewPositionSeconds;
        try
        {
            _status.Text = "Reproduzindo prévia a 24 FPS...";
            await new DynamicMapService().StreamPreviewAsync(_inputPath, _analysis, _removeBackground.Checked,
                (double)_similarity.Value, (double)_blend.Value, _stabilize.Checked, start, async (bitmap, seconds) =>
                {
                    if (cts.IsCancellationRequested) { bitmap.Dispose(); return; }
                    var updated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    BeginInvoke(() =>
                    {
                        if (cts.IsCancellationRequested) bitmap.Dispose();
                        else { _preview.PreviewImage = bitmap; SetPreviewPosition(seconds); }
                        updated.TrySetResult(true);
                    });
                    await updated.Task;
                }, cts.Token);
            if (!cts.IsCancellationRequested) { SetPreviewPosition(_analysis.Video.Duration); _status.Text = "Fim da prévia — mapa conferido até o último quadro."; }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _status.Text = "Falha na reprodução: " + exception.Message.Split('\n')[0]; }
        finally
        {
            if (ReferenceEquals(_playbackCts, cts)) { _isPlaying = false; _playbackCts.Dispose(); _playbackCts = null; SetPlaybackEnabled(_analysis is not null); }
        }
    }

    private async Task StopPlaybackAsync()
    {
        PausePlayback();
        if (_analysis is null) return;
        SetPreviewPosition(0); await RefreshPreviewAsync();
    }

    private async Task TestLoopAsync()
    {
        if (_analysis is null || !_seamlessLoop.Checked || _operationCts is not null) return;
        PausePlayback(); _previewDebounce.Stop(); _previewCts?.Cancel();
        var cts = new CancellationTokenSource(); _playbackCts = cts; _isPlaying = true;
        _play.Enabled = false; _pause.Enabled = true; _stop.Enabled = true; _testLoop.Enabled = false;
        try
        {
            _status.Text = "Testando somente a passagem final → começo...";
            await new DynamicMapService().StreamLoopPreviewAsync(_inputPath, _analysis, _removeBackground.Checked,
                (double)_similarity.Value, (double)_blend.Value, _stabilize.Checked, (double)_loopDuration.Value,
                async (bitmap, seconds) =>
                {
                    if (cts.IsCancellationRequested) { bitmap.Dispose(); return; }
                    var updated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    BeginInvoke(() =>
                    {
                        if (cts.IsCancellationRequested) bitmap.Dispose();
                        else { _preview.PreviewImage = bitmap; _previewTime.Text = $"TESTE DO LOOP  •  {FormatTime(seconds)}"; }
                        updated.TrySetResult(true);
                    });
                    await updated.Task;
                }, cts.Token);
            if (!cts.IsCancellationRequested) _status.Text = "Teste concluído — observe se a passagem final → começo ficou natural.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _status.Text = "Falha ao testar o loop: " + exception.Message.Split('\n')[0]; }
        finally
        {
            if (ReferenceEquals(_playbackCts, cts))
            {
                _isPlaying = false; _playbackCts.Dispose(); _playbackCts = null;
                UpdatePreviewTime(); SetPlaybackEnabled(_analysis is not null);
            }
        }
    }

    private async Task RenderAsync()
    {
        if (_analysis is null || !File.Exists(_inputPath)) { TokenDialog.ShowMessage(FindForm()!, "Vídeo necessário", "Selecione e analise um vídeo antes de gerar."); return; }
        if (string.IsNullOrWhiteSpace(_outputPath)) { SelectOutput(); if (string.IsNullOrWhiteSpace(_outputPath)) return; }
        if (File.Exists(_outputPath)) { _outputPath = AvailableOutput(_outputPath); _outputFolder.Text = _outputPath; }
        PausePlayback(); _operationCts = new CancellationTokenSource(); SetBusy(true);
        try
        {
            var quality = Enum.TryParse<MapQualityPreset>(_quality.SelectedItem?.ToString(), out var parsed) ? parsed : MapQualityPreset.Equilibrada;
            var progress = new Progress<RenderProgress>(item => { _progress.Value = (int)item.Percent; _status.Text = item.Message; });
            var originalSize = new FileInfo(_inputPath).Length;
            await new DynamicMapService().RenderAsync(_inputPath, _outputPath, _analysis, _removeBackground.Checked,
                (double)_similarity.Value, (double)_blend.Value, quality, SelectedFrameRate(), _keepAudio.Checked,
                _seamlessLoop.Checked, (double)_loopDuration.Value, _stabilize.Checked, progress, _operationCts.Token);
            var finalSize = new FileInfo(_outputPath).Length;
            var reduction = originalSize <= 0 ? 0 : (1 - finalSize / (double)originalSize) * 100;
            var message = $"Mapa WebM concluído!\n\nOriginal: {FormatBytes(originalSize)}\nWebM: {FormatBytes(finalSize)}" +
                          (reduction > 0 ? $"\nEconomia: {reduction:0.#}%" : "") + "\n\nDeseja abrir a pasta?";
            if (TokenDialog.Confirm(FindForm()!, "Mapa concluído", message))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_outputPath}\"") { UseShellExecute = true });
        }
        catch (OperationCanceledException) { _status.Text = "Conversão cancelada."; }
        catch (Exception exception) { _status.Text = "Falha na conversão."; TokenDialog.ShowMessage(FindForm()!, "Erro na conversão", exception.Message); }
        finally { SetBusy(false); _operationCts.Dispose(); _operationCts = null; }
    }

    private void ChooseColor()
    {
        if (_analysis is null) return;
        using var dialog = new ColorDialog { Color = _analysis.BackgroundColor, FullOpen = true };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _analysis = _analysis with { BackgroundColor = dialog.Color, Confidence = 1 };
        _colorSwatch.BackColor = dialog.Color; _detected.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}\nmanual";
        if (!_removeBackground.Checked) _removeBackground.Checked = true; else SchedulePreview();
    }

    private void ChromaChanged()
    {
        UpdateChromaControls(); SaveSettings(); PausePlayback(); SchedulePreview();
        UpdatePreviewMode();
    }

    private void LoopChanged()
    {
        _loopDuration.Enabled = _seamlessLoop.Checked && _operationCts is null;
        SaveSettings(); PausePlayback(); UpdatePreviewMode(); SetPlaybackEnabled(_analysis is not null && _operationCts is null);
        _status.Text = _seamlessLoop.Checked
            ? "Loop contínuo ativado — use TESTAR LOOP para conferir a passagem."
            : "Loop contínuo desativado.";
    }

    private void LoopSettingChanged()
    {
        SaveSettings();
        if (_seamlessLoop.Checked) _status.Text = $"Transição do loop: {_loopDuration.Value:0.##} segundo(s).";
    }

    private void UpdatePreviewMode()
    {
        var alpha = _removeBackground.Checked || _analysis?.Video.HasAlpha == true ? "VP9 ALPHA" : "VP9";
        _previewMode.Text = "PROPORÇÃO ORIGINAL  •  " + alpha + (_seamlessLoop.Checked ? "  •  LOOP" : "") + (_stabilize.Checked ? "  •  CÂMERA FIXA" : "");
    }

    private void PreviewSettingChanged()
    {
        SaveSettings(); PausePlayback(); SchedulePreview();
    }

    private void UpdateChromaControls()
    {
        var enabled = _removeBackground.Checked && _operationCts is null;
        _similarity.Enabled = enabled; _blend.Enabled = enabled; _chooseColor.Enabled = enabled && _analysis is not null;
    }

    private void SchedulePreview()
    {
        if (_analysis is null || _operationCts is not null) return;
        _previewDebounce.Stop(); _previewDebounce.Start();
    }

    private void SetBusy(bool busy)
    {
        _selectVideo.Enabled = !busy; _selectOutput.Enabled = !busy; _clear.Enabled = !busy; _cancel.Enabled = busy;
        _render.Enabled = !busy && _analysis is not null; _quality.Enabled = !busy; _frameRate.Enabled = !busy;
        _keepAudio.Enabled = !busy; _removeBackground.Enabled = !busy; _seamlessLoop.Enabled = !busy; _stabilize.Enabled = !busy;
        _loopDuration.Enabled = !busy && _seamlessLoop.Checked; UpdateChromaControls();
        if (busy) { PausePlayback(); _play.Enabled = false; _pause.Enabled = false; _stop.Enabled = false; _testLoop.Enabled = false; }
        else SetPlaybackEnabled(_analysis is not null);
    }

    private void ResetVideo()
    {
        PausePlayback(); _previewDebounce.Stop(); _previewCts?.Cancel();
        _analysis = null; _inputPath = ""; _outputPath = ""; _previewPositionSeconds = 0;
        _preview.PreviewImage = null; _videoTitle.Text = "Nenhum vídeo carregado"; _videoMeta.Text = "Selecione um arquivo para começar";
        _outputFolder.Text = "Destino automático"; _detected.Text = ""; _colorSwatch.BackColor = Theme.Field;
        _timeline.Value = 0; _timeline.Maximum = 1_000; _progress.Value = 0; UpdatePreviewTime(); SetPlaybackEnabled(false); UpdateChromaControls(); UpdatePreviewMode();
        _status.Text = "Pronto para o próximo mapa. Suas configurações foram mantidas.";
    }

    private void ConfigureTimeline()
    {
        if (_analysis is null) return;
        _timeline.Maximum = Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(_analysis.Video.Duration * 1000)));
        _timeline.SmallChange = 1; _timeline.LargeChange = 100;
    }

    private void SetPreviewPosition(double seconds)
    {
        if (_analysis is null) return;
        _previewPositionSeconds = Math.Clamp(seconds, 0, _analysis.Video.Duration);
        _timeline.Value = _analysis.Video.Duration <= 0 ? 0 : Math.Clamp((int)Math.Round(_previewPositionSeconds / _analysis.Video.Duration * _timeline.Maximum), 0, _timeline.Maximum);
        UpdatePreviewTime();
    }

    private double TimelineToSeconds() => _analysis is null ? 0 : _timeline.Value / (double)_timeline.Maximum * _analysis.Video.Duration;
    private void UpdatePreviewTime() => _previewTime.Text = $"{FormatTime(_previewPositionSeconds)} / {FormatTime(_analysis?.Video.Duration ?? 0)}";
    private void PausePlayback() { _isPlaying = false; _playbackCts?.Cancel(); SetPlaybackEnabled(_analysis is not null && _operationCts is null); }
    private void SetPlaybackEnabled(bool enabled) { _timeline.Enabled = enabled; _play.Enabled = enabled && !_isPlaying; _pause.Enabled = enabled && _isPlaying; _stop.Enabled = enabled; _testLoop.Enabled = enabled && !_isPlaying && _seamlessLoop.Checked; }

    private double? SelectedFrameRate() => _frameRate.SelectedItem?.ToString() switch
    {
        "30 FPS" => 30,
        "24 FPS" => 24,
        "15 FPS" => 15,
        _ => null
    };

    private void LoadSettings()
    {
        var settings = AppSettings.Load();
        _similarity.Value = Clamp(settings.MapSimilarity, _similarity.Minimum, _similarity.Maximum);
        _blend.Value = Clamp(settings.MapBlend, _blend.Minimum, _blend.Maximum);
        _quality.SelectedItem = Enum.TryParse<MapQualityPreset>(settings.MapQuality, out var quality) ? quality.ToString() : MapQualityPreset.Equilibrada.ToString();
        _frameRate.SelectedItem = _frameRate.Items.Contains(settings.MapFrameRate) ? settings.MapFrameRate : "24 FPS";
        _keepAudio.Checked = settings.MapKeepAudio; _removeBackground.Checked = settings.MapRemoveBackground;
        _seamlessLoop.Checked = settings.MapSeamlessLoop;
        _loopDuration.Value = Clamp(settings.MapLoopDuration, _loopDuration.Minimum, _loopDuration.Maximum);
        _stabilize.Checked = settings.MapStabilize;
    }

    private void SaveSettings()
    {
        var settings = AppSettings.Load();
        settings.MapSimilarity = _similarity.Value; settings.MapBlend = _blend.Value;
        settings.MapQuality = _quality.SelectedItem?.ToString() ?? MapQualityPreset.Equilibrada.ToString();
        settings.MapFrameRate = _frameRate.SelectedItem?.ToString() ?? "24 FPS";
        settings.MapKeepAudio = _keepAudio.Checked; settings.MapRemoveBackground = _removeBackground.Checked;
        settings.MapSeamlessLoop = _seamlessLoop.Checked; settings.MapLoopDuration = _loopDuration.Value;
        settings.MapStabilize = _stabilize.Checked;
        settings.Save();
    }

    private static string AvailableOutput(string desiredPath)
    {
        if (!File.Exists(desiredPath)) return desiredPath;
        var directory = Path.GetDirectoryName(desiredPath) ?? ""; var extension = Path.GetExtension(desiredPath); var name = Path.GetFileNameWithoutExtension(desiredPath); var suffix = 2;
        string candidate;
        do { candidate = Path.Combine(directory, $"{name}_{suffix++}{extension}"); } while (File.Exists(candidate));
        return candidate;
    }

    private static Image LoadImage(string path) { using var stream = File.OpenRead(path); using var image = Image.FromStream(stream); return new Bitmap(image); }
    private static string FormatTime(double seconds) { var time = TimeSpan.FromSeconds(Math.Max(0, seconds)); return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss\.fff") : time.ToString(@"mm\:ss\.fff"); }
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.##} GB" : bytes >= 1024L * 1024 ? $"{bytes / (1024d * 1024):0.##} MB" : $"{bytes / 1024d:0.##} KB";
    private static ValueSlider Slider(decimal min, decimal max, decimal value, decimal increment, int decimals) => new() { Minimum = min, Maximum = max, Value = value, Increment = increment, DecimalPlaces = decimals, Tag = "surface" };
    private static decimal Clamp(decimal value, decimal min, decimal max) => Math.Min(max, Math.Max(min, value));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewDebounce.Stop(); _previewDebounce.Dispose();
            _operationCts?.Cancel(); _previewCts?.Cancel(); _playbackCts?.Cancel();
            _operationCts?.Dispose(); _previewCts?.Dispose(); _playbackCts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
