using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
    private string[] colors = Array.Empty<string>();
    private readonly List<Image> previews = new();
    private static readonly string[] DefaultPalette = { "#58B7D3", "#E49E64", "#BD86D2", "#73C59D", "#DB7792", "#D4C66E" };
    private static string[] DefaultColors(int count) => Enumerable.Range(0, count).Select(i => DefaultPalette[i % DefaultPalette.Length]).ToArray();
    private bool updating, valid, changed;
    public BankItem? SavedItem { get; private set; }
    private string T(string fa, string en) => english ? en : fa;
    private static Border Panel(UIElement child, int padding = 16)
    {
        var border = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(padding), BorderThickness = new Thickness(1), Child = child };
        border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Border"); return border;
    }
    private static TextBlock Heading(string text, double size = 16) => new() { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Muted(string text) { var textBlock = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), FontSize = 12 }; textBlock.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return textBlock; }
    public CompositeWeaveWindow(WeaveBank bank, IReadOnlyList<BankItem> choices, bool english)
    {
        this.bank = bank; this.choices = choices; this.english = english;
        Title = T("طراحی بافت ترکیبی", "Composite weave studio"); Width = 1250; Height = 820; MinWidth = 850; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft; SetResourceReference(BackgroundProperty, "AppBackground");
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(Heading(T("استودیوی بافت ترکیبی", "Composite weave studio"), 23)); heading.Children.Add(Muted(T("پودها را طراحی کنید و نتیجه رنگی را هم‌زمان ببینید.", "Design each weft and see the colored result live."))); header.Children.Add(heading);
        var close = Button(T("بستن", "Close"), (_, _) => Close()); Grid.SetColumn(close, 1); header.Children.Add(close); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var main = new Grid(); main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440), MinWidth = 320 }); main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 330 }); root.Children.Add(main);
        var left = new DockPanel(); Grid.SetColumn(left, 0); main.Children.Add(left);
        var configuration = new StackPanel(); configuration.Children.Add(Heading(T("۱  ·  پودها", "1  ·  Wefts"))); configuration.Children.Add(Muted(T("تعداد را مشخص کنید؛ برای هر پود طرح و رنگ جداگانه انتخاب کنید.", "Set the count and choose a design and color for each weft.")));
        var countLine = new WrapPanel(); countLine.Children.Add(new TextBlock { Text = T("تعداد پود", "Weft count"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5) }); countBox.Padding = new Thickness(6); countLine.Children.Add(countBox); countLine.Children.Add(Button(T("اعمال تعداد", "Set count"), (_, _) => SetCount())); configuration.Children.Add(countLine);
        var configPanel = Panel(configuration); configPanel.Margin = new Thickness(0, 0, 0, 10); DockPanel.SetDock(configPanel, Dock.Top); left.Children.Add(configPanel);
        var splitPanel = new StackPanel(); splitPanel.Children.Add(Heading(T("تجزیه از بانک", "Split from bank"), 14)); splitPanel.Children.Add(Muted(T("بافت را انتخاب و به تعداد پود بالا تجزیه کنید.", "Split a selected bank weave into the count above.")));
        source.ItemsSource = choices; if (choices.Count > 0) source.SelectedIndex = 0; splitPanel.Children.Add(source); splitPanel.Children.Add(Button(T("تجزیه بافت انتخابی", "Split selected weave"), (_, _) => SplitSelected()));
        var splitFrame = Panel(splitPanel); splitFrame.Margin = new Thickness(0, 10, 0, 0); DockPanel.SetDock(splitFrame, Dock.Bottom); left.Children.Add(splitFrame);
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; left.Children.Add(scroll);
        var right = new Grid(); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn(right, 2); main.Children.Add(right);
        var resultHeader = new StackPanel(); resultHeader.Children.Add(Heading(T("۲  ·  پیش‌نمایش ترکیب", "2  ·  Composite preview"))); resultHeader.Children.Add(Muted(T("هر خانه قابل ویرایش است؛ رنگ پودها در تصویر و نمای سه‌بعدی یکسان است.", "Click cells to edit. The preview and 3D use the same weft colors.")));
        resultHeader.Children.Add(status); var resultTop = Panel(resultHeader); resultTop.Margin = new Thickness(0, 0, 0, 10); right.Children.Add(resultTop);
        var editArea = new ScrollViewer { Content = editor, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.LeftToRight };
        var editFrame = Panel(editArea, 10); Grid.SetRow(editFrame, 1); right.Children.Add(editFrame);
        var footer = new StackPanel(); var tools = new WrapPanel(); footer.Children.Add(tools);
        tools.Children.Add(Button(T("نمای سه‌بعدی", "3D view"), (_, _) => { if (valid) Fabric3DWindow.ShowPattern(this, editor.Snapshot(), english, colors); }));
        tools.Children.Add(Button(T("بازگردانی", "Undo"), (_, _) => editor.Undo())); tools.Children.Add(Button(T("انجام دوباره", "Redo"), (_, _) => editor.Redo()));
        var saveLine = new WrapPanel(); saveLine.Children.Add(new TextBlock { Text = T("نام بافت", "Weave name"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }); nameBox.Padding = new Thickness(7); saveLine.Children.Add(nameBox); saveLine.Children.Add(Button(T("ذخیره در بانک", "Save to bank"), (_, _) => Save())); footer.Children.Add(saveLine);
        var footerFrame = Panel(footer); footerFrame.Margin = new Thickness(0, 10, 0, 0); Grid.SetRow(footerFrame, 2); right.Children.Add(footerFrame);
        editor.Changed += (_, _) => { editor.SizeForScrolling(); if (!updating && valid) { var split = CompositeWeave.Split(editor.Snapshot(), parts.Length); for (int i = 0; i < parts.Length; i++) parts[i] = split[i]; UpdatePreviews(); changed = true; } };
        parts = new WeavePattern?[2]; names = new string[2]; colors = DefaultColors(2); editor.SetRowColors(colors); BuildRows(); editor.SizeForScrolling(); Status();
        Closing += (_, e) => { if (changed && SavedItem is null && MessageBox.Show(this, T("تغییرات ذخیره نشده‌اند. پنجره بسته شود؟", "Discard unsaved composite changes?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true; };
    }
    private static Button Button(string text, RoutedEventHandler handler) { var b = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) }; b.Click += handler; return b; }
    private void Status(string? error = null)
    {
        editor.IsEnabled = valid; editor.Opacity = valid ? 1 : .35;
        status.Text = error ?? (valid ? T($"نتیجه: {editor.PatternWidth} × {editor.PatternHeight} خانه. هر بافت تا دوره مشترک تکرار شده است.", $"Result: {editor.PatternWidth} × {editor.PatternHeight} cells. Each weave repeats to the common period.") : T("برای هر پود یک بافت انتخاب یا ترسیم کنید، یا یک بافت از بانک تجزیه کنید.", "Choose or draw one weave per weft, or split a bank weave."));
    }
    private bool Count(out int count)
    {
        if (NumberInput.TryInt(countBox.Text, out count) && count is >= 1 and <= CompositeWeave.MaxWefts) return true;
        MessageBox.Show(this, T("تعداد پود باید بین ۱ و ۲۵۶ باشد.", "Weft count must be between 1 and 256."), Title); return false;
    }
    private bool ConfirmReplace() => !changed || MessageBox.Show(this, T("طرح فعلی جایگزین شود؟", "Replace the current unsaved design?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void SetCount()
    {
        if (!Count(out int count) || count == parts.Length || !ConfirmReplace()) return;
        parts = new WeavePattern?[count]; names = new string[count]; colors = DefaultColors(count); editor.SetRowColors(colors); valid = false; changed = true; BuildRows(); Status();
    }
    private void SplitSelected()
    {
        if (!Count(out int count) || source.SelectedItem is not BankItem item) return;
        if (item.Pattern.Height < count || item.Pattern.Height % count != 0) { MessageBox.Show(this, T("تجزیه ممکن نیست: ارتفاع بافت باید مضرب تعداد پود باشد.", "Cannot split: the weave height must be a multiple of the weft count."), Title); return; }
        try
        {
            var split = CompositeWeave.Split(item.Pattern, count);
            if (!ConfirmReplace()) return;
            parts = split.Cast<WeavePattern?>().ToArray(); names = Enumerable.Range(1, count).Select(i => $"{item.Name} — {i}").ToArray(); colors = item.WeftColors is { Length: > 0 } savedColors && savedColors.Length == count ? (string[])savedColors.Clone() : DefaultColors(count); editor.SetRowColors(colors);
            BuildRows(); Recombine(); changed = true;
        }
        catch (ArgumentException ex) { MessageBox.Show(this, T("تجزیه ممکن نیست: ", "Cannot split: ") + ex.Message, Title); }
    }
    private static BitmapSource ColoredPreview(WeavePattern pattern, string color)
    {
        Color weft = FabricSimulation.ParseColor(color); byte[] data = new byte[checked(pattern.Values.Length * 4)];
        for (int i = 0; i < pattern.Values.Length; i++) { Color c = pattern.Values[i] == 0 ? Color.FromRgb(218, 191, 160) : weft; data[i * 4] = c.B; data[i * 4 + 1] = c.G; data[i * 4 + 2] = c.R; data[i * 4 + 3] = 255; }
        var image = BitmapSource.Create(pattern.Width, pattern.Height, 96, 96, PixelFormats.Bgra32, null, data, pattern.Width * 4); image.Freeze(); return image;
    }
    private void UpdatePreviews()
    {
        for (int i = 0; i < previews.Count && i < parts.Length; i++) previews[i].Source = parts[i] is { } pattern ? ColoredPreview(pattern, colors[i]) : null;
    }
    private void BuildRows()
    {
        rows.Children.Clear(); previews.Clear();
        for (int i = 0; i < parts.Length; i++)
        {
            int n = i; var body = new StackPanel();
            var title = new Grid(); title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.Children.Add(Heading(T($"پود {i + 1}", $"Weft {i + 1}"), 15));
            var swatch = new ColorSwatch(colors[n], T($"رنگ پود {n + 1}", $"Weft {n + 1} color")); swatch.ColorChanged += (_, _) => { colors[n] = swatch.Value; changed = true; editor.SetRowColors(colors); BuildRows(); }; Grid.SetColumn(swatch, 1); title.Children.Add(swatch); body.Children.Add(title);
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var preview = new Image { Width = 76, Height = 76, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor); previews.Add(preview);
            var thumb = new Border { Child = preview, Background = Brushes.White, CornerRadius = new CornerRadius(7), Padding = new Thickness(4), Height = 84, Width = 84 }; row.Children.Add(thumb);
            var details = new StackPanel { Margin = new Thickness(9, 0, 0, 0) }; Grid.SetColumn(details, 1); row.Children.Add(details);
            details.Children.Add(Muted(string.IsNullOrEmpty(names[i]) ? T("طرح انتخاب نشده", "No weave selected") : names[i]));
            var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", ToolTip = T("انتخاب از بانک", "Choose from bank") };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is BankItem selected) { parts[n] = selected.Pattern; names[n] = selected.Name; changed = true; BuildRows(); Recombine(); } }; details.Children.Add(combo); body.Children.Add(row);
            var tools = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; body.Children.Add(tools);
            tools.Children.Add(Button(T("ترسیم جدید", "Draw new"), (_, _) => { var blank = new WeavePattern(8, 8, Enumerable.Repeat((byte)255, 64).ToArray()); EditPart(n, blank); }));
            tools.Children.Add(Button(T("ویرایش طرح", "Edit weave"), (_, _) => { if (parts[n] is { } p) EditPart(n, p); }));
            tools.Children.Add(Button("3D", (_, _) => { if (parts[n] is { } p) Fabric3DWindow.ShowPattern(this, p, english, new[] { colors[n] }); }));
            var card = Panel(body, 12); card.Margin = new Thickness(0, 0, 0, 10); rows.Children.Add(card);
        }
        UpdatePreviews();
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
        try { SavedItem = bank.SaveNew(nameBox.Text, editor.Snapshot(), colors); changed = false; DialogResult = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { MessageBox.Show(this, T("ذخیره ممکن نیست: ", "Cannot save: ") + ex.Message, Title); }
    }
}
