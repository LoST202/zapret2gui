using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Xml;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;

namespace Zapret.App;

public partial class MainWindow
{
    private CompletionWindow? _completion;
    private Popup? _hintPopup;
    private TextBlock? _hintText;
    private CollectionViewSource? _refView;

    // Wire up the winws2 code editor: syntax highlighting, context-aware autocomplete,
    // a floating parameter hint (VS Code style), and the argument reference palette.
    private void SetupStrategyEditor()
    {
        try
        {
            using var stream = typeof(MainWindow).Assembly
                .GetManifestResourceStream("Zapret.App.Assets.WinwsHighlighting.xshd");
            if (stream is not null)
            {
                using var reader = new XmlTextReader(stream);
                EditArgs.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
        }
        catch { /* highlighting is a nicety; the editor still works plain */ }

        BuildHintPopup();
        PopulateReference();

        EditArgs.TextArea.TextEntered += Editor_TextEntered;
        EditArgs.TextArea.Caret.PositionChanged += (_, _) => UpdateEditorHint();
        EditArgs.TextArea.LostKeyboardFocus += (_, _) => HideHint();
        EditArgs.TextArea.TextView.ScrollOffsetChanged += (_, _) => { if (_hintPopup?.IsOpen == true) PositionHint(); };
        EditArgs.TextArea.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ShowCompletion();
                e.Handled = true;
            }
        };
    }

    // ===== Autocomplete =====

    private void Editor_TextEntered(object sender, TextCompositionEventArgs e)
    {
        if (_completion is not null || e.Text.Length != 1)
            return;
        var c = e.Text[0];
        if (c is '-' or '=' or ':' or ',' || char.IsLetter(c))
            ShowCompletion();
    }

    private void ShowCompletion()
    {
        var ctx = ComputeCompletion();
        if (ctx is null)
            return;

        _completion = new CompletionWindow(EditArgs.TextArea) { StartOffset = ctx.Value.Start };
        foreach (var item in ctx.Value.Items)
            _completion.CompletionList.CompletionData.Add(new CompletionItemData(item));
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    private (int Start, WinwsItem[] Items)? ComputeCompletion()
    {
        var (token, start) = CurrentToken();
        if (token.Length == 0 || token[0] != '-')
            return null;

        var eq = token.IndexOf('=');
        if (eq < 0)
            return (start, WinwsSyntax.Flags);

        var flag = token[..eq];
        var value = token[(eq + 1)..];
        var valueStart = start + eq + 1;

        return flag switch
        {
            "--filter-l7" => (LastSegmentStart(value, valueStart, ','), WinwsSyntax.L7),
            "--payload" => (LastSegmentStart(value, valueStart, ','), WinwsSyntax.Payloads),
            "--lua-desync" => LuaDesyncCompletion(value, valueStart),
            _ => null,
        };
    }

    private static (int Start, WinwsItem[] Items)? LuaDesyncCompletion(string value, int valueStart)
    {
        var colon = value.IndexOf(':');
        if (colon < 0)
            return (valueStart, WinwsSyntax.Methods);

        var segSep = Math.Max(value.LastIndexOf(':'), value.LastIndexOf(','));
        var seg = value[(segSep + 1)..];
        var segStart = valueStart + segSep + 1;

        var peq = seg.IndexOf('=');
        if (peq < 0)
            return (segStart, WinwsSyntax.Params);

        var set = WinwsSyntax.ValuesForParam(seg[..peq]);
        if (set is null)
            return null;

        var pval = seg[(peq + 1)..];
        var pvalStart = segStart + peq + 1 + pval.LastIndexOf(',') + 1;
        return (pvalStart, set);
    }

    private static int LastSegmentStart(string value, int valueStart, char sep)
        => valueStart + value.LastIndexOf(sep) + 1;

    private (string Text, int Start) CurrentToken()
    {
        var caret = EditArgs.CaretOffset;
        var doc = EditArgs.Document;
        var start = caret;
        while (start > 0 && !char.IsWhiteSpace(doc.GetCharAt(start - 1)))
            start--;
        return (doc.GetText(start, caret - start), start);
    }

    // ===== Floating parameter hint =====

    private void BuildHintPopup()
    {
        _hintText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        _hintText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Child = _hintText,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4, Color = Colors.Black },
        };
        border.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "AccentTextFillColorPrimaryBrush");

        _hintPopup = new Popup
        {
            Child = border,
            Placement = PlacementMode.Relative,
            PlacementTarget = EditArgs.TextArea.TextView,
            StaysOpen = true,
            AllowsTransparency = true,
            Focusable = false,
        };
    }

    // Show the hint for the flag (and desync method) the caret currently sits after on this line.
    private void UpdateEditorHint()
    {
        if (_hintPopup is null || _hintText is null)
            return;

        var caret = EditArgs.CaretOffset;
        var doc = EditArgs.Document;
        if (caret == 0 || doc.TextLength == 0)
        {
            HideHint();
            return;
        }

        var line = doc.GetLineByOffset(caret);
        var text = doc.GetText(line.Offset, caret - line.Offset);

        var flags = Regex.Matches(text, "--[a-z0-9\\-]+", RegexOptions.IgnoreCase);
        if (flags.Count == 0)
        {
            HideHint();
            return;
        }

        var flagName = flags[^1].Value;
        var flag = WinwsSyntax.FindFlag(flagName);
        if (flag is null)
        {
            HideHint();
            return;
        }

        var hint = $"{flag.Name}  —  {flag.Hint}";
        if (flagName.Equals("--lua-desync", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(text, "--lua-desync=([a-z_]+)", RegexOptions.IgnoreCase);
            if (m.Success && WinwsSyntax.FindMethod(m.Groups[1].Value) is { } method)
                hint = $"{method.Name}  —  {method.Hint}";
        }

        _hintText.Text = hint;
        PositionHint();
        _hintPopup.IsOpen = true;
    }

    private void PositionHint()
    {
        if (_hintPopup is null)
            return;
        try
        {
            var view = EditArgs.TextArea.TextView;
            view.EnsureVisualLines();
            var pt = view.GetVisualPosition(EditArgs.TextArea.Caret.Position, VisualYPosition.LineTop);
            var rel = pt - view.ScrollOffset;
            _hintPopup.HorizontalOffset = rel.X;
            var above = rel.Y - 34;
            _hintPopup.VerticalOffset = above < 0 ? rel.Y + 20 : above;
        }
        catch { /* visual lines not ready yet */ }
    }

    private void HideHint()
    {
        if (_hintPopup is not null)
            _hintPopup.IsOpen = false;
    }

    // ===== Argument reference palette =====

    private void PopulateReference()
    {
        var items = new List<RefItem>();
        foreach (var f in WinwsSyntax.Flags)
            items.Add(new RefItem(f.Name, f.Hint, f.Insert, f.Group.Length > 0 ? f.Group : "Флаги"));
        foreach (var m in WinwsSyntax.Methods)
            items.Add(new RefItem(m.Name, m.Hint, m.Insert, "Методы desync"));
        foreach (var p in WinwsSyntax.Params)
            items.Add(new RefItem(p.Name, p.Hint, p.Insert, "Параметры desync"));
        foreach (var v in WinwsSyntax.L7)
            items.Add(new RefItem(v.Name, v.Hint, v.Insert, "Значения --filter-l7"));
        foreach (var v in WinwsSyntax.Payloads)
            items.Add(new RefItem(v.Name, v.Hint, v.Insert, "Значения --payload"));
        foreach (var v in WinwsSyntax.PosMarkers)
            items.Add(new RefItem(v.Name, v.Hint, v.Insert, "Позиции (pos)"));

        _refView = new CollectionViewSource { Source = items };
        _refView.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        _refView.Filter += RefFilter;
        RefList.ItemsSource = _refView.View;
    }

    private void RefSearch_TextChanged(object sender, TextChangedEventArgs e) => _refView?.View.Refresh();

    private void RefFilter(object sender, FilterEventArgs e)
    {
        var q = RefSearch.Text?.Trim();
        if (string.IsNullOrEmpty(q))
        {
            e.Accepted = true;
            return;
        }
        e.Accepted = e.Item is RefItem it &&
                     (it.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                      it.Hint.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void RefChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string insert })
        {
            EditArgs.Document.Insert(EditArgs.CaretOffset, insert);
            EditArgs.Focus();
        }
    }
}

internal sealed class CompletionItemData : ICompletionData
{
    private readonly WinwsItem _item;

    public CompletionItemData(WinwsItem item) => _item = item;

    public ImageSource? Image => null;
    public string Text => _item.Name;
    public object Content => _item.Name;
    public object Description => _item.Hint;
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        => textArea.Document.Replace(completionSegment, _item.Insert);
}

/// <summary>A row in the searchable argument reference (name + description, grouped by category).</summary>
internal sealed record RefItem(string Name, string Hint, string Insert, string Category);
