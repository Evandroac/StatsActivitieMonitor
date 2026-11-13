using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StatsMonitor.Views;

/// <summary>Numeric input with spinner buttons, arrow keys, mouse wheel and clamping.</summary>
public partial class NumericBox : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumericBox),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericBox), new PropertyMetadata(0.0));
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericBox), new PropertyMetadata(100.0));
    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumericBox), new PropertyMetadata(1.0));

    public NumericBox()
    {
        InitializeComponent();
        Loaded += (_, _) => ShowValue();
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((NumericBox)d).ShowValue();

    private void ShowValue() => Box.Text = Value.ToString("0.##", CultureInfo.CurrentCulture);

    private void Commit()
    {
        if (double.TryParse(Box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v))
            Value = Math.Clamp(v, Minimum, Maximum);
        ShowValue();
    }

    private void Nudge(int direction)
    {
        Commit();
        Value = Math.Clamp(Math.Round(Value + direction * Step, 4), Minimum, Maximum);
    }

    private void OnUp(object sender, RoutedEventArgs e) => Nudge(1);
    private void OnDown(object sender, RoutedEventArgs e) => Nudge(-1);
    private void OnLostFocus(object sender, RoutedEventArgs e) => Commit();

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Box.IsKeyboardFocused) return;
        Nudge(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: Nudge(1); e.Handled = true; break;
            case Key.Down: Nudge(-1); e.Handled = true; break;
            case Key.Enter: Commit(); break;
        }
    }

    private static bool IsNumeric(string s)
    {
        var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        return s.All(c => char.IsDigit(c) || sep.Contains(c));
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !IsNumeric(e.Text);

    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.Text) is not string s || !IsNumeric(s)) e.CancelCommand();
    }
}
