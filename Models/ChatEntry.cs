namespace Translator.Models;

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

sealed class ChatEntry : INotifyPropertyChanged
{
    public string Original { get; init; } = "";
    public string Operations { get; init; } = "";
    public DateTime Timestamp { get; init; } = DateTime.Now;

    string _result = "";
    public string Result
    {
        get => _result;
        set
        {
            _result = value;
            Notify();
            Notify(nameof(ResultVis));
            Notify(nameof(ExpandVis));
            Notify(nameof(ExpandText));
        }
    }

    string _errorMsg = "";
    public string ErrorMsg
    {
        get => _errorMsg;
        set { _errorMsg = value; Notify(); }
    }

    bool _processing = true;
    public bool Processing
    {
        get => _processing;
        set
        {
            _processing = value;
            Notify();
            Notify(nameof(ProcessingVis));
            Notify(nameof(ResultVis));
            Notify(nameof(ExpandVis));
        }
    }

    bool _failed;
    public bool Failed
    {
        get => _failed;
        set
        {
            _failed = value;
            Notify();
            Notify(nameof(FailedVis));
            Notify(nameof(ResultVis));
        }
    }

    bool _expanded;
    public bool Expanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            Notify();
            Notify(nameof(OrigMaxLines));
            Notify(nameof(ResMaxLines));
            Notify(nameof(ExpandVis));
            Notify(nameof(ExpandText));
        }
    }

    public string TimeText => Timestamp.ToString("h:mm tt");
    public int OrigMaxLines => _expanded ? 0 : 4;
    public int ResMaxLines => _expanded ? 0 : 8;
    public string ExpandText => _expanded ? "See less" : "See more";

    public Visibility ExpandVis
    {
        get
        {
            if (_processing) return Visibility.Collapsed;
            if (_expanded) return Visibility.Visible;
            return Original.Length > 200 || _result.Length > 400
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public Visibility ProcessingVis =>
        _processing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResultVis =>
        !_processing && !_failed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FailedVis =>
        _failed ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? p = null) =>
        PropertyChanged?.Invoke(this, new(p));
}
