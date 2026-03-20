namespace Translator;

using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Translator.Models;
using Translator.Services;
using WinRT.Interop;
using Windows.ApplicationModel.DataTransfer;

sealed partial class ShellWindow : Window
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hw);
    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr proc, nint min, nint max);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hw, int cmd);

    readonly ObservableCollection<ChatEntry> _history = [];
    readonly OpenAiClient _ai = new();
    readonly AiDetector _detector = new();
    readonly TrayIcon _tray = new();
    GlobalKeyboard? _kb;
    TextProcessor? _proc;

    string? _apiKey;
    bool _quitting;
    bool _enabled = true;
    GlobalKeyboard.Combo? _savedCombo;

    public ShellWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(440, 660));
        CenterOnScreen();

        HistoryList.ItemsSource = _history;

        AppWindow.Closing += OnWindowClosing;
        ((FrameworkElement)Content).Loaded += OnContentLoaded;

        AppWindow.Changed += (s, a) =>
        {
            if (s.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized)
            {
                p.Restore();
                AppWindow.Hide();
            }
        };
    }

    void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        int cx = (area.WorkArea.Width - 440) / 2;
        int cy = (area.WorkArea.Height - 660) / 2;
        AppWindow.Move(new Windows.Graphics.PointInt32(cx, cy));
    }

    async void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)Content).Loaded -= OnContentLoaded;
        await InitAsync();
    }

    async Task InitAsync()
    {
        _apiKey = Vault.GetApiKey();
        if (_apiKey == null)
            _apiKey = await PromptApiKey();

        if (_apiKey == null)
        {
            _quitting = true;
            Close();
            return;
        }

        _ai.Configure(_apiKey);

        _kb = new GlobalKeyboard();
        _proc = new TextProcessor(_ai, _detector) { AutoSelectAll = Vault.GetAutoSelectAll() };

        GlobalKeyboard.Combo? combo = null;

        var saved = Vault.GetHotkey();
        if (saved != null)
            combo = GlobalKeyboard.Combo.Deserialize(saved);

        if (combo == null)
            combo = await PromptHotkey();

        if (combo == null)
        {
            _quitting = true;
            Close();
            return;
        }

        _savedCombo = combo;
        _kb.SetHotkey(combo);
        HotkeyText.Text = combo.ToString();
        Vault.SetHotkey(combo.Serialize());

        _tray.Create(combo.ToString());
        _tray.ShowRequested += () => DispatcherQueue.TryEnqueue(BringToFront);
        _tray.QuitRequested += () => DispatcherQueue.TryEnqueue(Quit);

        _kb.HotkeyPressed += OnHotkey;
        StatusText.Text = "Ready";
        TrimMemory();

        _ = CheckForUpdates();
    }

    async Task CheckForUpdates()
    {
        var release = await Updater.CheckAsync();
        if (release == null)
        {
            if (Updater.LastCheckError != null && Updater.LastCheckError != "up to date")
                StatusText.Text = $"Update check: {Updater.LastCheckError}";
            return;
        }

        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Update available: v{release.Tag}",
            Content = "Download and install?",
            PrimaryButtonText = "Update",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        UpdateOverlay.Visibility = Visibility.Visible;

        var progress = new Progress<(double pct, string status)>(p =>
        {
            UpdateProgress.Value = p.pct;
            UpdateStatus.Text = p.status;
            UpdatePct.Text = $"{p.pct:F0}%";
        });

        var err = await Updater.DownloadAndRun(release, progress);
        if (err == null)
        {
            Quit();
            return;
        }

        UpdateOverlay.Visibility = Visibility.Collapsed;
        StatusText.Text = $"Update failed: {err}";
    }

    void OnHotkey()
    {
        if (!_enabled) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            StatusRing.IsActive = true;
            StatusText.Text = "Processing\u2026";
        });

        Task.Run(async () =>
        {
            var entry = await _proc!.ProcessAsync();

            DispatcherQueue.TryEnqueue(() =>
            {
                StatusRing.IsActive = false;
                StatusText.Text = _enabled ? "Ready" : "Disabled";

                if (entry == null) return;

                _history.Add(entry);
                EmptyState.Visibility = Visibility.Collapsed;
            });
        });
    }

    void OnToggle(object sender, RoutedEventArgs e)
    {
        if (_kb == null) return;
        _enabled = EnableToggle.IsChecked == true;
        _kb.SetHotkey(_enabled ? _savedCombo : null);
        StatusText.Text = _enabled ? "Ready" : "Disabled";
        ToggleIcon.Glyph = _enabled ? "\uE768" : "\uE769";
    }

    void OnExpand(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ChatEntry entry)
            entry.Expanded = !entry.Expanded;
    }

    void OnCopyResult(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ChatEntry entry && entry.Result.Length > 0)
            CopyText(entry.Result);
    }

    void OnCopyOriginal(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ChatEntry entry)
            CopyText(entry.Original);
    }

    void OnRemoveEntry(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ChatEntry entry)
        {
            _history.Remove(entry);
            if (_history.Count == 0)
                EmptyState.Visibility = Visibility.Visible;
        }
    }

    static void CopyText(string text)
    {
        var pkg = new DataPackage();
        pkg.SetText(text);
        Clipboard.SetContent(pkg);
    }

    void ApplyHotkey(GlobalKeyboard.Combo combo)
    {
        _savedCombo = combo;
        _kb?.SetHotkey(_enabled ? combo : null);
        Vault.SetHotkey(combo.Serialize());
        HotkeyText.Text = combo.ToString();
        _tray.UpdateTip(combo.ToString());
    }

    void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_quitting) return;
        Quit();
    }

    void BringToFront()
    {
        AppWindow.Show();
        var hwnd = WindowNative.GetWindowHandle(this);
        SetForegroundWindow(hwnd);
    }

    void Quit()
    {
        _quitting = true;
        _tray.Dispose();
        _kb?.Dispose();
        _ai.Dispose();
        _detector.Dispose();
        Close();
        Environment.Exit(0);
    }

    void OnClear(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        EmptyState.Visibility = Visibility.Visible;
    }

    static void TrimMemory()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        SetProcessWorkingSetSize(proc.Handle, -1, -1);
    }

    async void OnSettings(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 20, Width = 400 };

        var keyHeader = new TextBlock
        {
            Text = "OpenAI API Key",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
        };
        panel.Children.Add(keyHeader);

        var keyBox = new TextBox
        {
            PlaceholderText = "sk-...",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 13,
            Width = 340,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (_apiKey != null)
            keyBox.Text = $"{_apiKey[..7]}****{_apiKey[^4..]}";
        panel.Children.Add(keyBox);

        var keyLink = new HyperlinkButton
        {
            Content = "Need a key? Get one from OpenAI",
            NavigateUri = new Uri("https://platform.openai.com/api-keys"),
            Padding = new Thickness(0),
            FontSize = 11,
        };
        panel.Children.Add(keyLink);

        var hkHeader = new TextBlock
        {
            Text = "Hotkey",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
        };
        panel.Children.Add(hkHeader);

        var hkCapture = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["CardStrokeColorDefaultBrush"],
            Background = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Padding = new Thickness(14, 10, 14, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var hkLabel = new TextBlock
        {
            Text = _kb?.CurrentHotkey?.ToString() ?? "Not set",
            FontSize = 13,
            Opacity = 0.8,
        };
        hkCapture.Child = hkLabel;

        bool capturing = false;
        hkCapture.Tapped += async (_, _) =>
        {
            if (capturing) return;
            capturing = true;
            hkLabel.Text = "Press a key combo\u2026";
            hkLabel.Opacity = 0.4;

            var c = await _kb!.CaptureAsync();
            if (c != null)
            {
                ApplyHotkey(c);
                hkLabel.Text = c.ToString();
            }
            else
            {
                hkLabel.Text = _kb.CurrentHotkey?.ToString() ?? "Not set";
            }
            hkLabel.Opacity = 0.8;
            capturing = false;
        };
        panel.Children.Add(hkCapture);

        var hkHint = new TextBlock
        {
            Text = "Click the box above, then press your key combo",
            FontSize = 11,
            Opacity = 0.3,
            Margin = new Thickness(0, -14, 0, 0),
        };
        panel.Children.Add(hkHint);

        var selectAllToggle = new ToggleSwitch
        {
            Header = "Auto Select All (Ctrl+A)",
            IsOn = Vault.GetAutoSelectAll(),
            OnContent = "On",
            OffContent = "Off",
        };
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(selectAllToggle, "Automatically selects all text before copying. Disable to only process selected text.");
        panel.Children.Add(selectAllToggle);

        var startupToggle = new ToggleSwitch
        {
            Header = "Start with Windows",
            IsOn = Vault.GetStartup(),
            OnContent = "On",
            OffContent = "Off",
        };
        panel.Children.Add(startupToggle);

        var suffixHeader = new TextBlock
        {
            Text = "Suffixes",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
        };
        panel.Children.Add(suffixHeader);

        var suffixes = new StackPanel { Spacing = 3 };
        suffixes.Children.Add(SuffixRow("--aicheck", "Check AI detection %"));
        suffixes.Children.Add(SuffixRow("--prompt", "Structure into AI prompt"));
        suffixes.Children.Add(SuffixRow("-r", "Answer a question"));
        suffixes.Children.Add(SuffixRow("-df", "Deformalise text"));
        foreach (var (sfx, lang) in OpParser.Languages)
            suffixes.Children.Add(SuffixRow(sfx, $"Translate to {lang}"));
        panel.Children.Add(suffixes);

        var version = typeof(App).Assembly.GetName().Version;
        var versionText = new TextBlock
        {
            Text = $"Version {version?.ToString(3) ?? "?"}",
            FontSize = 11,
            Opacity = 0.25,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        panel.Children.Add(versionText);

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Settings",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = scroll,
            Resources =
            {
                ["ContentDialogMaxWidth"] = 600.0,
                ["ContentDialogMinWidth"] = 400.0,
                ["ContentDialogMaxHeight"] = 800.0,
            },
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            var raw = keyBox.Text?.Trim();
            if (raw != null && raw.StartsWith("sk-") && raw.Length > 20 && !raw.Contains("****"))
            {
                _apiKey = raw;
                Vault.SetApiKey(raw);
                _ai.Configure(raw);
            }

            Vault.SetStartup(startupToggle.IsOn);
            Vault.SetAutoSelectAll(selectAllToggle.IsOn);
            if (_proc != null) _proc.AutoSelectAll = selectAllToggle.IsOn;
        }
    }

    static StackPanel SuffixRow(string sfx, string desc)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new TextBlock
        {
            Text = sfx,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Width = 72,
            Opacity = 0.7,
            FontSize = 12,
        });
        row.Children.Add(new TextBlock { Text = desc, Opacity = 0.5, FontSize = 12 });
        return row;
    }

    async Task<string?> PromptApiKey()
    {
        var box = new PasswordBox { PlaceholderText = "sk-..." };
        var link = new HyperlinkButton
        {
            Content = "Need a key? Get one from OpenAI",
            NavigateUri = new Uri("https://platform.openai.com/api-keys"),
            Padding = new Thickness(0),
            FontSize = 11,
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(link);

        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Enter OpenAI API Key",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Quit",
            DefaultButton = ContentDialogButton.Primary,
        };

        while (true)
        {
            var r = await dlg.ShowAsync();
            if (r != ContentDialogResult.Primary) return null;

            var key = box.Password?.Trim();
            if (key != null && key.StartsWith("sk-") && key.Length > 20)
            {
                Vault.SetApiKey(key);
                return key;
            }

            dlg.Title = "Invalid key \u2014 must start with sk-";
        }
    }

    async Task<GlobalKeyboard.Combo?> PromptHotkey()
    {
        var label = new TextBlock
        {
            Text = "Press any key combination\u2026",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.7,
        };

        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Set Hotkey",
            Content = label,
            CloseButtonText = "Cancel",
        };

        GlobalKeyboard.Combo? result = null;
        var captureTask = _kb!.CaptureAsync();

        _ = captureTask.ContinueWith(t =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                result = t.Result;
                if (result != null)
                    label.Text = result.ToString();
                dlg.Hide();
            });
        });

        await dlg.ShowAsync();
        return result;
    }
}
