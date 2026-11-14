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

    private static readonly (string Pt, string En, Color A, Color B)[] Backgrounds =
    [
        ("Padrão", "Default", Colors.Transparent, Colors.Transparent),
        ("Gramado", "Meadow", Color.FromRgb(0xA8, 0xE0, 0xFF), Color.FromRgb(0x5B, 0xB3, 0x4A)),
        ("Noite estrelada", "Starry night", Color.FromRgb(0x0B, 0x10, 0x2E), Color.FromRgb(0x2B, 0x2F, 0x6B)),
        ("Praia", "Beach", Color.FromRgb(0x8E, 0xD8, 0xF8), Color.FromRgb(0xF2, 0xD9, 0x9B)),
        ("Neve", "Snow", Color.FromRgb(0xC9, 0xE4, 0xF5), Color.FromRgb(0xFA, 0xFC, 0xFF)),
        ("Pôr do sol", "Sunset", Color.FromRgb(0xFF, 0x7E, 0x5F), Color.FromRgb(0x7F, 0x53, 0xAC)),
    ];

    private void OnBgChanged(object sender, SelectionChangedEventArgs e) { PetStore.SaveBackground(_bg.SelectedIndex); ApplyBackground(); }

    private void ApplyBackground()
    {
        if (_frame == null) return;
        int i = Math.Clamp(_bg.SelectedIndex, 0, Backgrounds.Length - 1);
        if (i == 0) { _frame.SetResourceReference(Border.BackgroundProperty, "CardBg"); return; }
        _frame.ClearValue(Border.BackgroundProperty);
        _frame.Background = new LinearGradientBrush(Backgrounds[i].A, Backgrounds[i].B, 90);
    }

    private static readonly Random Rng = new();
    private readonly List<PetData> _pets = PetStore.Load();
    private readonly List<Sprite> _sprites = [];
    private readonly Canvas _field = new() { ClipToBounds = true, Height = 96 };
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
        Loaded += (_, _) => SyncSprites();
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) { SyncSprites(); _timer.Start(); } else _timer.Stop(); };
        if (IsVisible) _timer.Start();
        Loc.LanguageChanged += () => { if (IsLoaded) Build(); };
    }

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var feed = MakeButton("🍖", (_, _) => Feed(), false);
        feed.ToolTip = L("Alimentar os pets", "Feed the pets");
        var fold = MakeButton(_collapsed ? "▸" : "▾", null!, false);
        fold.ToolTip = L("Esconder / mostrar a área dos pets", "Hide / show the pets area");
        fold.Margin = new Thickness(0, 0, 4, 0);
        fold.Click += (_, _) => { _collapsed = !_collapsed; PetStore.SaveCollapsed(_collapsed); Build(); };
        DockPanel.SetDock(fold, Dock.Left);
        DockPanel.SetDock(feed, Dock.Right);
        head.Children.Add(fold);
        if (!_collapsed) head.Children.Add(feed);
        head.Children.Add(new TextBlock { Text = "🐾 Pets", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(head);
        root.Children.Add(_status);
        if (_collapsed) { Content = root; _timer.Stop(); return; }
        if (IsVisible) _timer.Start();

        var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = Detach(_field), Margin = new Thickness(0, 0, 0, 6) };
        _frame = frame;
        frame.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        root.Children.Add(frame);

        _species.Items.Clear();
        foreach (var s in Species) _species.Items.Add(new ComboBoxItem { Content = $"{s.Emoji}  {L(s.Pt, s.En)}", FontFamily = new FontFamily("Segoe UI Emoji,Segoe UI") });
        if (_species.SelectedIndex < 0) _species.SelectedIndex = 0;
        _name.ToolTip = L("Nome do pet (Enter adiciona). Botão direito em um pet o remove.", "Pet name (Enter adds). Right-click a pet to remove it.");
        _name.KeyDown -= OnNameKey; _name.KeyDown += OnNameKey;
        _bg.Items.Clear();
        foreach (var b in Backgrounds) _bg.Items.Add(new ComboBoxItem { Content = "🖼  " + L(b.Pt, b.En) });
        _bg.SelectionChanged -= OnBgChanged;
        _bg.SelectedIndex = Math.Clamp(PetStore.LoadBackground(), 0, Backgrounds.Length - 1);
        _bg.SelectionChanged += OnBgChanged;
        _bg.ToolTip = L("Plano de fundo dos pets", "Pets background");
        ApplyBackground();
        var hint = new TextBlock { Text = L("Nome do pet…", "Pet name…"), Opacity = 0.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        _name.TextChanged -= OnNameChanged; _name.TextChanged += OnNameChanged;
        _hint = hint;
        var nameBox = new Grid();
        nameBox.Children.Add(Detach(_name));
        nameBox.Children.Add(hint);
        var plus = MakeButton("➕", OnAdd, true);
        plus.ToolTip = L("Adicionar pet", "Add pet");
        _species.Width = 104; _species.Margin = new Thickness(4, 0, 4, 0);
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 40 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(Detach(_species), 1); Grid.SetColumn(plus, 2);
        row.Children.Add(nameBox); row.Children.Add(_species); row.Children.Add(plus);
        root.Children.Add(row);
        root.Children.Add(Detach(_bg));
        Content = root;
        if (IsLoaded) { _field.Children.Clear(); _sprites.Clear(); SyncSprites(); }
    }
    private static T Detach<T>(T el) where T : FrameworkElement
    {
        if (el.Parent is Panel p) p.Children.Remove(el);
        else if (el.Parent is Decorator d) d.Child = null;
        return el;
    }

        private static Button MakeButton(string text, RoutedEventHandler click, bool primary)
    {
        var b = new Button { Content = text, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0), Cursor = Cursors.Arrow };
        if (Application.Current.TryFindResource(primary ? "PrimaryButton" : "PillButton") is Style st) b.Style = st;
        b.Click += click;
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
        foreach (var sp in _sprites.Where(s => !_pets.Contains(s.Data)).ToList()) { _field.Children.Remove(sp.Root); _sprites.Remove(sp); }
        double w = Math.Max(120, _field.ActualWidth > 0 ? _field.ActualWidth : 160), h = _field.Height;
        foreach (var p in _pets.Where(p => _sprites.All(s => s.Data != p)))
        {
            var flip = new ScaleTransform(1, 1);
            var hop = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(flip); group.Children.Add(hop);
            var face = new TextBlock { Text = p.Emoji, FontSize = 24, FontFamily = new FontFamily("Segoe UI Emoji"), RenderTransform = group, RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center };
            var label = new TextBlock { Text = p.Name, FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
            var tag = new Border { CornerRadius = new CornerRadius(8), Child = label, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.9 };
            tag.SetResourceReference(Border.BackgroundProperty, "CardBorder");
            var root = new StackPanel { Cursor = Cursors.Arrow, Background = Brushes.Transparent };
            root.Children.Add(tag); root.Children.Add(face);
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
