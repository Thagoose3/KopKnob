using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Knob.App.Platform;
using Knob.App.Tiles;
using Knob.Core.Calc;
using Knob.Core.Storage;

namespace Knob.App.Tools;

/// <summary>
/// Full-panel calculator: live result while typing; Enter saves to history (calc-history.json) and copies the result.
/// Clicking a history entry puts its expression back in the box.
/// </summary>
public sealed class CalculatorTool(string historyPath)
{
    private readonly JsonStore<CalcHistory> _store = new(historyPath);
    private CalcHistory? _history;

    private CalcHistory History => _history ??= _store.Load();

    public FrameworkElement CreateView()
    {
        var input = new TextBox { FontSize = 18, Padding = new Thickness(8, 6, 8, 6) };
        var result = Ui.Text("", 24, Ui.Accent, FontWeights.SemiBold);
        result.Margin = new Thickness(4, 10, 0, 0);
        var hint = Ui.Text("Enter = save + copy", 10, Ui.Dim);
        hint.Margin = new Thickness(4, 6, 0, 0);

        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(Ui.WithHint(input, "e.g. (1200 × 7%) + 15"));
        left.Children.Add(result);
        left.Children.Add(hint);

        var historyList = new StackPanel();
        var right = new DockPanel { Width = 250 };
        var title = Ui.Title("History");
        title.Margin = new Thickness(0, 0, 0, 4);
        DockPanel.SetDock(title, Dock.Top);
        right.Children.Add(title);
        right.Children.Add(new ScrollViewer { Content = historyList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false });

        var root = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);
        root.Children.Add(left);

        void Evaluate()
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                result.Text = "";
                return;
            }
            if (ExpressionEvaluator.TryEvaluate(input.Text, out var v, out var err))
            {
                result.Text = "= " + CalcHistory.Format(v);
                result.Foreground = Ui.Accent;
            }
            else
            {
                result.Text = err;
                result.Foreground = Ui.Subtle;
            }
        }

        void RefreshHistory()
        {
            historyList.Children.Clear();
            foreach (var e in History.Entries)
            {
                var expr = e.Expression;
                var row = new Border
                {
                    Padding = new Thickness(6, 3, 6, 3),
                    CornerRadius = new CornerRadius(6),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = "Use again",
                };
                var line = new DockPanel();
                var res = Ui.Text(e.Result, 11, Ui.Fg, FontWeights.SemiBold);
                DockPanel.SetDock(res, Dock.Right);
                line.Children.Add(res);
                line.Children.Add(new TextBlock { Text = expr, FontSize = 11, Foreground = Ui.Subtle, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) });
                row.Child = line;
                row.MouseEnter += (_, _) => row.Background = Ui.Control;
                row.MouseLeave += (_, _) => row.Background = System.Windows.Media.Brushes.Transparent;
                row.MouseLeftButtonUp += (_, _) =>
                {
                    input.Text = expr;
                    input.CaretIndex = input.Text.Length;
                    input.Focus();
                };
                historyList.Children.Add(row);
            }
        }

        input.TextChanged += (_, _) => Evaluate();
        input.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (!ExpressionEvaluator.TryEvaluate(input.Text, out var v, out string _)) return;
            var formatted = CalcHistory.Format(v);
            History.Add(input.Text, formatted);
            try { _store.Save(History); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            RefreshHistory();
            // Copy without thousands separators so it pastes as a number anywhere.
            if (await ClipboardUtil.TrySetTextAsync(v.ToString("0.############################", CultureInfo.InvariantCulture)))
                hint.Text = "Copied " + formatted;
        };

        root.Loaded += (_, _) =>
        {
            RefreshHistory();
            // Focus once the window has been activated.
            root.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { input.Focus(); Keyboard.Focus(input); });
        };
        return root;
    }
}
