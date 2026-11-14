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

    private static readonly Random Rng = new();
    private readonly List<PetData> _pets = PetStore.Load();
    private readonly List<Sprite> _sprites = [];
    private readonly Canvas _field = new() { ClipToBounds = true, Height = 96 };
    private readonly TextBox _name = new() { MaxLength = 20, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _species = new() { Width = 56, Margin = new Thickness(4, 0, 0, 0) };
    private readonly TextBlock _status = new() { Opacity = 0.8, FontSize = 11, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Point? _food;
    private TextBlock? _foodVisual;

    public PetsView()
    {
        Build();
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { SyncSprites(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        Loc.LanguageChanged += () => { if (IsLoaded) Build(); };
    }

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var feed = MakeButton("🍖", (_, _) => Feed(), false);
        feed.ToolTip = L("Alimentar os pets", "Feed the pets");
        DockPanel.SetDock(feed, Dock.Right);
        head.Children.Add(feed);
        head.Children.Add(new TextBlock { Text = "🐾 Pets", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(head);
        root.Children.Add(_status);

        var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = Detach(_field), Margin = new Thickness(0, 0, 0, 6) };
        frame.SetResourceReference(Border.BackgroundProperty, "Background");
        frame.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        root.Children.Add(frame);

        _species.Items.Clear();
        foreach (var s in Species) _species.Items.Add(new ComboBoxItem { Content = s.Emoji, ToolTip = L(s.Pt, s.En), FontFamily = new FontFamily("Segoe UI Emoji") });
        if (_species.SelectedIndex < 0) _species.SelectedIndex = 0;
        _name.ToolTip = L("Nome do pet (Enter adiciona). Botão direito em um pet o remove.", "Pet name (Enter adds). Right-click a pet to remove it.");
        _name.KeyDown -= OnNameKey; _name.KeyDown += OnNameKey;
        var add = new DockPanel();
        var plus = MakeButton("➕", OnAdd, true);
        plus.ToolTip = L("Adicionar pet", "Add pet");
        plus.Margin = new Thickness(4, 0, 0, 0);
        DockPanel.SetDock(plus, Dock.Right);
        DockPanel.SetDock(_species, Dock.Right);
        add.Children.Add(plus);
        add.Children.Add(Detach(_species));
        add.Children.Add(Detach(_name));
        root.Children.Add(add);

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
        if (w < 100 || h < 100) return;
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
        if (_sprites.Count == 0 || _field.ActualWidth < 100) return;
        ClearFood();
        double w = _field.ActualWidth, h = _field.ActualHeight;
        _food = new Point(40 + Rng.NextDouble() * (w - 120), 40 + Rng.NextDouble() * (h - 120));
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
