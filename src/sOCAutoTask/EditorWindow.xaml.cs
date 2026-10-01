using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocAutoTask.AppServices;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Editing;
using SocAutoTask.Localization;
using SocAutoTask.Model;

namespace SocAutoTask.Desktop;

/// <summary>Una fila del editor. Los textos se calculan al pedirlos (la lista puede tener cientos de miles).</summary>
public sealed class EventRow(int index, MacroEvent e, long timeMs)
{
    public int Index { get; } = index;
    public string Number => (Index + 1).ToString(Loc.Culture);
    public string Time => EventDescriber.FormatMs(timeMs);
    public string Delay => EventDescriber.FormatMs(e.DelayMs);
    public string Kind => EventDescriber.Kind(e);
    public string Detail => EventDescriber.Detail(e);
}

/// <summary>Editor de eventos (RF-34..40). La logica esta en <see cref="MacroEditor"/>; aqui solo la lista y los botones.</summary>
public partial class EditorWindow : Window
{
    private readonly EditorSession _session;

    public EditorWindow(IEnumerable<MacroEvent> events)
    {
        _session = new EditorSession(events);
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        SetHeaders();
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
        Refresh();
    }

    /// <summary>Los eventos editados, si se pulso Aplicar.</summary>
    public List<MacroEvent>? Result { get; private set; }

    private void OnLanguageChanged()
    {
        SetHeaders();
        Refresh(keepSelection: true);
    }

    private void SetHeaders()
    {
        ColTime.Header = Loc.Get("ColTime");
        ColDelay.Header = Loc.Get("ColDelay");
        ColKind.Header = Loc.Get("ColKind");
        ColDetail.Header = Loc.Get("ColDetail");
    }

    private void Refresh(bool keepSelection = false, int? select = null)
    {
        var first = keepSelection ? SelectedIndices().FirstOrDefault(-1) : select ?? -1;
        var events = _session.Events;
        var rows = new List<EventRow>(events.Count);
        long time = 0;
        for (var i = 0; i < events.Count; i++)
        {
            time += events[i].DelayMs;
            rows.Add(new EventRow(i, events[i], time));
        }
        Grid.ItemsSource = rows;
        if (first >= 0 && rows.Count > 0)
        {
            var row = rows[Math.Min(first, rows.Count - 1)];
            Grid.SelectedItem = row;
            Grid.ScrollIntoView(row);
        }
        SummaryText.Text = Loc.Format("EditorSummary", events.Count, EventDescriber.FormatMs(time));
        UndoButton.IsEnabled = _session.CanUndo;
        UpdateButtons();
    }

    private List<int> SelectedIndices() => Grid.SelectedItems.OfType<EventRow>().Select(r => r.Index).Order().ToList();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var any = Grid.SelectedItems.Count > 0;
        DeleteButton.IsEnabled = any;
        TrimStartButton.IsEnabled = any;
        TrimEndButton.IsEnabled = any;
        SetDelayButton.IsEnabled = any;
        SimplifyButton.IsEnabled = _session.Events.Count > 0;
        ScaleButton.IsEnabled = _session.Events.Count > 0;
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Grid.SelectedItems.Count > 0)
        {
            OnDelete(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnUndo(sender, e);
            e.Handled = true;
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var selected = SelectedIndices();
        if (selected.Count == 0)
            return;
        _session.Apply(list => MacroEditor.Delete(list, selected));
        Refresh(select: selected[0]);
    }

    private void OnTrimStart(object sender, RoutedEventArgs e)
    {
        var selected = SelectedIndices();
        if (selected.Count == 0)
            return;
        _session.Apply(list => MacroEditor.TrimStart(list, selected[0]));
        Refresh(select: 0);
    }

    private void OnTrimEnd(object sender, RoutedEventArgs e)
    {
        var selected = SelectedIndices();
        if (selected.Count == 0)
            return;
        _session.Apply(list => MacroEditor.TrimEnd(list, selected[^1]));
        Refresh(select: selected[^1]);
    }

    private void OnSimplify(object sender, RoutedEventArgs e)
    {
        if (!InputParsing.TryNumber(ToleranceBox.Text, out var tolerance) || tolerance < 0 || tolerance > 500)
        {
            PromptWindow.Alert(this, Loc.Get("EditorTitle"), Loc.Get("InvalidTolerance"));
            return;
        }
        var before = _session.Events.Count;
        var onlyLast = OnlyLastCheck.IsChecked == true;
        _session.Apply(list => MacroEditor.SimplifyMoves(list, tolerance, onlyLast));
        Refresh();
        SummaryText.Text += " · " + Loc.Format("SimplifyRemoved", before - _session.Events.Count);
    }

    private void OnSetDelay(object sender, RoutedEventArgs e)
    {
        var selected = SelectedIndices();
        if (selected.Count == 0)
            return;
        var current = _session.Events[selected[0]].DelayMs;
        if (PromptWindow.AskNumber(this, Loc.Get("EditSetDelay"), Loc.Format("AskDelay", selected.Count), current, 0, 86_400_000) is not { } ms)
            return;
        _session.Apply(list => MacroEditor.SetDelay(list, selected, ms));
        Refresh(select: selected[0]);
    }

    private void OnScale(object sender, RoutedEventArgs e)
    {
        var selected = SelectedIndices();
        var message = selected.Count > 1 ? Loc.Format("AskScaleSelected", selected.Count) : Loc.Get("AskScaleAll");
        if (PromptWindow.AskNumber(this, Loc.Get("EditScale"), message, 100, 0, 10_000) is not { } percent)
            return;
        IReadOnlyCollection<int>? targets = selected.Count > 1 ? selected : null;
        _session.Apply(list => MacroEditor.ScaleDelays(list, targets, percent / 100.0));
        Refresh(keepSelection: true);
    }

    private void OnInsertWait(object sender, RoutedEventArgs e)
    {
        var selected = SelectedIndices();
        var at = selected.Count > 0 ? selected[0] : _session.Events.Count;
        if (PromptWindow.AskNumber(this, Loc.Get("EditInsertWait"), Loc.Get("AskWait"), 1000, 0, 86_400_000) is not { } ms)
            return;
        _session.Apply(list => MacroEditor.InsertWait(list, at, ms));
        Refresh(select: at);
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (!_session.CanUndo)
            return;
        _session.Undo();
        Refresh(keepSelection: true);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        Result = _session.Events;
        DialogResult = true;
    }
}
