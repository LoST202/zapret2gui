using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
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
    private static readonly ImageSource TrayOn = MakeTrayIcon(Color.FromRgb(0x3F, 0xB9, 0x50));
    private static readonly ImageSource TrayOff = MakeTrayIcon(Color.FromRgb(0x8A, 0x90, 0x99));

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

    private bool _wasRunning;
    private int _crashCount;
    private DateTime _lastCrash = DateTime.MinValue;
    private bool _infoLoaded;
    private bool _infoMasked;
    private bool _sysLoading;
    private SysNet? _sysCache;
    private ObservableCollection<DashCardOption> _dashOpts = new();

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
        _runner.Crashed += (_, s) => Dispatcher.BeginInvoke(new Action(() => OnEngineCrashed(s)));

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
        AutoConnectToggle.IsChecked = _state.Config.AutoConnect;
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

        _infoMasked = _state.Config.MaskInfo;
        ApplyInfoMask();
        RebuildProfiles();
        LayoutDashboardCards();
        SetupStrategyEditor();

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
            if (_state.Config.AutoConnect && SelectedStrategy() is not null && !_runner.IsRunning)
                _ = AutoConnectStartupAsync();
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
                _crashCount = 0;   // fresh manual start → fresh watchdog budget
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

    // ===== Watchdog + tray notifications =====

    private async Task AutoConnectStartupAsync()
    {
        await ToggleAsync();
        if (_runner.IsRunning && _state.Config.StartMinimized)
            Notify("Zapret2 запущен", $"Обход включён · {_runner.Current?.Label}", BalloonIcon.Info);
    }

    private void OnEngineCrashed(Strategy s)
    {
        // StopAsync detaches the exit handler, so a user stop/switch never lands here -
        // getting here means the engine died on its own. Don't fight an action in progress.
        if (_busy)
            return;

        var now = DateTime.Now;
        if ((now - _lastCrash).TotalSeconds > 60)
            _crashCount = 0;   // isolated crash after a long stable run → fresh budget
        _lastCrash = now;
        _crashCount++;

        AppendLog($"Движок неожиданно остановился «{s.Label}».");

        if (_crashCount > 3)
        {
            Notify("Обход отключился", "Не удаётся восстановить — откройте «Диагностику».", BalloonIcon.Warning);
            UpdateStatus();
            return;
        }

        Notify("Обход отключился", $"Восстанавливаю «{s.Label}»…", BalloonIcon.Warning);
        _ = RestartAfterCrashAsync(s);
    }

    private async Task RestartAfterCrashAsync(Strategy s)
    {
        _busy = true;
        StartSpinner();
        try
        {
            await Task.Delay(1000);
            await _runner.StartAsync(s);
            Notify("Обход восстановлен", $"«{s.Label}» снова активен.", BalloonIcon.Info);
        }
        catch (Exception ex)
        {
            AppendLog("Не удалось восстановить обход: " + ex.Message);
            Notify("Обход отключился", "Не удалось восстановить.", BalloonIcon.Error);
        }
        finally
        {
            _busy = false;
            StopSpinner();
            UpdateStatus();
        }
    }

    private void Notify(string title, string message, BalloonIcon icon)
    {
        try { Tray.ShowBalloonTip(title, message, icon); }
        catch { /* balloon is best-effort */ }
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
        RebuildProfiles();
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

    // Updatable IP set, same source the original zapret uses.
    private const string IpsetUpdateUrl =
        "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/ipset-service.txt";

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as System.Windows.Controls.Control;
        if (btn is not null) btn.IsEnabled = false;

        ListsInfoBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Informational;
        ListsInfoBar.Title = "Обновление списка IP…";
        ListsInfoBar.Message = "Загружаю ipset-all.txt из репозитория Flowseal.";
        ListsInfoBar.IsOpen = true;

        var target = _paths.List("ipset-all.txt");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Zapret2/1.0");
            var data = await http.GetByteArrayAsync(IpsetUpdateUrl);
            if (data.Length == 0)
                throw new Exception("получен пустой файл");

            Directory.CreateDirectory(_paths.ListsDir);
            var tmp = target + ".tmp";
            await File.WriteAllBytesAsync(tmp, data);
            File.Move(tmp, target, overwrite: true);

            LoadListsPage();
            UpdateListCounts();

            var count = ListsManager.CountEntries(target);
            ListsInfoBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
            ListsInfoBar.Title = "Список IP обновлён";
            ListsInfoBar.Message = $"ipset-all.txt — {count:N0} записей. Применится при следующем подключении.";
            AppendLog($"Список IP обновлён: {count} записей.");
        }
        catch (Exception ex)
        {
            ListsInfoBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Error;
            ListsInfoBar.Title = "Не удалось обновить список";
            ListsInfoBar.Message = ex.Message;
            AppendLog("Обновление списка: " + ex.Message);
        }
        finally
        {
            if (btn is not null) btn.IsEnabled = true;
        }
    }

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

    private void AutoConnect_Toggle(object sender, RoutedEventArgs e)
    {
        if (_suppressSettings)
            return;
        _state.Config.AutoConnect = ((ToggleButton)sender).IsChecked == true;
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

        // Smoothly slide the pane width (PowerToys-style) instead of snapping.
        NavColumn.BeginAnimation(ColumnDefinition.WidthProperty, new GridLengthAnimation
        {
            From = NavColumn.Width,
            To = new GridLength(collapsed ? 48 : 220),
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

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
        TestStatus.Visibility = Visibility.Visible;
        TestStatus.Text = "Подготовка…";

        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        var wasRunning = _runner.IsRunning;
        var restoreTo = _runner.Current ?? SelectedStrategy();
        var noProbe = new Progress<TestProgress>(_ => { });

        try
        {
            var idx = 0;
            foreach (var s in strategies)
            {
                ct.ThrowIfCancellationRequested();
                idx++;
                TestStatus.Text = $"Тестирую «{s.Label}» ({idx}/{strategies.Count})…";

                bool started = true;
                try { await _runner.StartAsync(s, ct); }
                catch (OperationCanceledException) { throw; }
                catch { started = false; }

                if (!started)
                {
                    _testGroups.Add(new StrategyTestGroup(s.Label, "не запустилась", ErrorBrush,
                        new ObservableCollection<CheckRow>()));
                    continue;
                }

                await Task.Delay(2000, ct);
                var results = dpiMode
                    ? await DpiChecker.RunAsync(noProbe, ct)
                    : await RestrictionTester.RunAsync(noProbe, ct);

                var okCount = results.Count(r => r.Ok);
                var rows = new ObservableCollection<CheckRow>(
                    results.Select(r => new CheckRow(r.Ok ? OkBrush : ErrorBrush, r.Name, r.Detail)));
                var brush = okCount == results.Count ? OkBrush : okCount == 0 ? ErrorBrush : WarnBrush;
                _testGroups.Add(new StrategyTestGroup(
                    s.Label, $"{okCount}/{results.Count} доступно", brush, rows));
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

    private void EditorExport_Click(object sender, RoutedEventArgs e)
    {
        var idx = EditorList.SelectedIndex;
        if (idx < 0)
            return;
        ApplyEditorForm();
        var s = _editStrategies[idx];

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Стратегия Zapret2 (*.json)|*.json",
            FileName = SafeFileName(s.Label) + ".json",
        };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(s, ZapretJson.Default.Strategy);
            File.WriteAllText(dlg.FileName, json, new System.Text.UTF8Encoding(false));
            AppendLog($"Стратегия «{s.Label}» экспортирована.");
        }
        catch (Exception ex) { AppendLog("Экспорт стратегии: " + ex.Message); }
    }

    private void EditorImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Стратегия Zapret2 (*.json)|*.json|Все файлы (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var imported = new List<Strategy>();
            if (json.TrimStart().StartsWith('['))
            {
                var list = System.Text.Json.JsonSerializer.Deserialize(json, ZapretJson.Default.ListStrategy);
                if (list is not null) imported.AddRange(list);
            }
            else
            {
                var one = System.Text.Json.JsonSerializer.Deserialize(json, ZapretJson.Default.Strategy);
                if (one is not null) imported.Add(one);
            }

            if (imported.Count == 0)
            {
                AppendLog("Импорт: стратегий не найдено.");
                return;
            }

            Strategy? first = null;
            foreach (var s in imported)
            {
                var added = s with { Id = GenStrategyId(s.Label) };
                _editStrategies.Add(added);
                first ??= added;
            }
            if (first is not null)
                EditorList.SelectedItem = first;
            AppendLog($"Импортировано стратегий: {imported.Count}. Нажмите «Сохранить», чтобы записать.");
        }
        catch (Exception ex) { AppendLog("Импорт стратегии: " + ex.Message); }
    }

    private static string SafeFileName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "strategy" : clean;
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
        {
            if (key == tag)
            {
                panel.Visibility = Visibility.Visible;
                // Soft fade + slight rise so page switches feel alive.
                panel.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                panel.Visibility = Visibility.Collapsed;
            }
        }

        if (tag == "lists")
            LoadListsPage();
        else if (tag == "editor")
            LoadEditor();
        else if (tag == "dashboard")
            AnimateDashboardIn();
        else if (tag == "diagnostics" && !_infoLoaded)
            _ = LoadSysInfoAsync();
    }

    private async Task LoadSysInfoAsync(bool force = false)
    {
        if (_sysLoading)
            return;
        if (!force && _sysCache is not null)
        {
            PopulateSysInfo(_sysCache);
            return;
        }

        _sysLoading = true;
        RefreshInfoButton.IsEnabled = false;
        NetRefreshButton.IsEnabled = false;
        try
        {
            _sysCache = await SysNetInfo.CollectAsync().ConfigureAwait(true);
            PopulateSysInfo(_sysCache);
            _infoLoaded = true;
        }
        catch { }
        finally
        {
            _sysLoading = false;
            RefreshInfoButton.IsEnabled = true;
            NetRefreshButton.IsEnabled = true;
        }
    }

    private void PopulateSysInfo(SysNet info)
    {
        InfoPublicIp.Text = info.PublicIp;
        InfoAsn.Text = info.Asn;
        InfoIsp.Text = info.Isp;
        InfoLocation.Text = info.Location;
        InfoOs.Text = info.Os;
        InfoCpu.Text = info.Cpu;
        InfoRam.Text = info.Ram;
        InfoMachine.Text = info.Machine;
        InfoUptime.Text = info.Uptime;
        InfoNetIp.Text = info.PublicIp;
        InfoNetIsp.Text = info.Isp;
        ApplyInfoMask();
    }

    private async void RefreshInfo_Click(object sender, RoutedEventArgs e) => await LoadSysInfoAsync(force: true);
    private async void NetRefresh_Click(object sender, RoutedEventArgs e) => await LoadSysInfoAsync(force: true);

    private void MaskInfo_Click(object sender, RoutedEventArgs e)
    {
        _infoMasked = !_infoMasked;
        _state.Config.MaskInfo = _infoMasked;
        _state.Save();
        ApplyInfoMask();
    }

    // Blur the identifying fields so a screenshot can be shared without leaking IP / provider / hostname.
    private void ApplyInfoMask()
    {
        var sensitive = new[] { InfoPublicIp, InfoAsn, InfoIsp, InfoLocation, InfoMachine, InfoNetIp, InfoNetIsp };
        foreach (var tb in sensitive)
            tb.Effect = _infoMasked ? new BlurEffect { Radius = 9 } : null;
        MaskInfoButton.Content = _infoMasked ? "Показать" : "Скрыть";
        NetMaskButton.Icon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = _infoMasked ? Wpf.Ui.Controls.SymbolRegular.Eye24 : Wpf.Ui.Controls.SymbolRegular.EyeOff24,
        };
    }

    // ===== Сборный главный экран =====

    private static readonly (string Key, string Title)[] DashWidgets =
    {
        ("lists", "Списки"),
        ("actions", "Быстрые действия"),
        ("profiles", "Профили"),
        ("net", "Сеть"),
    };

    private Border? CardFor(string key) => key switch
    {
        "lists" => CardLists,
        "actions" => CardActions,
        "profiles" => CardProfiles,
        "net" => CardNet,
        _ => null,
    };

    private void LayoutDashboardCards()
    {
        var order = _state.Config.DashboardCards;
        DashCards.Children.Clear();
        foreach (var key in order)
        {
            var card = CardFor(key);
            if (card is null)
                continue;
            card.Visibility = Visibility.Visible;
            card.Opacity = 1;
            DashCards.Children.Add(card);
        }
        if (order.Contains("net"))
            _ = LoadSysInfoAsync();
    }

    private void DashCustomize_Click(object sender, RoutedEventArgs e)
    {
        _dashOpts = new ObservableCollection<DashCardOption>();
        var order = _state.Config.DashboardCards;
        foreach (var key in order)
        {
            var w = DashWidgets.FirstOrDefault(x => x.Key == key);
            if (w.Key is not null)
                _dashOpts.Add(new DashCardOption { Key = w.Key, Title = w.Title, Enabled = true });
        }
        foreach (var (key, title) in DashWidgets)
            if (!order.Contains(key))
                _dashOpts.Add(new DashCardOption { Key = key, Title = title, Enabled = false });

        DashOptionsList.ItemsSource = _dashOpts;
        ShowOverlay(DashCustomizePanel);
    }

    private void DashOptUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            var i = IndexOfOpt(key);
            if (i > 0) _dashOpts.Move(i, i - 1);
        }
    }

    private void DashOptDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            var i = IndexOfOpt(key);
            if (i >= 0 && i < _dashOpts.Count - 1) _dashOpts.Move(i, i + 1);
        }
    }

    private int IndexOfOpt(string key)
    {
        for (var i = 0; i < _dashOpts.Count; i++)
            if (_dashOpts[i].Key == key) return i;
        return -1;
    }

    private void DashCustomizeCancel_Click(object sender, RoutedEventArgs e) => HideOverlays();

    private void DashCustomizeApply_Click(object sender, RoutedEventArgs e)
    {
        _state.Config.DashboardCards = _dashOpts.Where(o => o.Enabled).Select(o => o.Key).ToList();
        _state.Save();
        LayoutDashboardCards();
        HideOverlays();
    }

    // Staggered reveal: each card fades up in turn for a lively, modern entrance.
    private void AnimateDashboardIn()
    {
        var cards = new List<FrameworkElement> { HeroCard };
        foreach (var child in DashCards.Children)
            if (child is FrameworkElement fe)
                cards.Add(fe);
        var delay = 0;
        foreach (var card in cards)
        {
            if (card is null) continue;
            // Fresh per-instance transform: ScaleTransform (for hover) + TranslateTransform (for the rise).
            var translate = new TranslateTransform(0, 6);
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            card.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), translate } };
            card.Opacity = 0;

            var begin = TimeSpan.FromMilliseconds(delay);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            card.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = begin, EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(220)) { BeginTime = begin, EasingFunction = ease });
            delay += 40;
        }
    }

    private void NavToLists_Click(object sender, RoutedEventArgs e) => NavLists.IsChecked = true;
    private void NavToEditor_Click(object sender, RoutedEventArgs e) => NavEditor.IsChecked = true;
    private void NavToJournal_Click(object sender, RoutedEventArgs e) => NavJournal.IsChecked = true;
    private void NavToDiag_Click(object sender, RoutedEventArgs e) => NavDiag.IsChecked = true;

    // ===== Профили =====

    private void RebuildProfiles()
    {
        var currentId = SelectedStrategy()?.Id ?? _state.Config.CurrentStrategyId;
        var accent = AccentBrush();
        var items = _state.Config.Profiles.Select(p => new ProfileVm
        {
            Id = p.Id,
            Name = p.Name,
            StrategyLabel = _catalog.ById(p.StrategyId)?.Label ?? "стратегия удалена",
            Border = p.StrategyId == currentId ? accent : Brushes.Transparent,
        }).ToList();

        ProfilesList.ItemsSource = items;
        ProfilesEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ProfileAdd_Click(object sender, RoutedEventArgs e)
    {
        var s = SelectedStrategy();
        if (s is null)
        {
            Notify("Профили", "Сначала выберите стратегию.", BalloonIcon.Warning);
            return;
        }
        ProfileNameBox.Text = "";
        ProfileNameHint.Text = $"Сохранит стратегию «{s.Label}» под этим именем.";
        ShowOverlay(ProfileNamePanel);
        ProfileNameBox.Focus();
    }

    private void ProfileNameSave_Click(object sender, RoutedEventArgs e) => SaveProfile();
    private void ProfileNameCancel_Click(object sender, RoutedEventArgs e) => HideOverlays();

    private void ProfileNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SaveProfile(); e.Handled = true; }
        else if (e.Key == Key.Escape) { HideOverlays(); e.Handled = true; }
    }

    private void SaveProfile()
    {
        var s = SelectedStrategy();
        if (s is null) { HideOverlays(); return; }

        var name = ProfileNameBox.Text?.Trim() ?? "";
        if (name.Length == 0)
            name = s.Label;

        _state.Config.Profiles.Add(new Profile { Id = Guid.NewGuid().ToString("N"), Name = name, StrategyId = s.Id });
        _state.Save();
        RebuildProfiles();
        HideOverlays();
    }

    private void ProfileApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
            return;
        var p = _state.Config.Profiles.FirstOrDefault(x => x.Id == id);
        var s = p is null ? null : _catalog.ById(p.StrategyId);
        if (s is null)
        {
            Notify("Профили", "Стратегия этого профиля больше не существует.", BalloonIcon.Warning);
            return;
        }
        // Setting the selection flows through StrategyCombo_SelectionChanged (saves + restarts if running).
        StrategyCombo.SelectedItem = s;
        RebuildProfiles();
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
            return;
        _state.Config.Profiles.RemoveAll(x => x.Id == id);
        _state.Save();
        RebuildProfiles();
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
        ProfileNamePanel.Visibility = Visibility.Collapsed;
        DashCustomizePanel.Visibility = Visibility.Collapsed;
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
        Tray.IconSource = running ? TrayOn : TrayOff;

        SetPulse(running && !_busy);

        // A gentle pop on the power icon the moment protection turns on.
        if (running && !_wasRunning)
            PopConnect();
        _wasRunning = running;

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
            EngineStatsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void PopConnect()
    {
        var scale = new ScaleTransform(1, 1);
        ConnectIcon.RenderTransformOrigin = new Point(0.5, 0.5);
        ConnectIcon.RenderTransform = scale;
        var pop = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
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

            var (ram, cpu) = _runner.SampleUsage();
            if (ram > 0)
            {
                EngineStatsText.Text = $"ОЗУ {ram / (1024 * 1024)} МБ · ЦП {cpu:0.#}%";
                EngineStatsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                EngineStatsPanel.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            UptimePanel.Visibility = Visibility.Collapsed;
            EngineStatsPanel.Visibility = Visibility.Collapsed;
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

    // A small status disc for the tray: green when protected, gray when off.
    private static ImageSource MakeTrayIcon(Color fill)
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
            dc.DrawEllipse(new SolidColorBrush(fill),
                new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), 2),
                new Point(16, 16), 13, 13);
        var img = new DrawingImage(dg);
        img.Freeze();
        return img;
    }
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

internal sealed class ProfileVm
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string StrategyLabel { get; init; } = "";
    public Brush Border { get; init; } = Brushes.Transparent;
}

internal sealed class DashCardOption
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public bool Enabled { get; set; }
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
    public Brush Brush { get; }
    public ObservableCollection<CheckRow> Rows { get; }

    public StrategyTestGroup(string label, string summary, Brush brush, ObservableCollection<CheckRow> rows)
    {
        Label = label;
        Summary = summary;
        Brush = brush;
        Rows = rows;
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

/// <summary>Animates a pixel <see cref="GridLength"/> (WPF has no built-in animation for it) - used for the nav pane slide.</summary>
internal sealed class GridLengthAnimation : AnimationTimeline
{
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

    public GridLength From { get => (GridLength)GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public GridLength To { get => (GridLength)GetValue(ToProperty); set => SetValue(ToProperty, value); }
    public IEasingFunction? EasingFunction { get; set; }

    public override Type TargetPropertyType => typeof(GridLength);
    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
    {
        var p = clock.CurrentProgress ?? 0.0;
        if (EasingFunction is not null)
            p = EasingFunction.Ease(p);
        var from = From.Value;
        var to = To.Value;
        return new GridLength(from + (to - from) * p, GridUnitType.Pixel);
    }
}
