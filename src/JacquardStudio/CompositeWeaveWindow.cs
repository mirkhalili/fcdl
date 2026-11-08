using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace JacquardStudio;

/// <summary>Independent weft designs and their editable, interleaved composite.</summary>
public sealed class CompositeWeaveWindow : Window
{
    private readonly WeaveBank bank;
    private readonly IReadOnlyList<BankItem> choices;
    private readonly bool english;
    private readonly TextBox countBox = new() { Text = "2", Width = 55, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox nameBox = new() { Width = 180 };
    private readonly StackPanel rows = new();
    private readonly ComboBox source = new() { MinWidth = 190, DisplayMemberPath = "Name" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3) };
    private readonly WeaveEditor editor = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
    private WeavePattern?[] parts = Array.Empty<WeavePattern?>();
    private string[] names = Array.Empty<string>();
    private bool updating, valid, changed;
    public BankItem? SavedItem { get; private set; }
    private string T(string fa, string en) => english ? en : fa;
    public CompositeWeaveWindow(WeaveBank bank, IReadOnlyList<BankItem> choices, bool english)
    {
        this.bank = bank; this.choices = choices; this.english = english;
        Title = T("ساخت و تجزیه بافت ترکیبی", "Create and split a composite weave"); Width = 1100; Height = 760; MinWidth = 760; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = T("تعداد پود:", "Wefts:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }); top.Children.Add(countBox);
        top.Children.Add(Button(T("اعمال تعداد", "Set count"), (_, _) => SetCount()));
        source.ItemsSource = choices; if (choices.Count > 0) source.SelectedIndex = 0;
        top.Children.Add(new TextBlock { Text = T("بافت بانک برای تجزیه:", "Bank weave to split:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 4, 4, 4) }); top.Children.Add(source);
        top.Children.Add(Button(T("تجزیه", "Split"), (_, _) => SplitSelected()));
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); bottom.Children.Add(status);
        var actions = new WrapPanel(); bottom.Children.Add(actions);
        actions.Children.Add(new TextBlock { Text = T("نام نتیجه:", "Result name:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }); actions.Children.Add(nameBox);
        actions.Children.Add(Button(T("ذخیره نتیجه در بانک", "Save result to bank"), (_, _) => Save()));
        actions.Children.Add(Button("3D", (_, _) => { if (valid) Fabric3DWindow.ShowPattern(this, editor.Snapshot(), english); }));
        actions.Children.Add(Button(T("بازگردانی", "Undo"), (_, _) => editor.Undo())); actions.Children.Add(Button(T("انجام دوباره", "Redo"), (_, _) => editor.Redo()));
        actions.Children.Add(Button(T("بستن", "Close"), (_, _) => Close()));
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(grid);
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 10, 0) }; Grid.SetColumn(scroll, 0); grid.Children.Add(scroll);
        var editArea = new DockPanel(); Grid.SetColumn(editArea, 1); grid.Children.Add(editArea);
        var explanation = new TextBlock { Text = T("ویرایش تصویر نتیجه: هر خانه را کلیک کنید. تغییرات به بافت پود مربوطه منتقل می‌شوند.", "Edit the result by clicking cells. Changes are reflected in the corresponding weft."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5) }; DockPanel.SetDock(explanation, Dock.Top); editArea.Children.Add(explanation);
        editArea.Children.Add(new ScrollViewer { Content = editor, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.LeftToRight });
        editor.Changed += (_, _) => { editor.SizeForScrolling(); if (!updating && valid) { var split = CompositeWeave.Split(editor.Snapshot(), parts.Length); for (int i = 0; i < parts.Length; i++) parts[i] = split[i]; changed = true; } };
        parts = new WeavePattern?[2]; names = new string[2]; BuildRows(); editor.SizeForScrolling(); Status();
        Closing += (_, e) => { if (changed && SavedItem is null && MessageBox.Show(this, T("تغییرات ذخیره نشده‌اند. پنجره بسته شود؟", "Discard unsaved composite changes?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true; };
    }
    private static Button Button(string text, RoutedEventHandler handler) { var b = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) }; b.Click += handler; return b; }
    private void Status(string? error = null) => status.Text = error ?? (valid ? T($"نتیجه: {editor.PatternWidth} × {editor.PatternHeight} خانه. هر بافت تا دوره مشترک تکرار شده است.", $"Result: {editor.PatternWidth} × {editor.PatternHeight} cells. Each weave repeats to the common period.") : T("برای هر پود یک بافت انتخاب یا ترسیم کنید، یا یک بافت از بانک تجزیه کنید.", "Choose or draw one weave per weft, or split a bank weave."));
    private bool Count(out int count)
    {
        if (NumberInput.TryInt(countBox.Text, out count) && count is >= 1 and <= CompositeWeave.MaxWefts) return true;
        MessageBox.Show(this, T("تعداد پود باید بین ۱ و ۲۵۶ باشد.", "Weft count must be between 1 and 256."), Title); return false;
    }
    private bool ConfirmReplace() => !changed || MessageBox.Show(this, T("طرح فعلی جایگزین شود؟", "Replace the current unsaved design?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void SetCount()
    {
        if (!Count(out int count) || count == parts.Length || !ConfirmReplace()) return;
        parts = new WeavePattern?[count]; names = new string[count]; valid = false; changed = true; BuildRows(); Status();
    }
    private void SplitSelected()
    {
        if (!Count(out int count) || source.SelectedItem is not BankItem item) return;
        if (item.Pattern.Height < count || item.Pattern.Height % count != 0) { MessageBox.Show(this, T("تجزیه ممکن نیست: ارتفاع بافت باید مضرب تعداد پود باشد.", "Cannot split: the weave height must be a multiple of the weft count."), Title); return; }
        try
        {
            var split = CompositeWeave.Split(item.Pattern, count);
            if (!ConfirmReplace()) return;
            parts = split.Cast<WeavePattern?>().ToArray(); names = Enumerable.Range(1, count).Select(i => $"{item.Name} — {i}").ToArray();
            BuildRows(); Recombine(); changed = true;
        }
        catch (ArgumentException ex) { MessageBox.Show(this, T("تجزیه ممکن نیست: ", "Cannot split: ") + ex.Message, Title); }
    }
    private void BuildRows()
    {
        rows.Children.Clear();
        for (int i = 0; i < parts.Length; i++)
        {
            int n = i; var panel = new StackPanel { Margin = new Thickness(3, 3, 3, 8) };
            panel.Children.Add(new TextBlock { Text = T($"پود {i + 1}: ", $"Weft {i + 1}: ") + (string.IsNullOrEmpty(names[i]) ? T("انتخاب نشده", "not selected") : names[i]), TextWrapping = TextWrapping.Wrap });
            var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", Margin = new Thickness(0, 3, 0, 0) };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is BankItem selected) { parts[n] = selected.Pattern; names[n] = selected.Name; changed = true; BuildRows(); Recombine(); } }; panel.Children.Add(combo);
            var tools = new WrapPanel(); panel.Children.Add(tools);
            tools.Children.Add(Button(T("ترسیم جدید", "New drawing"), (_, _) => { var blank = new WeavePattern(8, 8, Enumerable.Repeat((byte)255, 64).ToArray()); EditPart(n, blank); }));
            tools.Children.Add(Button(T("ویرایش", "Edit"), (_, _) => { if (parts[n] is { } p) EditPart(n, p); }));
            tools.Children.Add(Button("3D", (_, _) => { if (parts[n] is { } p) Fabric3DWindow.ShowPattern(this, p, english); })); rows.Children.Add(panel);
        }
    }
    private void EditPart(int index, WeavePattern pattern)
    {
        if (pattern.Width > WeaveEditor.MaxSide || pattern.Height > WeaveEditor.MaxSide) { MessageBox.Show(this, T("هر بعد ویرایشگر حداکثر ۴۰۹۶ خانه است.", "Editor dimensions are limited to 4096 cells."), Title); return; }
        var dialog = new Window { Owner = this, Title = T($"ویرایش پود {index + 1}", $"Edit weft {index + 1}"), Width = 720, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = FlowDirection };
        var root = new DockPanel { Margin = new Thickness(10) }; dialog.Content = root;
        var bar = new WrapPanel(); DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var width = new TextBox { Width = 60, Text = pattern.Width.ToString(), FlowDirection = FlowDirection.LeftToRight }; var height = new TextBox { Width = 60, Text = pattern.Height.ToString(), FlowDirection = FlowDirection.LeftToRight };
        bar.Children.Add(new TextBlock { Text = T("عرض", "Width"), Margin = new Thickness(4) }); bar.Children.Add(width); bar.Children.Add(new TextBlock { Text = T("ارتفاع", "Height"), Margin = new Thickness(4) }); bar.Children.Add(height);
        var canvas = new WeaveEditor { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight }; canvas.SetPattern(pattern); canvas.Changed += (_, _) => canvas.SizeForScrolling(); canvas.SizeForScrolling();
        bar.Children.Add(Button(T("شبکه جدید", "New grid"), (_, _) => { if (NumberInput.TryInt(width.Text, out int w) && NumberInput.TryInt(height.Text, out int h)) { try { canvas.Reset(w, h); } catch (ArgumentOutOfRangeException) { MessageBox.Show(dialog, T("ابعاد بیش از حد مجاز است.", "Dimensions exceed the editor limit.")); } } }));
        bar.Children.Add(Button(T("بازگردانی", "Undo"), (_, _) => canvas.Undo())); bar.Children.Add(Button(T("انجام دوباره", "Redo"), (_, _) => canvas.Redo()));
        bar.Children.Add(Button("3D", (_, _) => Fabric3DWindow.ShowPattern(dialog, canvas.Snapshot(), english)));
        bar.Children.Add(Button(T("اعمال", "Apply"), (_, _) => { dialog.DialogResult = true; }));
        root.Children.Add(new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.LeftToRight });
        if (dialog.ShowDialog() == true) { parts[index] = canvas.Snapshot(); names[index] = T("بافت ویرایش‌شده", "Edited weave"); changed = true; BuildRows(); Recombine(); }
    }
    private void Recombine()
    {
        valid = false;
        if (parts.Any(p => p is null)) { Status(); return; }
        try { var result = CompositeWeave.Combine(parts.Select(p => p!).ToArray()); updating = true; try { editor.SetPattern(result); } finally { updating = false; } valid = true; Status(); }
        catch (ArgumentException ex) { Status(T("ترکیب ممکن نیست: ", "Cannot combine: ") + ex.Message); }
    }
    private void Save()
    {
        if (!valid) { MessageBox.Show(this, T("ابتدا بافت ترکیبی معتبر بسازید.", "First create a valid composite weave."), Title); return; }
        try { SavedItem = bank.SaveNew(nameBox.Text, editor.Snapshot()); changed = false; DialogResult = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { MessageBox.Show(this, T("ذخیره ممکن نیست: ", "Cannot save: ") + ex.Message, Title); }
    }
}
