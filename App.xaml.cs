namespace Translator;

using System;
using System.Threading;
using Microsoft.UI.Xaml;

partial class App : Application
{
    static Mutex? _single;
    Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled: {e.Exception}");
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _single = new Mutex(true, "Global\\Translator_7dc1b696", out bool first);
        if (!first)
        {
            Current.Exit();
            return;
        }

        _window = new ShellWindow();
        _window.Activate();
    }
}
