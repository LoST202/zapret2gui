using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Zapret.Core;

namespace Zapret.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly Brush Accent = Freeze(0x2E, 0x8B, 0xFF);
    private static readonly Brush RingOff = Freeze(0x3B, 0x42, 0x4B);
    private static readonly Brush OkBrush = Freeze(0x3F, 0xB9, 0x50);
    private static readonly Brush FailBrush = Freeze(0x9A, 0xA0, 0xA6);
    private static readonly Brush ErrorBrush = Freeze(0xE8, 0x5C, 0x5C);
    private static readonly Brush WarnBrush = Freeze(0xE8, 0xB1, 0x3B);
    private static readonly Brush OkBadgeBg = FreezeA(0x2E, 0x3F, 0xB9, 0x50);
    private static readonly Brush ErrBadgeBg = FreezeA(0x2E, 0xE8, 0x5C, 0x5C);
    private static readonly Brush NoBadgeBg = Brushes.Transparent;

    private readonly AppPaths _paths;
    private readonly AppState _state;
    private StrategyCatalog _catalog;
    private readonly WinwsRunner _runner;

    private bool _reallyExit;
    private bool _suppressSelection;
    private bool _suppressSettings;
    private bool _busy;
    private string? _startError;
    private CancellationTokenSource? _autoPickCts;
    private readonly DispatcherTimer _uptimeTimer;

    private Dictionary<string, FrameworkElement>? _navPages;
    private bool _navCollapsedByUser;
    private const double NavCollapseThreshold = 760;

    private readonly ObservableCollection<CheckRow> _diagRows = new();
    private readonly ObservableCollection<StrategyTestGroup> _testGroups = new();
    private CancellationTokenSource? _testCts;
    private bool _testing;

    private readonly ObservableCollection<Strategy> _editStrategies = new();
    private bool _suppressEditorSelection;

    private const string AppVersion = "1.0.0";

    public MainWindow()
    {
        InitializeComponent();

        _paths = AppPaths.Discover();
        _state = AppState.Load(_paths.StateFile);
        _catalog = StrategyCatalog.Load(_paths.StrategiesFile);
        if (_catalog.Strategies.Any(s => string.IsNullOrWhiteSpace(s.Command)))
        {
            var migrated = _catalog.Strategies
                .Select(s => string.IsNullOrWhiteSpace(s.Command) ? s with { Command = CommandBuilder.ToText(s) } : s)
                .ToList();
            try { StrategyCatalog.Save(_paths.StrategiesFile, migrated); } catch { }
            _catalog = StrategyCatalog.Load(_paths.StrategiesFile);
        }
        _runner = new WinwsRunner(_paths);
        _runner.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateStatus));
        _runner.Output += (_, line) => Dispatcher.BeginInvoke(new Action(() => AppendLog(line)));

        ApplyTheme(_state.Config.Theme);
        ApplyFont();

        var strategies = _catalog.Strategies.OrderBy(s => s.Label, NaturalComparer.Instance).ToList();
        StrategyCombo.ItemsSource = strategies;

        var current = _catalog.ById(_state.Config.CurrentStrategyId) ?? strategies.FirstOrDefault();
        if (current is not null)
        {
            _suppressSelection = true;
            StrategyCombo.SelectedItem = current;
            _suppressSelection = false;
        }

        _suppressSettings = true;
        ThemeToggle.IsChecked = string.Equals(_state.Config.Theme, "light", StringComparison.OrdinalIgnoreCase);
        AutostartToggle.IsChecked = Autostart.IsEnabled();
        StartMinToggle.IsChecked = _state.Config.StartMinimized;
        FontCombo.ItemsSource = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        FontCombo.SelectedItem = string.IsNullOrWhiteSpace(_state.Config.FontFamily) ? "Segoe UI" : _state.Config.FontFamily;
        FontSizeCombo.ItemsSource = new[] { 11, 12, 13, 14, 15, 16, 18, 20 };
        FontSizeCombo.SelectedItem = (int)(_state.Config.FontSize >= 8 ? _state.Config.FontSize : 14);
        AccentHexBox.Text = string.Equals(_state.Config.AccentColor, "system", StringComparison.OrdinalIgnoreCase)
            ? "" : _state.Config.AccentColor;
        _suppressSettings = false;

        if (_catalog.Strategies.Count == 0)
            AppendLog("strategies.json не найден или пуст (ожидается в state/strategies.json).");

        _uptimeTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => UpdateUptime();
        AboutVersion.Text = BuildAboutVersion();

        _navPages = new Dictionary<string, FrameworkElement>
        {
            ["dashboard"] = DashboardPanel,
            ["lists"] = ListsPanel,
            ["editor"] = EditorPanel,
            ["journal"] = JournalPanel,
            ["diagnostics"] = DiagnosticsPanel,
            ["settings"] = SettingsPanel,
        };
        DiagResults.ItemsSource = _diagRows;
        StrategyTests.ItemsSource = _testGroups;
        EditorList.ItemsSource = _editStrategies;

        UpdateStatus();
        ShowPage("dashboard");

        SizeChanged += (_, _) => ApplyAdaptiveNav();

        Loaded += (_, _) =>
        {
            ApplyAdaptiveNav();
            UpdateListCounts();
            if (_state.Config.StartMinimized)
            {
                Hide();
                MemoryTrimmer.Trim();
            }
        };
    }

    private Strategy? SelectedStrategy() => StrategyCombo.SelectedItem as Strategy;

    private void Connect_Click(object sender, MouseButtonEventArgs e) => _ = ToggleAsync();
    private void Toggle_Click(object sender, RoutedEventArgs e) => _ = ToggleAsync();

    private async Task ToggleAsync()
    {
        if (_busy)
            return;

        _busy = true;
        _startError = null;
        StartSpinner();
        try
        {
            if (_runner.IsRunning)
            {
                AppendLog("Остановка…");
                await _runner.StopAsync();
            }
            else
            {
                var s = SelectedStrategy();
                if (s is null)
                {
                    AppendLog("Сначала выберите стратегию.");
                    return;
                }
                AppendLog($"Запуск «{s.Label}»…");
                await _runner.StartAsync(s);
            }
        }
        catch (Exception ex)
        {
            ReportStartError(ex);
        }
        finally
        {
            _busy = false;
            StopSpinner();
            UpdateStatus();
        }
    }

    private async void StrategyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;

        var s = SelectedStrategy();
        if (s is null)
            return;

        _state.Config.CurrentStrategyId = s.Id;
        _state.Save();

        if (_runner.IsRunning && !_busy)
        {
            _busy = true;
            StartSpinner();
            try
            {
                AppendLog($"Переключение на «{s.Label}»…");
                _startError = null;
                await _runner.StartAsync(s);
            }
            catch (Exception ex) { ReportStartError(ex); }
            finally { _busy = false; StopSpinner(); }
        }
        UpdateStatus();
    }

    private void SelectStrategyById(string id)
    {
        var s = _catalog.ById(id);
        if (s is null)
            return;
        _suppressSelection = true;
        StrategyCombo.SelectedItem = s;
        _suppressSelection = false;
        _state.Config.CurrentStrategyId = id;
        _state.Save();
    }

    private async void AutoPick_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        _busy = true;
        StartSpinner();
        AutoPickButton.IsEnabled = false;
        var oldContent = AutoPickButton.Content;
        AutoPickButton.Content = "Подбираю…";

        var results = new ObservableCollection<AutoPickItem>();
        AutoPickResults.ItemsSource = results;
        AutoPickBar.Value = 0;
        AutoPickCurrent.Text = "Подготовка…";
        AutoPickCloseBtn.Content = "Отмена";
        ShowOverlay(AutoPickPanel);

        _autoPickCts = new CancellationTokenSource();
        var progress = new Progress<AutoPickProgress>(p =>
        {
            switch (p.Phase)
            {
                case AutoPickPhase.Started:
                    AutoPickBar.Maximum = Math.Max(1, p.Total);
                    AutoPickBar.Value = 0;
                    results.Clear();
                    AutoPickCurrent.Text = "Запуск…";
                    break;
                case AutoPickPhase.Testing:
                    AutoPickBar.Value = p.Index;
                    AutoPickCurrent.Text = $"Проверяю «{p.Label}» ({p.Index}/{p.Total})";
                    break;
                case AutoPickPhase.Passed:
                    results.Add(new AutoPickItem($"✓   {p.Label}", OkBrush));
                    break;
                case AutoPickPhase.Failed:
                    results.Add(new AutoPickItem($"✗   {p.Label}", FailBrush));
                    break;
                case AutoPickPhase.Done:
                    AutoPickBar.Value = AutoPickBar.Maximum;
                    AutoPickCurrent.Text = p.Result is not null
                        ? $"Готово — выбрана «{p.Result.Label}»"
                        : "Подходящая стратегия не найдена";
                    AutoPickCloseBtn.Content = "Закрыть";
                    break;
            }
        });

        try
        {
            var picker = new AutoPicker(_runner);
            var candidates = _catalog.Strategies
                .OrderByDescending(s => s.Id == (SelectedStrategy()?.Id ?? _state.Config.CurrentStrategyId))
                .ToList();

            var found = await picker.RunAsync(candidates, progress, _autoPickCts.Token);
            if (found is not null)
            {
                SelectStrategyById(found.Id);
            }
            else
            {
                await _runner.StopAsync();
            }
        }
        catch (OperationCanceledException)
        {
            AutoPickCurrent.Text = "Отменено";
            AutoPickCloseBtn.Content = "Закрыть";
            try { await _runner.StopAsync(); } catch { }
        }
        catch (Exception ex)
        {
            AutoPickCurrent.Text = "Ошибка: " + ex.Message;
            AutoPickCloseBtn.Content = "Закрыть";
        }
        finally
        {
            _autoPickCts?.Dispose();
            _autoPickCts = null;
            AutoPickButton.Content = oldContent;
            AutoPickButton.IsEnabled = true;
            _busy = false;
            StopSpinner();
            UpdateStatus();
        }
    }

    private void AutoPickClose_Click(object sender, RoutedEventArgs e)
    {
        if (_autoPickCts is { IsCancellationRequested: false } cts)
        {
            cts.Cancel();
            return;
        }
        HideOverlays();
    }

    private List<ListFileVm> _listVms = new();

    private void LoadListsPage()
    {
        _listVms = ListsManager.Enumerate(_paths)
            .Select(name =>
            {
                var path = _paths.List(name);
                return new ListFileVm(name, path,
                    ListsManager.CountEntries(path),
                    ListsManager.CountBadLines(_paths, name),
                    _state.Config.ListNotes.GetValueOrDefault(name, ""));
            })
            .ToList();
        ListsItems.ItemsSource = _listVms;
        SelectAllCheck.IsChecked = false;
        RefreshListsValidation();
    }

    private void RefreshListsValidation()
    {
        var lines = new List<string>();
        var error = false;

        foreach (var issue in ListsManager.Validate(_paths))
        {
            lines.Add("• " + issue.Text);
            if (issue.Level == ListIssueLevel.Error)
                error = true;
        }

        foreach (var vm in _listVms.Where(v => v.BadCount > 0))
            lines.Add($"• «{vm.FileName}»: подозрительных строк — {vm.BadCount}");

        if (lines.Count == 0)
        {
            ListsInfoBar.IsOpen = false;
            return;
        }

        ListsInfoBar.Severity = error
            ? Wpf.Ui.Controls.InfoBarSeverity.Error
            : Wpf.Ui.Controls.InfoBarSeverity.Warning;
        ListsInfoBar.Title = error
            ? "Со списками проблема — обход может не запуститься"
            : "В списках есть подозрительные строки";
        ListsInfoBar.Message = string.Join(Environment.NewLine, lines);
        ListsInfoBar.IsOpen = true;
    }

    private void ListNote_Commit(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ListFileVm vm)
            return;
        var note = vm.Note.Trim();
        if (string.IsNullOrEmpty(note))
            _state.Config.ListNotes.Remove(vm.FileName);
        else
            _state.Config.ListNotes[vm.FileName] = note;
        _state.Save();
    }

    private void NoteBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox tb)
        {
            if (!tb.IsKeyboardFocusWithin)
                tb.Focus();
            e.Handled = true;
        }
    }

    private void SelectAll_Changed(object sender, RoutedEventArgs e)
    {
        var on = ((CheckBox)sender).IsChecked == true;
        foreach (var vm in _listVms)
            vm.IsSelected = on;
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = _listVms.Where(v => v.IsSelected).ToList();
        if (sel.Count == 0)
        {
            AppendLog("Не выбрано ни одного списка.");
            return;
        }
        var r = MessageBox.Show(this,
            $"Удалить выбранные файлы ({sel.Count})? Это может нарушить работу обхода.",
            "Удаление списков", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes)
            return;

        foreach (var vm in sel)
        {
            try { File.Delete(vm.FullPath); _state.Config.ListNotes.Remove(vm.FileName); }
            catch (Exception ex) { AppendLog($"Не удалось удалить {vm.FileName}: {ex.Message}"); }
        }
        _state.Save();
        AppendLog($"Удалено списков: {sel.Count}.");
        LoadListsPage();
        UpdateListCounts();
    }

    private void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        var sel = _listVms.Where(v => v.IsSelected).ToList();
        if (sel.Count == 0)
        {
            AppendLog("Не выбрано ни одного списка.");
            return;
        }
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Куда экспортировать выбранные списки" };
        if (dlg.ShowDialog(this) != true)
            return;

        var done = 0;
        foreach (var vm in sel)
        {
            try { File.Copy(vm.FullPath, Path.Combine(dlg.FolderName, vm.FileName), overwrite: true); done++; }
            catch (Exception ex) { AppendLog($"Не удалось экспортировать {vm.FileName}: {ex.Message}"); }
        }
        AppendLog($"Экспортировано списков: {done} → {dlg.FolderName}");
    }

    private void ListExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ListFileVm vm)
            vm.Load();
    }

    private void ListFileSave_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ListFileVm vm)
            return;
        try
        {
            UserLists.SaveText(vm.FullPath, vm.Content);
            vm.Content = UserLists.ReadText(vm.FullPath);
            vm.Count = ListsManager.CountEntries(vm.FullPath);
            vm.BadCount = ListsManager.CountBadLines(_paths, vm.FileName);
            AppendLog($"Сохранён список «{vm.FileName}» (применится при следующем подключении).");
        }
        catch (Exception ex) { AppendLog("Не удалось сохранить: " + ex.Message); }
        RefreshListsValidation();
        UpdateListCounts();
    }

    private void ListFileExport_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ListFileVm vm)
            return;
        vm.Load();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Экспорт списка в .txt",
            Filter = "Списки (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = vm.FileName,
            DefaultExt = ".txt",
        };
        if (dlg.ShowDialog(this) != true)
            return;
        try
        {
            UserLists.SaveText(dlg.FileName, vm.Content);
            AppendLog($"Экспортирован список: {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) { AppendLog("Не удалось сохранить файл: " + ex.Message); }
    }

    private void ListFileDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ListFileVm vm)
            return;
        var r = MessageBox.Show(this,
            $"Удалить файл «{vm.FileName}»? Это может нарушить работу обхода.",
            "Удаление списка", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes)
            return;
        try { File.Delete(vm.FullPath); AppendLog($"Удалён список «{vm.FileName}»."); }
        catch (Exception ex) { AppendLog("Не удалось удалить: " + ex.Message); }
        LoadListsPage();
        UpdateListCounts();
    }

    private void AddListFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Добавить файл(ы) списка",
            Filter = "Списки (*.txt)|*.txt|Все файлы (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true)
            return;

        Directory.CreateDirectory(_paths.ListsDir);
        var added = 0;
        foreach (var src in dlg.FileNames)
        {
            try
            {
                var dest = _paths.List(Path.GetFileName(src));
                if (File.Exists(dest))
                {
                    var r = MessageBox.Show(this, $"«{Path.GetFileName(dest)}» уже есть. Заменить?",
                        "Добавление списка", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes)
                        continue;
                }
                File.Copy(src, dest, overwrite: true);
                added++;
            }
            catch (Exception ex) { AppendLog($"Не удалось добавить {Path.GetFileName(src)}: {ex.Message}"); }
        }
        if (added > 0)
        {
            AppendLog($"Добавлено файлов: {added}.");
            LoadListsPage();
            UpdateListCounts();
        }
    }

    private void CreateListFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Создать новый список",
            Filter = "Списки (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = "my-list.txt",
            InitialDirectory = _paths.ListsDir,
        };
        if (dlg.ShowDialog(this) != true)
            return;
        try
        {
            var dest = _paths.List(Path.GetFileName(dlg.FileName));
            if (!File.Exists(dest))
            {
                Directory.CreateDirectory(_paths.ListsDir);
                File.WriteAllText(dest, "");
            }
            AppendLog($"Создан список «{Path.GetFileName(dest)}».");
            LoadListsPage();
        }
        catch (Exception ex) { AppendLog("Не удалось создать список: " + ex.Message); }
    }

    private void Update_Click(object sender, RoutedEventArgs e) =>
        AppendLog("Обновление списков ещё не реализовано.");

    private async void UpdateListCounts()
    {
        try
        {
            var (domains, ips) = await Task.Run(() =>
            {
                var d = CountEntries("list-general.txt") + CountEntries("list-general-user.txt") + CountEntries("list-google.txt");
                var i = CountEntries("ipset-all.txt") + CountEntries("ipset-user.txt");
                return (d, i);
            });
            ListCountsText.Text = $"Домены: {domains:N0} · IP: {ips:N0}";
        }
        catch { ListCountsText.Text = ""; }
    }

    private int CountEntries(string file)
    {
        var path = _paths.List(file);
        if (!File.Exists(path))
            return 0;
        var n = 0;
        try
        {
            foreach (var raw in File.ReadLines(path))
            {
                var l = raw.Trim();
                if (l.Length > 0 && !l.StartsWith('#'))
                    n++;
            }
        }
        catch { }
        return n;
    }

    private Wpf.Ui.Appearance.ApplicationTheme CurrentTheme =>
        string.Equals(_state.Config.Theme, "light", StringComparison.OrdinalIgnoreCase)
            ? Wpf.Ui.Appearance.ApplicationTheme.Light
            : Wpf.Ui.Appearance.ApplicationTheme.Dark;

    private void ApplyTheme(string? theme)
    {
        var t = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)
            ? Wpf.Ui.Appearance.ApplicationTheme.Light
            : Wpf.Ui.Appearance.ApplicationTheme.Dark;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(t);
        ApplyAccent(_state.Config.AccentColor);
    }

    private void ApplyAccent(string? accent)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accent) || string.Equals(accent, "system", StringComparison.OrdinalIgnoreCase))
            {
                Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();
            }
            else
            {
                var c = (Color)ColorConverter.ConvertFromString(accent)!;
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(c, CurrentTheme, false, false);
            }
        }
        catch
        {
            Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();
        }
    }

    private void ApplyFont()
    {
        if (!string.IsNullOrWhiteSpace(_state.Config.FontFamily))
        {
            try { FontFamily = new System.Windows.Media.FontFamily(_state.Config.FontFamily); }
            catch { }
        }
        if (_state.Config.FontSize is >= 8 and <= 32)
            FontSize = _state.Config.FontSize;
    }

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string hex)
            return;
        _state.Config.AccentColor = hex;
        _state.Save();
        ApplyAccent(hex);
        AccentHexBox.Text = string.Equals(hex, "system", StringComparison.OrdinalIgnoreCase) ? "" : hex;
        UpdateStatus();
    }

    private void AccentHexApply_Click(object sender, RoutedEventArgs e)
    {
        var hex = AccentHexBox.Text.Trim();
        if (string.IsNullOrEmpty(hex))
        {
            _state.Config.AccentColor = "system";
            _state.Save();
            ApplyAccent("system");
            UpdateStatus();
            return;
        }
        if (!hex.StartsWith('#'))
            hex = "#" + hex;
        try { _ = (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { AppendLog($"Неверный цвет: {hex}"); return; }

        _state.Config.AccentColor = hex;
        _state.Save();
        ApplyAccent(hex);
        UpdateStatus();
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings)
            return;
        if (FontCombo.SelectedItem is string family)
        {
            _state.Config.FontFamily = family;
            _state.Save();
            ApplyFont();
        }
    }

    private void FontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings)
            return;
        if (FontSizeCombo.SelectedItem is int size)
        {
            _state.Config.FontSize = size;
            _state.Save();
            ApplyFont();
        }
    }

    private void Theme_Toggle(object sender, RoutedEventArgs e)
    {
        if (_suppressSettings)
            return;
        var light = ((ToggleButton)sender).IsChecked == true;
        ApplyTheme(light ? "light" : "dark");
        _state.Config.Theme = light ? "light" : "dark";
        _state.Save();
        UpdateStatus();
    }

    private void Autostart_Toggle(object sender, RoutedEventArgs e)
    {
        if (_suppressSettings)
            return;
        var toggle = (ToggleButton)sender;
        var on = toggle.IsChecked == true;

        var exe = Environment.ProcessPath;
        var ok = on ? (exe is not null && Autostart.Enable(exe)) : Autostart.Disable();
        if (on && !ok)
        {
            AppendLog("Не удалось включить автозапуск (нужны права администратора).");
            _suppressSettings = true;
            toggle.IsChecked = false;
            _suppressSettings = false;
            return;
        }

    }

    private void StartMin_Toggle(object sender, RoutedEventArgs e)
    {
        if (_suppressSettings)
            return;
        _state.Config.StartMinimized = ((ToggleButton)sender).IsChecked == true;
        _state.Save();
    }

    private bool _navCollapsed;

    private void NavToggle_Click(object sender, RoutedEventArgs e)
    {
        _navCollapsedByUser = !_navCollapsed;
        SetNavCollapsed(_navCollapsedByUser);
    }

    private void SetNavCollapsed(bool collapsed)
    {
        _navCollapsed = collapsed;
        NavColumn.Width = new GridLength(collapsed ? 48 : 220);

        var labels = collapsed ? Visibility.Collapsed : Visibility.Visible;
        NavHomeLabel.Visibility = labels;
        NavListsLabel.Visibility = labels;
        NavEditorLabel.Visibility = labels;
        NavJournalLabel.Visibility = labels;
        NavDiagLabel.Visibility = labels;
        NavSettingsLabel.Visibility = labels;

        NavToggle.ToolTip = collapsed ? "Развернуть меню" : "Свернуть меню";
    }

    private void ApplyAdaptiveNav()
    {
        if (ActualWidth < NavCollapseThreshold)
        {
            if (!_navCollapsed)
                SetNavCollapsed(true);
        }
        else if (_navCollapsed && !_navCollapsedByUser)
        {
            SetNavCollapsed(false);
        }
    }

    private async void RunDiag_Click(object sender, RoutedEventArgs e)
    {
        RunDiagButton.IsEnabled = false;
        var old = RunDiagButton.Content;
        RunDiagButton.Content = "Проверяю…";
        _diagRows.Clear();
        try
        {
            var items = await Task.Run(() => Diagnostics.Run(_paths));
            foreach (var it in items)
                _diagRows.Add(new CheckRow(BrushFor(it.Level), it.Title, it.Detail));
        }
        catch (Exception ex) { AppendLog("Диагностика: " + ex.Message); }
        finally
        {
            RunDiagButton.Content = old;
            RunDiagButton.IsEnabled = true;
        }
    }

    private async void RunTest_Click(object sender, RoutedEventArgs e)
    {
        if (_testing)
        {
            _testCts?.Cancel();
            return;
        }

        var strategies = _catalog.Strategies.OrderBy(s => s.Label, NaturalComparer.Instance).ToList();
        if (strategies.Count == 0)
        {
            AppendLog("Тесты: список стратегий пуст.");
            return;
        }

        var dpiMode = TestTypeCombo.SelectedIndex == 1;

        _testing = true;
        _busy = true;
        RunTestButton.Content = "Отмена";
        TestTypeCombo.IsEnabled = false;
        _testGroups.Clear();
        TestProgressPanel.Visibility = Visibility.Visible;
        TestStatus.Text = "Подготовка…";
        TestPercent.Text = "0%";
        TestProgress.Value = 0;

        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        var wasRunning = _runner.IsRunning;
        var restoreTo = _runner.Current ?? SelectedStrategy();

        try
        {
            var idx = 0;
            foreach (var s in strategies)
            {
                ct.ThrowIfCancellationRequested();
                idx++;
                var current = idx;
                TestStatus.Text = $"Проверяю «{s.Label}» · {current}/{strategies.Count}";
                TestProgress.Value = (double)(current - 1) / strategies.Count * 100;
                TestPercent.Text = $"{(int)Math.Round((double)(current - 1) / strategies.Count * 100)}%";
                var progress = new Progress<TestProgress>(p =>
                {
                    if (p.Total <= 0) return;
                    var pct = (current - 1 + (double)p.Done / p.Total) / strategies.Count * 100;
                    TestProgress.Value = pct;
                    TestPercent.Text = $"{(int)Math.Round(pct)}%";
                });

                bool started = true;
                try { await _runner.StartAsync(s, ct); }
                catch (OperationCanceledException) { throw; }
                catch { started = false; }

                if (!started)
                {
                    _testGroups.Add(new StrategyTestGroup(s.Label, "не запустилась", "", ErrorBrush,
                        new ObservableCollection<SiteRow>()));
                    continue;
                }

                await Task.Delay(2000, ct);

                ObservableCollection<SiteRow> rows;
                int okCount, total;
                string analytics;
                if (dpiMode)
                {
                    var results = await DpiChecker.RunAsync(progress, ct);
                    rows = new ObservableCollection<SiteRow>(results.Select(r => new SiteRow(
                        r.Name,
                        r.Ok ? new StatusCell("OK", OkBrush, OkBadgeBg, r.Detail)
                             : new StatusCell("ERR", ErrorBrush, ErrBadgeBg, r.Detail),
                        new StatusCell("—", FailBrush, NoBadgeBg),
                        new StatusCell("—", FailBrush, NoBadgeBg),
                        new StatusCell(r.Detail, r.Ok ? FailBrush : ErrorBrush, NoBadgeBg))));
                    okCount = results.Count(r => r.Ok);
                    total = results.Count;
                    analytics = $"нет фриза: {okCount} · режется: {total - okCount}";
                }
                else
                {
                    var results = await RestrictionTester.RunAsync(progress, ct);
                    rows = new ObservableCollection<SiteRow>(results.Select(a => new SiteRow(
                        a.Name,
                        BadgeCell(a.Http),
                        BadgeCell(a.Tls12),
                        BadgeCell(a.Tls13),
                        PingCellVm(a.Ping))));

                    bool Ok(ProbeCell? c) => c is { State: ProbeState.Ok };
                    okCount = results.Count(a => a.IsSite ? (Ok(a.Tls13) || Ok(a.Tls12)) : Ok(a.Ping));
                    total = results.Count;

                    var sites = results.Where(a => a.IsSite).ToList();
                    int hOk = sites.Count(a => Ok(a.Http)), hErr = sites.Count(a => a.Http is { State: ProbeState.Err });
                    int tOk = sites.Count(a => Ok(a.Tls12) || Ok(a.Tls13));
                    int tErr = sites.Count(a => !Ok(a.Tls12) && !Ok(a.Tls13));
                    int pOk = results.Count(a => Ok(a.Ping)), pErr = results.Count(a => !Ok(a.Ping));
                    analytics = $"HTTP {hOk}✓/{hErr}✗   ·   TLS {tOk}✓/{tErr}✗   ·   Пинг {pOk}✓/{pErr}✗";
                }

                var brush = okCount == total ? OkBrush : okCount == 0 ? ErrorBrush : WarnBrush;
                _testGroups.Add(new StrategyTestGroup(
                    s.Label, $"{okCount}/{total} доступно", analytics, brush, rows));
            }
            TestStatus.Text = "Готово";
        }
        catch (OperationCanceledException) { TestStatus.Text = "Отменено"; }
        catch (Exception ex) { AppendLog("Тесты: " + ex.Message); TestStatus.Text = "Ошибка: " + ex.Message; }
        finally
        {
            try
            {
                if (wasRunning && restoreTo is not null)
                    await _runner.StartAsync(restoreTo);
                else
                    await _runner.StopAsync();
            }
            catch { }

            _busy = false;
            _testing = false;
            RunTestButton.Content = "Проверить";
            TestTypeCombo.IsEnabled = true;
            _testCts?.Dispose();
            _testCts = null;
            UpdateStatus();
        }
    }

    private static Brush BrushFor(DiagLevel level) => level switch
    {
        DiagLevel.Ok => OkBrush,
        DiagLevel.Warn => WarnBrush,
        _ => ErrorBrush,
    };

    private void LoadEditor()
    {
        if (_editStrategies.Count > 0)
            return;
        foreach (var s in _catalog.Strategies)
            _editStrategies.Add(s);
        if (_editStrategies.Count > 0)
            EditorList.SelectedIndex = 0;
    }

    private void EditorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEditorSelection)
            return;
        if (EditorList.SelectedItem is Strategy s)
        {
            EditorForm.IsEnabled = true;
            EditLabel.Text = s.Label;
            EditArgs.Text = string.IsNullOrWhiteSpace(s.Command) ? CommandBuilder.ToText(s) : s.Command;
        }
        else
        {
            EditorForm.IsEnabled = false;
            EditLabel.Text = "";
            EditArgs.Text = "";
        }
    }

    private void EditorAdd_Click(object sender, RoutedEventArgs e)
    {
        var s = new Strategy { Id = GenStrategyId("новая"), Label = "Новая стратегия" };
        _editStrategies.Add(s);
        EditorList.SelectedItem = s;
        EditLabel.Focus();
    }

    private void EditorDelete_Click(object sender, RoutedEventArgs e)
    {
        if (EditorList.SelectedItem is not Strategy s)
            return;
        var idx = EditorList.SelectedIndex;
        _editStrategies.Remove(s);
        if (_editStrategies.Count > 0)
            EditorList.SelectedIndex = Math.Min(idx, _editStrategies.Count - 1);
    }

    private void EditorSave_Click(object sender, RoutedEventArgs e)
    {
        ApplyEditorForm();
        try
        {
            StrategyCatalog.Save(_paths.StrategiesFile, _editStrategies.ToList());
            _catalog = StrategyCatalog.Load(_paths.StrategiesFile);
            RefreshStrategyCombo();
            AppendLog($"Стратегии сохранены ({_editStrategies.Count}).");
        }
        catch (Exception ex)
        {
            AppendLog("Не удалось сохранить стратегии: " + ex.Message);
        }
    }

    private void ApplyEditorForm()
    {
        var idx = EditorList.SelectedIndex;
        if (idx < 0)
            return;

        var cur = _editStrategies[idx];
        var label = string.IsNullOrWhiteSpace(EditLabel.Text) ? "Без названия" : EditLabel.Text.Trim();
        var updated = cur with { Label = label, Command = EditArgs.Text.Trim() };

        _suppressEditorSelection = true;
        _editStrategies[idx] = updated;
        EditorList.SelectedIndex = idx;
        _suppressEditorSelection = false;
    }

    private string GenStrategyId(string baseLabel)
    {
        var slug = new string((baseLabel ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (slug.Length == 0)
            slug = "strategy";
        var id = slug;
        var n = 1;
        while (_editStrategies.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            id = slug + ++n;
        return id;
    }

    private void RefreshStrategyCombo()
    {
        var currentId = SelectedStrategy()?.Id ?? _state.Config.CurrentStrategyId;
        var strategies = _catalog.Strategies.OrderBy(s => s.Label, NaturalComparer.Instance).ToList();

        _suppressSelection = true;
        StrategyCombo.ItemsSource = strategies;
        StrategyCombo.SelectedItem = _catalog.ById(currentId) ?? strategies.FirstOrDefault();
        _suppressSelection = false;

        UpdateStatus();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
            ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        if (_navPages is null)
            return;

        foreach (var (key, panel) in _navPages)
            panel.Visibility = key == tag ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "lists")
            LoadListsPage();
        else if (tag == "editor")
            LoadEditor();
    }

    private void Scrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Scrim))
            return;
        if (_autoPickCts is { IsCancellationRequested: false } cts)
        {
            cts.Cancel();
            return;
        }
        HideOverlays();
    }

    private void ShowOverlay(FrameworkElement panel)
    {
        panel.Visibility = Visibility.Visible;
        Scrim.Visibility = Visibility.Visible;
        Scrim.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(140))));
    }

    private void HideOverlays()
    {
        Scrim.BeginAnimation(OpacityProperty, null);
        Scrim.Opacity = 1;
        Scrim.Visibility = Visibility.Collapsed;
        AutoPickPanel.Visibility = Visibility.Collapsed;
    }

    private void StartSpinner()
    {
        Spinner.Visibility = Visibility.Visible;
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1.1))) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void StopSpinner()
    {
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        Spinner.Visibility = Visibility.Collapsed;
    }

    private void SetPulse(bool on)
    {
        if (on)
        {
            ConnectPulse.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.5, 0.0, new Duration(TimeSpan.FromSeconds(1.6)))
                { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true });
        }
        else
        {
            ConnectPulse.BeginAnimation(OpacityProperty, null);
            ConnectPulse.Opacity = 0;
        }
    }

    private void TrayOpen_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _reallyExit = true;
        try { _runner.Dispose(); } catch { }
        try { Tray.Dispose(); } catch { }
        Application.Current.Shutdown();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void UpdateStatus()
    {
        var running = _runner.IsRunning;
        var shown = running ? _runner.Current : SelectedStrategy();

        var accent = AccentBrush();
        ConnectRing.Stroke = running ? accent : RingOff;
        ConnectIcon.Foreground = running ? accent : RingOff;
        StatusTitle.Text = running ? "Защищено" : "Не защищено";

        if (!running && _startError is not null)
        {
            StatusSub.Text = _startError;
            StatusSub.Foreground = ErrorBrush;
        }
        else
        {
            StatusSub.Text = running
                ? $"Обход включён · {shown?.Label}"
                : "Нажмите кнопку, чтобы включить обход";
            StatusSub.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        }

        TrayToggle.Header = running ? "Отключить" : "Подключить";
        Tray.ToolTipText = running ? $"Zapret2 — {shown?.Label}" : "Zapret2";

        SetPulse(running && !_busy);

        if (running)
        {
            UpdateUptime();
            if (!_uptimeTimer.IsEnabled)
                _uptimeTimer.Start();
        }
        else
        {
            _uptimeTimer.Stop();
            UptimePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateUptime()
    {
        if (_runner.StartedAt is DateTime started)
        {
            var elapsed = DateTime.Now - started;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;
            UptimeText.Text = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            UptimePanel.Visibility = Visibility.Visible;
        }
        else
        {
            UptimePanel.Visibility = Visibility.Collapsed;
        }
    }

    private static string BuildAboutVersion()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return $"версия {AppVersion} · сборка {File.GetLastWriteTime(exe):dd.MM.yyyy HH:mm}";
        }
        catch { }
        return $"версия {AppVersion}";
    }

    private void ReportStartError(Exception ex)
    {
        _startError = ex is FileNotFoundException
            ? @"Движок bin\winws2.exe не найден рядом с программой"
            : "Не удалось запустить — подробности в «Журнале»";
        AppendLog("Ошибка: " + ex.Message);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => AppendLog(line)));
            return;
        }
        LogBox.AppendText(line + Environment.NewLine);
        if (LogBox.Text.Length > 100_000)
            LogBox.Text = LogBox.Text[^50_000..];
        LogBox.CaretIndex = LogBox.Text.Length;
        LogBox.ScrollToEnd();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            e.Cancel = true;
            Hide();
            MemoryTrimmer.Trim();
            return;
        }
        base.OnClosing(e);
    }

    private Brush AccentBrush() => TryFindResource("AccentTextFillColorPrimaryBrush") as Brush ?? Accent;

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush FreezeA(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private StatusCell BadgeCell(ProbeCell? c) => c is null
        ? new StatusCell("—", FailBrush, NoBadgeBg)
        : c.State switch
        {
            ProbeState.Ok => new StatusCell("OK", OkBrush, OkBadgeBg, c.Detail),
            ProbeState.Err => new StatusCell("ERR", ErrorBrush, ErrBadgeBg, c.Detail),
            _ => new StatusCell("н/д", FailBrush, NoBadgeBg, c.Detail),
        };

    private StatusCell PingCellVm(ProbeCell c) =>
        new(c.Detail, c.State == ProbeState.Ok ? FailBrush : ErrorBrush, NoBadgeBg);
}

internal sealed class AutoPickItem
{
    public string Text { get; }
    public Brush Brush { get; }

    public AutoPickItem(string text, Brush brush)
    {
        Text = text;
        Brush = brush;
    }
}

internal sealed class CheckRow
{
    public Brush Brush { get; }
    public string Title { get; }
    public string Detail { get; }

    public CheckRow(Brush brush, string title, string detail)
    {
        Brush = brush;
        Title = title;
        Detail = detail;
    }
}

internal sealed class StrategyTestGroup
{
    public string Label { get; }
    public string Summary { get; }
    public string Analytics { get; }
    public Brush Brush { get; }
    public ObservableCollection<SiteRow> Rows { get; }

    public StrategyTestGroup(string label, string summary, string analytics, Brush brush, ObservableCollection<SiteRow> rows)
    {
        Label = label;
        Summary = summary;
        Analytics = analytics;
        Brush = brush;
        Rows = rows;
    }
}

internal sealed class StatusCell
{
    public string Text { get; }
    public Brush Fg { get; }
    public Brush Bg { get; }
    public string? Tip { get; }

    public StatusCell(string text, Brush fg, Brush bg, string tip = "")
    {
        Text = text;
        Fg = fg;
        Bg = bg;
        Tip = string.IsNullOrEmpty(tip) ? null : tip;
    }
}

internal sealed class SiteRow
{
    public string Name { get; }
    public StatusCell Http { get; }
    public StatusCell Tls12 { get; }
    public StatusCell Tls13 { get; }
    public StatusCell Ping { get; }

    public SiteRow(string name, StatusCell http, StatusCell tls12, StatusCell tls13, StatusCell ping)
    {
        Name = name;
        Http = http;
        Tls12 = tls12;
        Tls13 = tls13;
        Ping = ping;
    }
}

internal sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = long.Parse(a.AsSpan(si, i - si));
                var nb = long.Parse(b.AsSpan(sj, j - sj));
                if (na != nb) return na.CompareTo(nb);
            }
            else
            {
                var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
