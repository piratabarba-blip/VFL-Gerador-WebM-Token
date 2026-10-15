namespace VFL.GeradorWebMToken;

internal sealed class ModeTabControl : UserControl
{
    private readonly FlowLayoutPanel _header = new()
    {
        Dock = DockStyle.Top,
        Height = 44,
        BackColor = Theme.Header,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Padding = Padding.Empty,
        Margin = Padding.Empty
    };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = Padding.Empty };
    private readonly List<(ModeTabButton Button, Control Page)> _pages = [];
    private int _selectedIndex = -1;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= _pages.Count || value == _selectedIndex) return;
            _selectedIndex = value;
            for (var index = 0; index < _pages.Count; index++)
            {
                _pages[index].Page.Visible = index == value;
                _pages[index].Button.Selected = index == value;
            }
        }
    }

    public ModeTabControl()
    {
        BackColor = Theme.Background;
        Controls.Add(_content);
        Controls.Add(_header);
    }

    public void AddTab(string title, Control page)
    {
        var index = _pages.Count;
        var button = new ModeTabButton { Text = title, Width = 230, Height = 44, Margin = Padding.Empty };
        button.Click += (_, _) => SelectedIndex = index;
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pages.Add((button, page));
        _header.Controls.Add(button);
        _content.Controls.Add(page);
        if (_selectedIndex < 0) SelectedIndex = 0;
    }

    private sealed class ModeTabButton : Control
    {
        private bool _selected;

        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public ModeTabButton()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 9f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var background = new SolidBrush(Selected ? Theme.Card : Theme.Header);
            e.Graphics.FillRectangle(background, ClientRectangle);
            if (Selected)
            {
                using var accent = new SolidBrush(Theme.Primary);
                e.Graphics.FillRectangle(accent, 12, Height - 4, Math.Max(0, Width - 24), 4);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Selected ? Theme.Text : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
