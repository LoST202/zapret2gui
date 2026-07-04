using System.ComponentModel;
using System.Windows;
using Zapret.Core;

namespace Zapret.App;

internal sealed class ListFileVm : INotifyPropertyChanged
{
    public string FileName { get; }
    public string FullPath { get; }
    public bool Loaded { get; private set; }

    private string _content = "";
    private int _count;
    private int _badCount;
    private bool _isSelected;
    private string _note = "";

    public ListFileVm(string fileName, string fullPath, int count, int badCount, string note)
    {
        FileName = fileName;
        FullPath = fullPath;
        _count = count;
        _badCount = badCount;
        _note = note ?? "";
    }

    public string Content
    {
        get => _content;
        set { _content = value ?? ""; OnChanged(nameof(Content)); }
    }

    public int Count
    {
        get => _count;
        set { _count = value; OnChanged(nameof(Header)); }
    }

    public int BadCount
    {
        get => _badCount;
        set
        {
            _badCount = value;
            OnChanged(nameof(BadCount));
            OnChanged(nameof(HasProblem));
            OnChanged(nameof(ProblemVisibility));
            OnChanged(nameof(BadBadge));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnChanged(nameof(IsSelected)); }
    }

    public string Note
    {
        get => _note;
        set { _note = value ?? ""; OnChanged(nameof(Note)); }
    }

    public string Header => $"{FileName}    ·    {Count} строк";

    public bool HasProblem => _badCount > 0;
    public Visibility ProblemVisibility => _badCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string BadBadge => $"⚠ {_badCount}";

    public void Load()
    {
        if (Loaded)
            return;
        Content = UserLists.ReadText(FullPath);
        Loaded = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
