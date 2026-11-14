using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StatsMonitor.Helpers;

namespace StatsMonitor.Views;

public sealed record PetData(string Name, string Emoji);

/// <summary>Persists the user's pets in %AppData%\StatsMonitor\pets.json.</summary>
public static class PetStore
{
    public const int MaxPets = 12;
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatsMonitor", "pets.json");

    public static List<PetData> Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<PetData>>(File.ReadAllText(FilePath)) ?? []; }
        catch { }
        return [];
    }

    private static readonly string BgPath = Path.Combine(Path.GetDirectoryName(FilePath)!, "pets-bg.txt");

    public static int LoadBackground()
    {
        try { if (File.Exists(BgPath) && int.TryParse(File.ReadAllText(BgPath), out int i)) return i; } catch { }
        return 0;
    }

    public static void SaveBackground(int i)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(BgPath)!); File.WriteAllText(BgPath, i.ToString()); } catch { }
    }

    private static readonly string FoldPath = Path.Combine(Path.GetDirectoryName(FilePath)!, "pets-collapsed.txt");
    public static bool LoadCollapsed() { try { return File.Exists(FoldPath) && File.ReadAllText(FoldPath).Trim() == "1"; } catch { return false; } }
    public static void SaveCollapsed(bool v) { try { Directory.CreateDirectory(Path.GetDirectoryName(FoldPath)!); File.WriteAllText(FoldPath, v ? "1" : "0"); } catch { } }

    public static void Save(IEnumerable<PetData> pets)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(pets.ToList()));
        }
        catch { }
    }
}

/// <summary>Playground where named pets wander around while the user waits for long tasks such as a diagnostic.</summary>
public sealed class PetsView : UserControl
{
    public static readonly string[] Keywords = ["Pets", "Pet", "Bichinhos", "Animais de estimação", "Adicionar pet", "Alimentar", "Brincar"];

    private static string L(string pt, string en) => Loc.Lang == "en" ? en : pt;

    private static readonly (string Emoji, string Pt, string En)[] Species =
    [
        ("🐶", "Cachorro", "Dog"), ("🐱", "Gato", "Cat"), ("🐰", "Coelho", "Rabbit"), ("🐹", "Hamster", "Hamster"), ("🦊", "Raposa", "Fox"),
        ("🐼", "Panda", "Panda"), ("🐸", "Sapo", "Frog"), ("🐥", "Pintinho", "Chick"), ("🐢", "Tartaruga", "Turtle"), ("🦄", "Unicórnio", "Unicorn"),
        ("🐙", "Polvo", "Octopus"), ("🐧", "Pinguim", "Penguin"), ("🦖", "Dinossauro", "Dinosaur"), ("🐷", "Porquinho", "Pig"), ("🐻", "Urso", "Bear"),
    ];

    private sealed class Sprite
    {
        public required PetData Data;
        public required StackPanel Root;
        public required TextBlock Face;
        public required ScaleTransform Flip;
        public required TranslateTransform Hop;
        public double X, Y, TX, TY, Speed, Phase, Excite;
        public int Pause;
        public bool Eating;
    }

    private static Color C(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);

    private sealed record Bg(string Icon, string Pt, string En, Color A, Color B, Color? Ground, bool Dark, (string, int, double, double)[] Decor);

    private static readonly Bg[] Backgrounds =
    [
        new("🎨", "Padrão", "Default", Colors.Transparent, Colors.Transparent, null, false, []),
        new("🌳", "Gramado", "Meadow", C(0xA8, 0xE0, 0xFF), C(0xD6, 0xF1, 0xFF), C(0x5B, 0xB3, 0x4A), false, [("☁️", 22, 0.08, 0.9), ("🌳", 26, 0.62, 1), ("🌼", 12, 0.86, 0.9)]),
        new("🌙", "Noite estrelada", "Starry night", C(0x0B, 0x10, 0x2E), C(0x2B, 0x2F, 0x6B), null, true, [("⭐", 8, 0.1, 0.8), ("🌙", 26, 0.05, 1), ("✨", 9, 0.45, 0.6)]),
        new("🏖️", "Praia", "Beach", C(0x8E, 0xD8, 0xF8), C(0xCF, 0xEE, 0xFB), C(0xF2, 0xD9, 0x9B), false, [("☀️", 26, 0.03, 1), ("🌴", 26, 0.6, 1), ("🐚", 11, 0.88, 0.95)]),
        new("❄️", "Neve", "Snow", C(0xB5, 0xD6, 0xEE), C(0xE6, 0xF2, 0xFA), C(0xFF, 0xFF, 0xFF), false, [("❄️", 10, 0.1, 0.8), ("⛄", 26, 0.6, 1), ("🌲", 24, 0.56, 1)]),
        new("🌇", "Pôr do sol", "Sunset", C(0x7F, 0x53, 0xAC), C(0xFF, 0x9A, 0x6B), null, true, [("🌅", 26, 0.12, 1), ("☁️", 14, 0.2, 0.7), ("🐦", 10, 0.3, 0.8)]),
    ];
    private void OnBgChanged(object sender, SelectionChangedEventArgs e) { if (_bg.SelectedIndex < 0) return; PetStore.SaveBackground(_bg.SelectedIndex); ApplyBackground(); }

    private void ApplyBackground()
    {
        if (_frame == null) return;
        int i = Math.Clamp(_bg.SelectedIndex, 0, Backgrounds.Length - 1);
        if (i == 0) { _frame.SetResourceReference(Border.BackgroundProperty, "CardBg"); if (IsLoaded) Decorate(); return; }
        _frame.ClearValue(Border.BackgroundProperty);
        _frame.Background = new LinearGradientBrush(Backgrounds[i].A, Backgrounds[i].B, 90);
        if (IsLoaded) Decorate();
    }

    private static readonly Random Rng = new();
    private readonly List<PetData> _pets = PetStore.Load();
    private readonly List<Sprite> _sprites = [];
    private readonly Canvas _field = new() { ClipToBounds = true, Height = 124 };
    private readonly TextBox _name = new() { MaxLength = 20, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _species = new() { Width = 56, Margin = new Thickness(4, 0, 0, 0) };
    private readonly TextBlock _status = new() { Opacity = 0.8, FontSize = 11, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private TextBlock? _hint;
    private Border? _frame;
    private bool _collapsed = PetStore.LoadCollapsed();
    private readonly ComboBox _bg = new() { Margin = new Thickness(0, 0, 0, 4) };
    private Point? _food;
    private TextBlock? _foodVisual;

    public PetsView()
    {
        Build();
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { Decorate(); SyncSprites(); };
        _field.SizeChanged += (_, e) => { if (e.WidthChanged && IsLoaded) Decorate(); };
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) { SyncSprites(); _timer.Start(); } else _timer.Stop(); };
        if (IsVisible) _timer.Start();
        Loc.LanguageChanged += () => { if (IsLoaded) Build(); };
    }

    private void Build()
    {
        var root = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var feed = MakeButton("🍖", (_, _) => Feed(), false);
        feed.ToolTip = L("Alimentar os pets", "Feed the pets");
        var fold = MakeButton(_collapsed ? "▸" : "▾", null!, false);
        fold.ToolTip = L("Esconder / mostrar a área dos pets", "Hide / show the pets area");
        fold.Click += (_, _) => { _collapsed = !_collapsed; PetStore.SaveCollapsed(_collapsed); Build(); };
        DockPanel.SetDock(fold, Dock.Right);
        DockPanel.SetDock(feed, Dock.Right);
        feed.Margin = new Thickness(0, 0, 4, 0);
        head.Children.Add(fold);
        if (!_collapsed) head.Children.Add(feed);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "🐾", FontFamily = new FontFamily("Segoe UI Emoji"), Margin = new Thickness(0, 0, 6, 0) });
        title.Children.Add(new TextBlock { Text = "Pets", FontWeight = FontWeights.Bold });
        title.Children.Add(Detach(_count));
        head.Children.Add(title);
        root.Children.Add(head);
        root.Children.Add(Detach(_status));
        Content = Card(root);
        if (_collapsed) { _timer.Stop(); return; }
        if (IsVisible) _timer.Start();

        var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = Detach(_field), Margin = new Thickness(0, 0, 0, 8), ClipToBounds = true };
        _frame = frame;
        frame.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        root.Children.Add(frame);

        _species.Items.Clear();
        foreach (var s in Species) _species.Items.Add(new ComboBoxItem { Content = $"{s.Emoji}  {L(s.Pt, s.En)}", FontFamily = new FontFamily("Segoe UI Emoji,Segoe UI") });
        if (_species.SelectedIndex < 0) _species.SelectedIndex = 0;
        _name.ToolTip = L("Nome do pet (Enter adiciona). Botão direito em um pet o remove.", "Pet name (Enter adds). Right-click a pet to remove it.");
        _name.KeyDown -= OnNameKey; _name.KeyDown += OnNameKey;
        _bg.SelectionChanged -= OnBgChanged;
        _bg.Items.Clear();
        foreach (var b in Backgrounds) _bg.Items.Add(new ComboBoxItem { Content = b.Icon + "  " + L(b.Pt, b.En), FontFamily = new FontFamily("Segoe UI Emoji,Segoe UI") });
        _bg.SelectedIndex = Math.Clamp(PetStore.LoadBackground(), 0, Backgrounds.Length - 1);
        _bg.SelectionChanged += OnBgChanged;
        _bg.ToolTip = L("Plano de fundo dos pets", "Pets background");
        ApplyBackground();
        var hint = new TextBlock { Text = L("Nome do pet…", "Pet name…"), Opacity = 0.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        _name.TextChanged -= OnNameChanged; _name.TextChanged += OnNameChanged;
        _hint = hint;
        var nameBox = new Grid();
        nameBox.Children.Add(Detach(_name));
        nameBox.Children.Add(hint);
        var plus = MakeButton("➕", OnAdd, true);
        plus.ToolTip = L("Adicionar pet", "Add pet");
        _species.Width = 108; _species.Margin = new Thickness(4, 0, 4, 0);
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 40 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(Detach(_species), 1); Grid.SetColumn(plus, 2);
        row.Children.Add(nameBox); row.Children.Add(_species); row.Children.Add(plus);
        root.Children.Add(row);
        root.Children.Add(Detach(_bg));
        if (IsLoaded) { _field.Children.Clear(); _sprites.Clear(); _decor.Clear(); _empty = null; Decorate(); SyncSprites(); }
    }

    private static Border Card(UIElement child)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 8), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "CardBg");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.18, Color = Colors.Black };
        return card;
    }

    private readonly TextBlock _count = new() { Opacity = 0.6, Margin = new Thickness(6, 0, 0, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<UIElement> _decor = [];
    private TextBlock? _empty;

    private void UpdateCount()
    {
        _count.Text = _pets.Count == 0 ? "" : $"{_pets.Count}/{PetStore.MaxPets}";
        if (_empty != null) _empty.Visibility = _pets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Decorate()
    {
        foreach (var d in _decor) _field.Children.Remove(d);
        _decor.Clear();
        double w = _field.ActualWidth > 0 ? _field.ActualWidth : 180, h = _field.Height;
        int i = Math.Clamp(_bg.SelectedIndex, 0, Backgrounds.Length - 1);
        var bgd = Backgrounds[i];
        void Add(UIElement el, int z = 0) { _field.Children.Insert(Math.Min(z, _field.Children.Count), el); _decor.Add(el); }
        if (bgd.Ground is { } g)
        {
            var ground = new System.Windows.Shapes.Rectangle { Width = w, Height = 22, Fill = new SolidColorBrush(g), IsHitTestVisible = false };
            Canvas.SetLeft(ground, 0); Canvas.SetTop(ground, h - 22);
            Add(ground);
        }
        var rnd = new Random(i * 31 + 7);
        foreach (var (glyph, size, yFrac, op) in bgd.Decor)
        {
            int n = size >= 24 ? 2 : 5;
            for (int k = 0; k < n; k++)
            {
                var tb = new TextBlock { Text = glyph, FontSize = size, Opacity = op, IsHitTestVisible = false, FontFamily = new FontFamily("Segoe UI Emoji") };
                Canvas.SetLeft(tb, rnd.NextDouble() * Math.Max(1, w - size)); Canvas.SetTop(tb, Math.Max(0, yFrac * (h - size) + rnd.Next(-6, 6)));
                Add(tb);
            }
        }
        _empty = new TextBlock { Text = L("Dê um nome e adicione seu primeiro pet 👇", "Give it a name and add your first pet 👇"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Width = Math.Max(80, w - 24), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, IsHitTestVisible = false, Opacity = 0.85, FontFamily = new FontFamily("Segoe UI Emoji,Segoe UI") };
        if (!bgd.Dark && i != 0) _empty.Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x44));
        else if (i == 0) _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        Canvas.SetLeft(_empty, 12); Canvas.SetTop(_empty, h / 2 - 14);
        _field.Children.Add(_empty); _decor.Add(_empty);
        UpdateCount();
    }
    private static T Detach<T>(T el) where T : FrameworkElement
    {
        if (el.Parent is Panel p) p.Children.Remove(el);
        else if (el.Parent is Decorator d) d.Child = null;
        else if (el.Parent is ContentControl c) c.Content = null;
        return el;
    }

        private static Button MakeButton(string text, RoutedEventHandler click, bool primary)
    {
        var b = new Button { Content = text, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0), Cursor = Cursors.Arrow };
        if (Application.Current.TryFindResource(primary ? "PrimaryButton" : "PillButton") is Style st) b.Style = st;
        if (click != null) b.Click += click;
        return b;
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e) { if (_hint != null) _hint.Visibility = _name.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }

    private void OnNameKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) OnAdd(sender, e); }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        string name = _name.Text.Trim();
        if (name.Length == 0) { _name.Focus(); return; }
        if (_pets.Count >= PetStore.MaxPets) { _status.Text = L($"Limite de {PetStore.MaxPets} pets atingido.", $"Limit of {PetStore.MaxPets} pets reached."); return; }
        string emoji = Species[Math.Max(0, _species.SelectedIndex)].Emoji;
        _pets.Add(new PetData(name, emoji));
        PetStore.Save(_pets);
        _name.Clear();
        SyncSprites();
    }

    // ---------- playground ----------

    private void SyncSprites()
    {
        UpdateCount();
        foreach (var sp in _sprites.Where(s => !_pets.Contains(s.Data)).ToList()) { _field.Children.Remove(sp.Root); _sprites.Remove(sp); }
        double w = Math.Max(120, _field.ActualWidth > 0 ? _field.ActualWidth : 160), h = _field.Height;
        foreach (var p in _pets.Where(p => _sprites.All(s => s.Data != p)))
        {
            var flip = new ScaleTransform(1, 1);
            var hop = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(flip); group.Children.Add(hop);
            var face = new TextBlock { Text = p.Emoji, FontSize = 28, FontFamily = new FontFamily("Segoe UI Emoji"), RenderTransform = group, RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center };
            var label = new TextBlock { Text = p.Name, FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
            label.Foreground = Brushes.White;
            var tag = new Border { CornerRadius = new CornerRadius(8), Child = label, HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(150, 20, 24, 40)), Margin = new Thickness(0, 0, 0, 1) };
            var root = new StackPanel { Cursor = Cursors.Arrow, Background = Brushes.Transparent };
            root.Children.Add(tag); root.Children.Add(face);
            root.Children.Add(new System.Windows.Shapes.Ellipse { Width = 24, Height = 5, Fill = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), Margin = new Thickness(0, -3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false });
            var sp = new Sprite
            {
                Data = p, Root = root, Face = face, Flip = flip, Hop = hop,
                X = Rng.NextDouble() * Math.Max(1, w - 50), Y = Rng.NextDouble() * Math.Max(1, h - 60),
                Speed = 1.2 + Rng.NextDouble() * 1.6, Phase = Rng.NextDouble() * 6,
            };
            sp.TX = sp.X; sp.TY = sp.Y;
            root.MouseLeftButtonDown += (_, e) => { Cuddle(sp); e.Handled = true; };
            ToolTipService.SetToolTip(root, p.Name);
            root.MouseRightButtonUp += (_, e) => { _pets.Remove(p); PetStore.Save(_pets); SyncSprites(); e.Handled = true; };
            _field.Children.Add(root);
            _sprites.Add(sp);
            Canvas.SetLeft(root, sp.X); Canvas.SetTop(root, sp.Y);
        }
    }

    private void Tick()
    {
        var st = Views.DiagnosticsView.Status;
        _status.Text = st is null ? "" : "🩺 " + st;
        _status.Visibility = st is null ? Visibility.Collapsed : Visibility.Visible;

        double w = _field.ActualWidth, h = _field.ActualHeight;
        if (w < 40 || h < 40) return;
        foreach (var s in _sprites)
        {
            s.Phase += 0.25;
            double sw = Math.Max(50, s.Root.ActualWidth), sh = Math.Max(60, s.Root.ActualHeight);
            if (s.Pause > 0) { s.Pause--; }
            else
            {
                double dx = s.TX - s.X, dy = s.TY - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
                double speed = s.Speed * (_food is not null ? 2.2 : 1);
                if (d <= speed)
                {
                    s.X = s.TX; s.Y = s.TY;
                    if (s.Eating) { s.Eating = false; s.Excite = 1; Heart(s, "😋"); }
                    s.Pause = 25 + Rng.Next(90);
                    s.TX = Rng.NextDouble() * Math.Max(1, w - sw);
                    s.TY = Rng.NextDouble() * Math.Max(1, h - sh);
                }
                else
                {
                    s.X += dx / d * speed; s.Y += dy / d * speed;
                    if (Math.Abs(dx) > 1) s.Flip.ScaleX = dx < 0 ? 1 : -1;
                }
            }
            bool moving = s.Pause == 0;
            double amp = (moving ? 4 : 1) + s.Excite * 10;
            s.Hop.Y = -Math.Abs(Math.Sin(s.Phase * (moving ? 1 : 0.4))) * amp;
            s.Excite = Math.Max(0, s.Excite - 0.03);
            Canvas.SetLeft(s.Root, Math.Clamp(s.X, 0, Math.Max(0, w - sw)));
            Canvas.SetTop(s.Root, Math.Clamp(s.Y, 0, Math.Max(0, h - sh)));
        }
        if (_food is not null && _sprites.All(s => !s.Eating)) ClearFood();
    }

    private void Cuddle(Sprite s)
    {
        s.Excite = 1; s.Pause = 30;
        Heart(s, "💖");
    }

    private void Heart(Sprite s, string glyph)
    {
        var t = new TextBlock { Text = glyph, FontSize = 22, FontFamily = new FontFamily("Segoe UI Emoji"), IsHitTestVisible = false };
        double left = Canvas.GetLeft(s.Root) + 8, top = Canvas.GetTop(s.Root) - 6;
        Canvas.SetLeft(t, left); Canvas.SetTop(t, top);
        _field.Children.Add(t);
        var dur = new Duration(TimeSpan.FromMilliseconds(900));
        var fade = new DoubleAnimation(1, 0, dur);
        fade.Completed += (_, _) => _field.Children.Remove(t);
        t.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, top - 50, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        t.BeginAnimation(OpacityProperty, fade);
    }

    private void Feed()
    {
        if (_sprites.Count == 0 || _field.ActualWidth < 40) return;
        ClearFood();
        double w = _field.ActualWidth, h = _field.ActualHeight;
        _food = new Point(Rng.NextDouble() * Math.Max(1, w - 60), 10 + Rng.NextDouble() * Math.Max(1, h - 50));
        _foodVisual = new TextBlock { Text = "🍖", FontSize = 28, FontFamily = new FontFamily("Segoe UI Emoji"), IsHitTestVisible = false };
        Canvas.SetLeft(_foodVisual, _food.Value.X); Canvas.SetTop(_foodVisual, _food.Value.Y);
        _field.Children.Add(_foodVisual);
        foreach (var s in _sprites)
        {
            s.TX = Math.Clamp(_food.Value.X + Rng.Next(-30, 30), 0, w - 60);
            s.TY = Math.Clamp(_food.Value.Y + Rng.Next(-20, 20), 0, h - 70);
            s.Pause = 0; s.Eating = true;
        }
    }

    private void ClearFood()
    {
        if (_foodVisual is not null) _field.Children.Remove(_foodVisual);
        _foodVisual = null; _food = null;
    }
}
