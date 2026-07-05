using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Zapret.Core;

namespace Zapret.App;

internal sealed partial class ListFileVm : ObservableObject
{
    public string FileName { get; }
    public string FullPath { get; }
    public bool Loaded { get; private set; }

    [ObservableProperty]
    private string _content = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    private int _count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemVisibility))]
    [NotifyPropertyChangedFor(nameof(BadBadge))]
    private int _badCount;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _note = "";

    public ListFileVm(string fileName, string fullPath, int count, int badCount, string note)
    {
        FileName = fileName;
        FullPath = fullPath;
        _count = count;
        _badCount = badCount;
        _note = note ?? "";
    }

    public string Header => $"{FileName}    ·    {Count} строк";
    public Visibility ProblemVisibility => BadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string BadBadge => $"⚠ {BadCount}";

    public void Load()
    {
        if (Loaded)
            return;
        Content = UserLists.ReadText(FullPath);
        Loaded = true;
    }
}
