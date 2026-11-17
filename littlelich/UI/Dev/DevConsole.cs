using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LittleLich.UI.Dev;

/// <summary>
/// Right-hand panel: event log with category filters, plus a command line.
/// Tab autocompletes (commands, then arguments from content ids, stats, slots...), Up/Down walks history.
/// </summary>
public partial class DevConsole : VBoxContainer
{
    private readonly RichTextLabel _log;
    private readonly LineEdit _input;
    private readonly Label _hint;
    private readonly HashSet<LogCategory> _hidden = new();
    private readonly List<string> _history = new();
    private int _historyIndex;

    /// <summary>Raised after a command ran (so the shell can refresh panels).</summary>
    public event Action? CommandRan;

    public DevConsole()
    {
        CustomMinimumSize = new Vector2(360, 0);

        var head = new HBoxContainer();
        AddChild(head);
        head.AddChild(new Label { Text = "Log", ThemeTypeVariation = "HeaderSmall", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var clear = new Button { Text = "Clear" };
        clear.Pressed += DevLog.Clear;
        head.AddChild(clear);

        var filters = new HFlowContainer();
        AddChild(filters);
        foreach (var cat in Enum.GetValues<LogCategory>())
        {
            var cb = new CheckBox { Text = cat.ToString(), ButtonPressed = true };
            cb.Toggled += on =>
            {
                if (on) _hidden.Remove(cat); else _hidden.Add(cat);
                Rebuild();
            };
            filters.AddChild(cb);
        }

        _log = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.Click,
        };
        AddChild(_log);

        _hint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.7f) };
        AddChild(_hint);

        _input = new LineEdit { PlaceholderText = "command (help, Tab completes)", ClearButtonEnabled = true };
        _input.TextSubmitted += Submit;
        _input.GuiInput += OnInputKey;
        AddChild(_input);

        DevLog.Added += Append;
        DevLog.Cleared += Rebuild;
    }

    public override void _ExitTree()
    {
        DevLog.Added -= Append;
        DevLog.Cleared -= Rebuild;
    }

    public override void _Ready() => Rebuild();

    public void FocusInput() => _input.GrabFocus();

    /// <summary>Runs a line as if typed (also used by --dev-cmd).</summary>
    public void Run(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return;
        DevLog.Add(LogCategory.Command, "> " + line);
        try
        {
            if (DevCommands.Execute(line) is { Length: > 0 } output)
                DevLog.Add(LogCategory.Command, output);
        }
        catch (DevCommandException e)
        {
            DevLog.Error(e.Message);
        }
        catch (Exception e)
        {
            DevLog.Error($"{e.GetType().Name}: {e.Message}");
        }
        CommandRan?.Invoke();
    }

    private void Submit(string text)
    {
        if (text.Trim().Length > 0 && (_history.Count == 0 || _history[^1] != text)) _history.Add(text);
        _historyIndex = _history.Count;
        _input.Clear();
        _hint.Text = "";
        Run(text);
    }

    private void OnInputKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return;
        switch (k.Keycode)
        {
            case Key.Tab:
                Complete();
                _input.AcceptEvent();
                break;
            case Key.Up when _history.Count > 0:
                _historyIndex = Math.Max(0, _historyIndex - 1);
                SetInput(_history[_historyIndex]);
                _input.AcceptEvent();
                break;
            case Key.Down when _history.Count > 0:
                _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
                SetInput(_historyIndex < _history.Count ? _history[_historyIndex] : "");
                _input.AcceptEvent();
                break;
        }
    }

    private void Complete()
    {
        var text = _input.Text;
        var options = DevCommands.Completions(text);
        if (options.Count == 0)
        {
            _hint.Text = "(no completions)";
            return;
        }
        var start = text.Length == 0 || char.IsWhiteSpace(text[^1]) ? text.Length : text.LastIndexOf(' ') + 1;
        var word = text[start..];
        var common = CommonPrefix(options);
        if (common.Length > word.Length || options.Count == 1)
            SetInput(text[..start] + common + (options.Count == 1 ? " " : ""));
        _hint.Text = options.Count == 1 ? "" : string.Join("  ", options.Take(40)) + (options.Count > 40 ? $"  (+{options.Count - 40})" : "");
    }

    private void SetInput(string s)
    {
        _input.Text = s;
        _input.CaretColumn = s.Length;
    }

    private static string CommonPrefix(IReadOnlyList<string> xs)
    {
        var p = xs[0];
        foreach (var x in xs.Skip(1))
        {
            var n = 0;
            while (n < p.Length && n < x.Length && char.ToLowerInvariant(p[n]) == char.ToLowerInvariant(x[n])) n++;
            p = p[..n];
        }
        return p;
    }

    private void Append(DevLogEntry e)
    {
        if (_hidden.Contains(e.Category)) return;
        _log.AppendText(Format(e));
    }

    private void Rebuild()
    {
        _log.Clear();
        foreach (var e in DevLog.Entries)
            if (!_hidden.Contains(e.Category)) _log.AppendText(Format(e));
    }

    private static string Format(DevLogEntry e)
    {
        var color = e.Category switch
        {
            LogCategory.Error => "#ff6b6b",
            LogCategory.Command => "#9ecbff",
            LogCategory.Save => "#b4e6a0",
            LogCategory.Pool => "#e6d27a",
            _ => "#d0d0d0",
        };
        var lines = DevFormat.Bb(e.Text).Replace("\n", "\n    ");
        return $"[color=#808080]{e.Time:HH:mm:ss}[/color] [color={color}][lb]{e.Category}] {lines}[/color]\n";
    }
}
