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
    private readonly Canvas _field = new() { ClipToBounds = true, MinHeight = 300, Height = 320 };
    private readonly StackPanel _list = new();
    private readonly TextBox _name = new() { MaxLength = 20, Width = 170, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _species = new() { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _custom = new() { MaxLength = 8, Width = 90, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
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
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "🐾 " + L("Pets", "Pets"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(new TextBlock
        {
            Text = L("Adicione bichinhos com nome para fazer companhia enquanto você espera, por exemplo durante um diagnóstico do computador (ele continua rodando em segundo plano). Clique em um pet para fazer carinho.",
                     "Add named pets to keep you company while you wait, for example during a computer diagnostic (it keeps running in the background). Click a pet to cuddle it."),
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 10),
        });
        root.Children.Add(_status);

        var add = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _species.Items.Clear();
        foreach (var s in Species) _species.Items.Add($"{s.Emoji}  {L(s.Pt, s.En)}");
        if (_species.SelectedIndex < 0) _species.SelectedIndex = 0;
        _name.ToolTip = L("Nome do pet", "Pet name");
        _custom.ToolTip = L("Emoji personalizado (opcional): cole ou digite qualquer emoji para usar no lugar da espécie", "Custom emoji (optional): paste or type any emoji to use instead of the species");
        _name.KeyDown -= OnNameKey; _name.KeyDown += OnNameKey;
        add.Children.Add(Label(L("Nome", "Name"))); add.Children.Add(Detach(_name));
        add.Children.Add(Label(L("Espécie", "Species"))); add.Children.Add(Detach(_species));
        add.Children.Add(Label(L("Emoji próprio", "Own emoji"))); add.Children.Add(Detach(_custom));
        add.Children.Add(MakeButton("➕  " + L("Adicionar", "Add"), OnAdd, true));
        root.Children.Add(add);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(MakeButton("🍖  " + L("Alimentar", "Feed"), (_, _) => Feed(), false));
        root.Children.Add(actions);

        var frame = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = Detach(_field), Margin = new Thickness(0, 0, 0, 12) };
        frame.SetResourceReference(Border.BackgroundProperty, "CardBg");
        frame.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        root.Children.Add(frame);

        root.Children.Add(new TextBlock { Text = L("Seus pets", "Your pets"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(Detach(_list));

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root, Padding = new Thickness(0, 0, 12, 0) };
        RefreshList();
        if (IsLoaded) { _field.Children.Clear(); _sprites.Clear(); SyncSprites(); }
    }

    private static T Detach<T>(T el) where T : FrameworkElement
    {
        if (el.Parent is Panel p) p.Children.Remove(el);
        else if (el.Parent is Decorator d) d.Child = null;
        return el;
    }

    private static TextBlock Label(string t) => new() { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Opacity = 0.8 };

    private static Button MakeButton(string text, RoutedEventHandler click, bool primary)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Arrow };
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
        string emoji = _custom.Text.Trim();
        if (emoji.Length == 0) emoji = Species[Math.Max(0, _species.SelectedIndex)].Emoji;
        _pets.Add(new PetData(name, emoji));
        PetStore.Save(_pets);
        _name.Clear(); _custom.Clear();
        RefreshList();
        SyncSprites();
    }

    private void RefreshList()
    {
        _list.Children.Clear();
        if (_pets.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = L("Nenhum pet ainda. Dê um nome e adicione o primeiro!", "No pets yet. Give it a name and add the first one!"), Opacity = 0.7 });
            return;
        }
        var wrap = new WrapPanel();
        foreach (var p in _pets.ToList())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = p.Emoji, FontSize = 18, FontFamily = new FontFamily("Segoe UI Emoji"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var del = new Button { Content = "🗑", ToolTip = L("Remover pet", "Remove pet"), Padding = new Thickness(6, 2, 6, 2), Cursor = Cursors.Arrow };
            if (Application.Current.TryFindResource("PillButton") is Style st) del.Style = st;
            del.Click += (_, _) => { _pets.Remove(p); PetStore.Save(_pets); RefreshList(); SyncSprites(); };
            row.Children.Add(del);
            var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(0, 0, 8, 8), Child = row };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            wrap.Children.Add(card);
        }
        _list.Children.Add(wrap);
    }

    // ---------- playground ----------

    private void SyncSprites()
    {
        foreach (var sp in _sprites.Where(s => !_pets.Contains(s.Data)).ToList()) { _field.Children.Remove(sp.Root); _sprites.Remove(sp); }
        double w = Math.Max(200, _field.ActualWidth > 0 ? _field.ActualWidth : 600), h = _field.Height;
        foreach (var p in _pets.Where(p => _sprites.All(s => s.Data != p)))
        {
            var flip = new ScaleTransform(1, 1);
            var hop = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(flip); group.Children.Add(hop);
            var face = new TextBlock { Text = p.Emoji, FontSize = 40, FontFamily = new FontFamily("Segoe UI Emoji"), RenderTransform = group, RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Center };
            var label = new TextBlock { Text = p.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(6, 1, 6, 1) };
            var tag = new Border { CornerRadius = new CornerRadius(8), Child = label, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.9 };
            tag.SetResourceReference(Border.BackgroundProperty, "CardBorder");
            var root = new StackPanel { Cursor = Cursors.Arrow, Background = Brushes.Transparent };
            root.Children.Add(tag); root.Children.Add(face);
            var sp = new Sprite
            {
                Data = p, Root = root, Face = face, Flip = flip, Hop = hop,
                X = Rng.NextDouble() * (w - 80), Y = 30 + Rng.NextDouble() * (h - 110),
                Speed = 1.2 + Rng.NextDouble() * 1.6, Phase = Rng.NextDouble() * 6,
            };
            sp.TX = sp.X; sp.TY = sp.Y;
            root.MouseLeftButtonDown += (_, e) => { Cuddle(sp); e.Handled = true; };
            ToolTipService.SetToolTip(root, p.Name);
            _field.Children.Add(root);
            _sprites.Add(sp);
            Canvas.SetLeft(root, sp.X); Canvas.SetTop(root, sp.Y);
        }
    }

    private void Tick()
    {
        var st = Views.DiagnosticsView.Status;
        _status.Text = st is null ? "" : "🩺 " + L("Diagnóstico em andamento: ", "Diagnostic in progress: ") + st;
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
                    s.TY = 20 + Rng.NextDouble() * Math.Max(1, h - sh - 20);
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
