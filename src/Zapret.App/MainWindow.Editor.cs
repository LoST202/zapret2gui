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
    private ToolTip? _hoverTip;

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
        EditArgs.TextArea.TextView.MouseHover += Editor_MouseHover;
        EditArgs.TextArea.TextView.MouseHoverStopped += (_, _) => _hoverTip?.IsOpen = false;
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

        var flags = FlagTokenRegex().Matches(text);
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
            var m = LuaDesyncMethodRegex().Match(text);
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

    private void HideHint() => _hintPopup?.IsOpen = false;

    // ===== Hover tooltip (VS Code style) =====

    private void Editor_MouseHover(object sender, MouseEventArgs e)
    {
        var pos = EditArgs.GetPositionFromPoint(e.GetPosition(EditArgs));
        if (pos is null)
            return;

        var item = ResolveTokenAt(EditArgs.Document.GetOffset(pos.Value.Location));
        if (item is null)
            return;

        _hoverTip ??= new ToolTip { Placement = PlacementMode.Mouse };
        _hoverTip.Content = BuildTipContent(item);
        _hoverTip.IsOpen = true;
        e.Handled = true;
    }

    // Expand the offset to a whole token and match it to its reference entry. Bare names can be
    // shared across grammar roles (host is a param AND a pos value; stun is an L7 filter AND a
    // payload; wsize is a desync method AND a param), so resolve by surrounding context rather than
    // a flat lookup that would always surface the first-registered meaning.
    private WinwsItem? ResolveTokenAt(int offset)
    {
        var doc = EditArgs.Document;
        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c is '-' or '_';

        int start = offset, end = offset;
        while (start > 0 && IsWord(doc.GetCharAt(start - 1)))
            start--;
        while (end < doc.TextLength && IsWord(doc.GetCharAt(end)))
            end++;
        if (end <= start)
            return null;

        var word = doc.GetText(start, end - start);
        if (word.Length == 0)
            return null;

        // A whole "--flag" token resolves directly.
        if (word.StartsWith("--", StringComparison.Ordinal))
            return WinwsSyntax.Lookup(word);

        // Otherwise resolve within the role implied by the CLI token this word sits in.
        var tokStart = start;
        while (tokStart > 0 && !char.IsWhiteSpace(doc.GetCharAt(tokStart - 1)))
            tokStart--;
        var prefix = doc.GetText(tokStart, start - tokStart);
        return ResolveByContext(prefix, word) ?? WinwsSyntax.Lookup(word);
    }

    // Resolve a bare name from the flag-value prefix that precedes it on the line.
    private static WinwsItem? ResolveByContext(string prefix, string word)
    {
        var eq = prefix.IndexOf('=');
        if (eq < 0)
            return null;

        var flag = prefix[..eq];
        if (flag.Equals("--filter-l7", StringComparison.OrdinalIgnoreCase))
            return FindIn(WinwsSyntax.L7, word);
        if (flag.Equals("--payload", StringComparison.OrdinalIgnoreCase))
            return FindIn(WinwsSyntax.Payloads, word);
        if (!flag.Equals("--lua-desync", StringComparison.OrdinalIgnoreCase))
            return null;

        // Inside --lua-desync=<method>[:<param>[=<value>]]…
        var val = prefix[(eq + 1)..];
        var colon = val.LastIndexOf(':');
        if (colon < 0)
            return WinwsSyntax.FindMethod(word);        // first segment: the desync method

        var seg = val[(colon + 1)..];                   // current ":"-delimited param segment
        var peq = seg.IndexOf('=');
        if (peq < 0)
            return FindIn(WinwsSyntax.Params, word);    // a param name

        var set = WinwsSyntax.ValuesForParam(seg[..peq]);
        return set is null ? null : FindIn(set, word);  // a param value (e.g. pos=host)
    }

    private static WinwsItem? FindIn(WinwsItem[] arr, string name)
    {
        foreach (var it in arr)
            if (it.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return it;
        return null;
    }

    private static object BuildTipContent(WinwsItem item)
    {
        var panel = new StackPanel { MaxWidth = 480 };

        var title = new TextBlock
        {
            Text = item.Name,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        panel.Children.Add(title);

        var body = new TextBlock { Text = item.Hint, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(body);

        if (item.Group.Length > 0)
        {
            var grp = new TextBlock { Text = item.Group, FontSize = 10, Margin = new Thickness(0, 5, 0, 0) };
            grp.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            panel.Children.Add(grp);
        }
        return panel;
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

    // Source-generated (compile-time) regexes — no runtime pattern compilation on the caret hot path.
    [GeneratedRegex(@"--[a-z0-9\-]+", RegexOptions.IgnoreCase)]
    private static partial Regex FlagTokenRegex();

    [GeneratedRegex(@"--lua-desync=([a-z_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LuaDesyncMethodRegex();
}

internal sealed class CompletionItemData : ICompletionData
{
    private readonly WinwsItem _item;

    public CompletionItemData(WinwsItem item) => _item = item;

    public ImageSource? Image => null;
    public string Text => _item.Name;

    public object Content
    {
        get
        {
            if (_item.Group.Length == 0)
                return _item.Name;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = _item.Name });
            panel.Children.Add(new TextBlock
            {
                Text = "  " + _item.Group,
                Opacity = 0.55,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return panel;
        }
    }

    public object Description =>
        new TextBlock { Text = _item.Hint, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };

    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        => textArea.Document.Replace(completionSegment, _item.Insert);
}

/// <summary>A row in the searchable argument reference (name + description, grouped by category).</summary>
internal sealed record RefItem(string Name, string Hint, string Insert, string Category);
