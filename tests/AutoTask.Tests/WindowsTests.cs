using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using SocAutoTask.AppServices;
using SocAutoTask.Desktop;
using SocAutoTask.Desktop.Controls;
using SocAutoTask.Desktop.Localization;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Editing;
using SocAutoTask.Input;
using SocAutoTask.Localization;
using SocAutoTask.Model;

namespace SocAutoTask.Tests;

/// <summary>Ajustes, editor, guia, «Acerca de», novedades, dialogos, controles, tema y arranque.</summary>
public sealed class WindowsTests : UiTest
{
    // ------------------------------------------------------------ ajustes

    private static void Settings(AppSettings settings, PlaybackOptions playback, Action<SettingsWindow> act, out bool? result, out PlaybackOptions chosen)
    {
        var w = new SettingsWindow(settings, playback);
        Ui.Expect(act);
        result = w.ShowDialog();
        chosen = w.Playback;
    }

    [Fact]
    public void Ajustes_EnseñanLoGuardado()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            settings.CountdownSeconds = 3;
            settings.Emergency = EmergencyKeys.Pause | EmergencyKeys.EscapeHold;
            settings.EscapeHoldMs = 1500;
            settings.RecordMouseMoves = false;
            settings.Theme = AppTheme.Light;
            var playback = new PlaybackOptions { Speed = 3, Repeat = RepeatMode.Times, Times = 4, PauseBetweenMs = 2500 };
            Settings(settings, playback, s =>
            {
                Assert.Equal(PlaybackOptions.PresetSpeeds.Length, Ui.Find<ComboBox>(s, "SpeedCombo").SelectedIndex);   // 3x: personalizada
                Assert.True(Ui.Find<TextBox>(s, "CustomSpeedBox").IsEnabled);
                Assert.Equal("3", Ui.Find<TextBox>(s, "CustomSpeedBox").Text);
                Assert.True(Ui.Find<RadioButton>(s, "TimesRadio").IsChecked);
                Assert.Equal("4", Ui.Find<TextBox>(s, "TimesBox").Text);
                Assert.Equal("2,5", Ui.Find<TextBox>(s, "PauseBox").Text);
                Assert.Equal("3", Ui.Find<TextBox>(s, "CountdownBox").Text);
                Assert.True(Ui.Find<CheckBox>(s, "PauseKeyCheck").IsChecked);
                Assert.False(Ui.Find<CheckBox>(s, "ScrollLockCheck").IsChecked);
                Assert.Equal("1,5", Ui.Find<TextBox>(s, "EscapeHoldBox").Text);
                Assert.False(Ui.Find<CheckBox>(s, "MovesCheck").IsChecked);
                Assert.Equal(1, Ui.Find<ComboBox>(s, "ThemeCombo").SelectedIndex);
                Assert.True(Ui.Find<CheckBox>(s, "AssociateCheck").IsEnabled);
                Assert.False(Ui.Find<CheckBox>(s, "AssociateCheck").IsChecked);
                Assert.Contains(Temp.Path, Ui.Find<TextBlock>(s, "DataFolderText").Text);
                // velocidad de la lista: la casilla se apaga
                Ui.Find<ComboBox>(s, "SpeedCombo").SelectedIndex = 1;
                Assert.False(Ui.Find<TextBox>(s, "CustomSpeedBox").IsEnabled);
                s.Close();
            }, out var result, out var chosen);
            Assert.NotEqual(true, result);
            Assert.Equal(playback, chosen);
        });
    }

    [Fact]
    public void Ajustes_Guardar_CambiaTodo_YAsocia()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            Settings(settings, new PlaybackOptions(), s =>
            {
                Ui.Find<ComboBox>(s, "SpeedCombo").SelectedIndex = 2;   // 2x
                Ui.Find<RadioButton>(s, "ContinuousRadio").IsChecked = true;
                Ui.Find<TextBox>(s, "PauseBox").Text = "1";
                Ui.Find<TextBox>(s, "CountdownBox").Text = "5";
                Ui.Find<CheckBox>(s, "ScrollLockCheck").IsChecked = false;
                Ui.Find<TextBox>(s, "EscapeHoldBox").Text = "2";
                Ui.Find<CheckBox>(s, "KeyboardCheck").IsChecked = false;
                Ui.Find<CheckBox>(s, "MovesCheck").IsChecked = false;
                Ui.Find<CheckBox>(s, "LabelsCheck").IsChecked = false;
                Ui.Find<CheckBox>(s, "TrayCheck").IsChecked = false;
                Ui.Find<CheckBox>(s, "AssociateCheck").IsChecked = true;
                Ui.Find<HotkeyBox>(s, "RecordHotkeyBox").Hotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x7B);
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
            }, out var result, out var chosen);
            Assert.True(result);
            Assert.Equal(new PlaybackOptions { Speed = 2, Repeat = RepeatMode.Continuous, PauseBetweenMs = 1000 }, chosen);
            Assert.Equal(5, settings.CountdownSeconds);
            Assert.Equal(EmergencyKeys.Pause | EmergencyKeys.EscapeHold, settings.Emergency);
            Assert.Equal(2000, settings.EscapeHoldMs);
            Assert.False(settings.RecordKeyboard);
            Assert.False(settings.RecordMouseMoves);
            Assert.False(settings.ShowLabels);
            Assert.False(settings.TrayOnMinimize);
            Assert.Equal(new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x7B), settings.RecordKey);
        });
        Assert.True(FileAssociation.IsApplied(Fake.ExePath!, Fake.AssociationRoot));

        // y al volver a abrir sale marcada; desmarcarla la quita
        Ui.Run(() =>
        {
            Settings(Settings(), new PlaybackOptions(), s =>
            {
                Assert.True(Ui.Find<CheckBox>(s, "AssociateCheck").IsChecked);
                Ui.Find<CheckBox>(s, "AssociateCheck").IsChecked = false;
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
            }, out _, out _);
        });
        Assert.False(FileAssociation.IsApplied(Fake.ExePath!, Fake.AssociationRoot));
    }

    [Theory]
    [InlineData("CustomSpeedBox", "abc", null)]
    [InlineData("CountdownBox", "99", "InvalidCountdown")]
    [InlineData("EscapeHoldBox", "0", "InvalidEscapeHold")]
    [InlineData("TimesBox", "0", null)]
    public void Ajustes_UnValorQueNoVale_SeDiceYNoSeCierra(string box, string value, string? key)
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            string? message = null;
            Settings(settings, new PlaybackOptions { Speed = 3 }, s =>
            {
                if (box == "TimesBox")
                    Ui.Find<RadioButton>(s, "TimesRadio").IsChecked = true;
                Ui.Find<TextBox>(s, box).Text = value;
                Ui.Answer("OkButton", m => message = m);
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
                Assert.True(s.IsVisible);
                s.Close();
            }, out var result, out _);
            Assert.NotEqual(true, result);
            Assert.NotNull(message);
            if (key is not null)
                Assert.Equal(Loc.Get(key), message);
            Assert.Equal(0, settings.CountdownSeconds);
        });
    }

    [Fact]
    public void Ajustes_Atajos_SinModificadorOIguales_NoValen()
    {
        Ui.Run(() =>
        {
            var messages = new List<string>();
            Settings(Settings(), new PlaybackOptions(), s =>
            {
                var record = Ui.Find<HotkeyBox>(s, "RecordHotkeyBox");
                var play = Ui.Find<HotkeyBox>(s, "PlayHotkeyBox");
                record.Hotkey = new Hotkey(HotkeyModifiers.None, 0x41);
                Ui.Answer("OkButton", messages.Add);
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
                record.Hotkey = play.Hotkey;
                Ui.Answer("OkButton", messages.Add);
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
                s.Close();
            }, out _, out _);
            Assert.Equal([Loc.Get("HotkeyNeedsModifier"), Loc.Get("HotkeysEqual")], messages);
        });
    }

    [Fact]
    public void Ajustes_SinParadaDeEmergencia_SePreguntaAntes()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            void Uncheck(SettingsWindow s)
            {
                Ui.Find<CheckBox>(s, "PauseKeyCheck").IsChecked = false;
                Ui.Find<CheckBox>(s, "ScrollLockCheck").IsChecked = false;
                Ui.Find<CheckBox>(s, "EscapeCheck").IsChecked = false;
            }
            Settings(settings, new PlaybackOptions(), s =>
            {
                Uncheck(s);
                Ui.Answer("CancelButton");
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
                Assert.True(s.IsVisible);
                s.Close();
            }, out var first, out _);
            Assert.NotEqual(true, first);
            Assert.Equal(EmergencyKeys.All, settings.Emergency);
            Settings(settings, new PlaybackOptions(), s =>
            {
                Uncheck(s);
                Ui.Answer("OkButton");
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
            }, out var second, out _);
            Assert.True(second);
            Assert.Equal(EmergencyKeys.None, settings.Emergency);
        });
    }

    [Fact]
    public void Ajustes_TemaEIdioma_SeAplicanAlMomento()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            Settings(settings, new PlaybackOptions(), s =>
            {
                Ui.Find<ComboBox>(s, "ThemeCombo").SelectedIndex = 2;   // oscuro
                Assert.True(ThemeManager.IsDark);
                Assert.Equal(AppTheme.Dark, settings.Theme);
                Ui.Click(Ui.Find<Button>(s, "EnglishButton"));
                Assert.Equal("en", Loc.Language);
                Assert.Equal("en", settings.Language);
                Assert.Equal(Loc.Get("ThemeDark"), ((ComboBoxItem)Ui.Find<ComboBox>(s, "ThemeCombo").SelectedItem).Content);
                Assert.Equal(Loc.Get("CustomSpeed"), ((ComboBoxItem)Ui.Find<ComboBox>(s, "SpeedCombo").Items[^1]).Content);
                Assert.Equal(s.FindResource("FilledButton"), Ui.Find<Button>(s, "EnglishButton").Style);
                Ui.Click(Ui.Find<Button>(s, "SpanishButton"));
                Assert.Equal("es", Loc.Language);
                Ui.Find<ComboBox>(s, "ThemeCombo").SelectedIndex = 1;   // claro
                Assert.False(ThemeManager.IsDark);
                s.Close();
            }, out _, out _);
        });
    }

    [Fact]
    public void Ajustes_EnElPaquete_LaAsociacionLaPoneElPaquete()
    {
        Fake.IsPackaged = true;
        Ui.Run(() =>
        {
            Settings(Settings(), new PlaybackOptions(), s =>
            {
                var check = Ui.Find<CheckBox>(s, "AssociateCheck");
                Assert.False(check.IsEnabled);
                Assert.True(check.IsChecked);
                Assert.Equal(Loc.Get("AssociatePackaged"), Ui.Find<TextBlock>(s, "AssociateHint").Text);
                Loc.Use("en");
                Assert.Equal(Loc.Get("AssociatePackaged"), Ui.Find<TextBlock>(s, "AssociateHint").Text);
                Loc.Use("es");
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
            }, out var result, out _);
            Assert.True(result);
        });
    }

    [Fact]
    public void Ajustes_SiNoSePuedeAsociar_SeAvisa()
    {
        Fake.AssociationRoot = Registry.CurrentUser.OpenSubKey(FakePlatform.RegistryBranch, writable: false)!;
        Ui.Run(() =>
        {
            string? message = null;
            Settings(Settings(), new PlaybackOptions(), s =>
            {
                Ui.Find<CheckBox>(s, "AssociateCheck").IsChecked = true;
                Ui.Answer("OkButton", m => message = m);
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
            }, out var result, out _);
            Assert.True(result);
            Assert.Equal(Loc.Get("AssociateFailed"), message);
        });
    }

    // ------------------------------------------------------------ editor

    private static List<MacroEvent> Moves(int n) =>
        [.. Enumerable.Range(0, n).Select(i => MacroEvent.Move(i, i, 10))];

    private static List<MacroEvent>? Edit(IEnumerable<MacroEvent> events, Action<EditorWindow, DataGrid> act)
    {
        var w = new EditorWindow(events);
        Ui.Expect<EditorWindow>(e => act(e, Ui.Find<DataGrid>(e, "Grid")));
        w.ShowDialog();
        return w.Result;
    }

    private static void Press(EditorWindow e, string button) => Ui.Click(Ui.Find<Button>(e, button));

    [Fact]
    public void Editor_FilasYResumen()
    {
        Ui.Run(() =>
        {
            var result = Edit(MainWindowTests.Events(), (e, grid) =>
            {
                var rows = ((IEnumerable<EventRow>)grid.ItemsSource).ToList();
                Assert.Equal(5, rows.Count);
                Assert.Equal("1", rows[0].Number);
                Assert.Equal(EventDescriber.FormatMs(50), rows[1].Time);
                Assert.Equal(EventDescriber.FormatMs(40), rows[2].Delay);
                Assert.Equal(EventDescriber.Kind(MainWindowTests.Events()[3]), rows[3].Kind);
                Assert.Equal(EventDescriber.Detail(MainWindowTests.Events()[3]), rows[3].Detail);
                Assert.Equal(Loc.Format("EditorSummary", 5, EventDescriber.FormatMs(140)), Ui.Find<TextBlock>(e, "SummaryText").Text);
                Assert.False(Ui.Find<Button>(e, "DeleteButton").IsEnabled);
                Assert.False(Ui.Find<Button>(e, "UndoButton").IsEnabled);
                Assert.True(Ui.Find<Button>(e, "SimplifyButton").IsEnabled);
                Loc.Use("en");
                Assert.Equal(Loc.Get("ColTime"), grid.Columns[1].Header);
                Loc.Use("es");
                e.Close();
            });
            Assert.Null(result);
        });
    }

    [Fact]
    public void Editor_Recortar_Borrar_Deshacer_YAplicar()
    {
        Ui.Run(() =>
        {
            var result = Edit(Moves(10), (e, grid) =>
            {
                grid.SelectedIndex = 2;
                Press(e, "TrimStartButton");          // quita 0 y 1
                Assert.Equal(8, grid.Items.Count);
                grid.SelectedIndex = 5;
                Press(e, "TrimEndButton");            // deja hasta la 5
                Assert.Equal(6, grid.Items.Count);
                grid.SelectedIndex = 0;
                Press(e, "DeleteButton");
                Assert.Equal(5, grid.Items.Count);
                Assert.True(Ui.Find<Button>(e, "UndoButton").IsEnabled);
                Press(e, "UndoButton");
                Assert.Equal(6, grid.Items.Count);
                Press(e, "ApplyButton");
            });
            Assert.Equal(6, result!.Count);
            Assert.Equal(2, result[0].X);
        });
    }

    [Fact]
    public void Editor_SinSeleccion_LosBotonesDeFilaNoHacenNada()
    {
        Ui.Run(() =>
        {
            Edit(Moves(3), (e, grid) =>
            {
                foreach (var name in new[] { "OnDelete", "OnTrimStart", "OnTrimEnd", "OnSetDelay", "OnUndo" })
                    Ui.Call(e, name, e, new RoutedEventArgs());
                Assert.Equal(3, grid.Items.Count);
                e.Close();
            });
        });
    }

    [Fact]
    public void Editor_Simplificar_ConToleranciaValidaYNoValida()
    {
        Ui.Run(() =>
        {
            var result = Edit([MacroEvent.Move(0, 0), .. Enumerable.Range(1, 9).Select(i => MacroEvent.Move(i * 10, 0, 5)), MacroEvent.Down(Model.MouseButton.Left, 90, 0)], (e, grid) =>
            {
                Ui.Find<TextBox>(e, "ToleranceBox").Text = "x";
                string? message = null;
                Ui.Answer("OkButton", m => message = m);
                Press(e, "SimplifyButton");
                Assert.Equal(Loc.Get("InvalidTolerance"), message);
                Ui.Find<TextBox>(e, "ToleranceBox").Text = "3";
                Press(e, "SimplifyButton");
                Assert.Contains(Loc.Format("SimplifyRemoved", 8), Ui.Find<TextBlock>(e, "SummaryText").Text);
                Press(e, "ApplyButton");
            });
            Assert.Equal(3, result!.Count);
        });
    }

    [Fact]
    public void Editor_Espera_Escala_EInsertar_PidenNumero()
    {
        Ui.Run(() =>
        {
            var result = Edit(Moves(4), (e, grid) =>
            {
                grid.SelectedIndex = 1;
                Ui.AnswerNumber("250");
                Press(e, "SetDelayButton");
                Ui.AnswerNumber(null);           // cancelar: nada
                Press(e, "SetDelayButton");
                grid.SelectedItems.Clear();
                Ui.AnswerNumber("200");          // todas al doble
                Press(e, "ScaleButton");
                grid.SelectedItems.Add(grid.Items[0]);
                grid.SelectedItems.Add(grid.Items[1]);
                Ui.AnswerNumber("50");           // las dos elegidas a la mitad
                Press(e, "ScaleButton");
                Ui.AnswerNumber(null);
                Press(e, "ScaleButton");
                grid.SelectedItems.Clear();
                Ui.AnswerNumber("1000");         // al final
                Press(e, "InsertWaitButton");
                grid.SelectedIndex = 0;
                Ui.AnswerNumber(null);
                Press(e, "InsertWaitButton");
                Press(e, "ApplyButton");
            });
            Assert.Equal(5, result!.Count);
            Assert.Equal(10, result[0].DelayMs);   // 10 *2 /2
            Assert.Equal(250, result[1].DelayMs);  // 250 *2 /2
            Assert.Equal(20, result[2].DelayMs);
            Assert.Equal(EventKind.Wait, result[4].Kind);
        });
    }

    [Fact]
    public void Editor_UnNumeroFueraDeRango_SeVuelveAPedir()
    {
        Ui.Run(() =>
        {
            Edit(Moves(2), (e, grid) =>
            {
                grid.SelectedIndex = 0;
                Ui.AnswerNumber("-5");
                string? message = null;
                Ui.Answer("OkButton", m => message = m);
                Ui.AnswerNumber("7");
                Press(e, "SetDelayButton");
                Assert.Equal(Loc.Format("NumberOutOfRange", 0, 86_400_000), message);
                Assert.Equal(EventDescriber.FormatMs(7), ((EventRow)grid.Items[0]).Delay);
                e.Close();
            });
        });
    }

    [Fact]
    public void Editor_TeclaSupr_Borra()
    {
        Ui.Run(() =>
        {
            Edit(Moves(3), (e, grid) =>
            {
                grid.SelectedIndex = 0;
                var source = PresentationSource.FromVisual(e)!;
                var del = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Delete) { RoutedEvent = Keyboard.KeyDownEvent };
                Ui.Call(e, "OnGridKeyDown", grid, del);
                Assert.True(del.Handled);
                Assert.Equal(2, grid.Items.Count);
                var other = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.A) { RoutedEvent = Keyboard.KeyDownEvent };
                Ui.Call(e, "OnGridKeyDown", grid, other);
                Assert.False(other.Handled);
                e.Close();
            });
        });
    }

    // ------------------------------------------------------------ guia

    [Fact]
    public void Guia_SeRecorreEntera_YCadaPasoDiceSuEstado()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            var main = new MainWindow(settings);
            main.Show();
            var guide = new GuideWindow(main, settings) { Owner = main };
            Ui.Expect<GuideWindow>(g =>
            {
                string Status() => Ui.Find<TextBlock>(g, "StatusLabel").Text;
                Assert.False(Ui.Find<Button>(g, "BackButton").IsEnabled);
                Assert.Equal(Loc.Format("GuideHotkeysText", settings.RecordKey.Display(), settings.PlayKey.Display()), Ui.Find<TextBlock>(g, "StepText").Text);
                Assert.Equal(Loc.Get("StepDone"), Status());      // la ventana registro los atajos
                Ui.Click(Ui.Find<Button>(g, "NextButton"));
                Assert.Equal(Loc.Get("StepDone"), Status());      // parada de emergencia puesta
                Ui.Click(Ui.Find<Button>(g, "BackButton"));
                Assert.Equal(Loc.Format("GuideStep", 1, 6), Ui.Find<TextBlock>(g, "StepCounter").Text);
                for (var i = 0; i < 5; i++)
                    Ui.Click(Ui.Find<Button>(g, "NextButton"));
                Assert.Equal("check", Ui.Find<Button>(g, "NextButton").Tag);
                Assert.Equal(Visibility.Collapsed, Ui.Find<Button>(g, "ActionButton").Visibility);
                Loc.Use("en");
                Assert.Equal(Loc.Get("GuideTryTitle"), Ui.Find<TextBlock>(g, "StepTitle").Text);
                Loc.Use("es");
                Ui.Click(Ui.Find<Button>(g, "NextButton"));   // Hecho: cierra
            });
            guide.ShowDialog();
            Assert.True(Ui.IsClosed(guide));
        });
    }

    [Fact]
    public void Guia_LasAcciones_AbrenAjustes_Asocian_YReinicianElevado()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            settings.Emergency = EmergencyKeys.None;
            var main = new MainWindow(settings);
            main.Show();
            Ui.Expect<GuideWindow>(g =>
            {
                string Status() => Ui.Find<TextBlock>(g, "StatusLabel").Text;
                Ui.Expect<SettingsWindow>(s => s.Close());
                Ui.Click(Ui.Find<Button>(g, "ActionButton"));   // paso 1: abre ajustes
                Ui.Click(Ui.Find<Button>(g, "NextButton"));
                Assert.Equal(Loc.Get("StepPending"), Status());
                Ui.Click(Ui.Find<Button>(g, "NextButton"));
                Assert.Equal(Loc.Get("StepOptional"), Status());   // privacidad
                Ui.Click(Ui.Find<Button>(g, "NextButton"));
                Assert.Equal(Loc.Get("StepOptional"), Status());   // ficheros: sin asociar
                Ui.Click(Ui.Find<Button>(g, "ActionButton"));
                Assert.Equal(Loc.Get("StepDone"), Status());
                Assert.Equal(Visibility.Collapsed, Ui.Find<Button>(g, "ActionButton").Visibility);
                Ui.Click(Ui.Find<Button>(g, "NextButton"));
                Ui.Click(Ui.Find<Button>(g, "ActionButton"));   // reiniciar como administrador
            });
            main.ShowGuide();
            Assert.Equal("runas", Assert.Single(Fake.Started).Verb);
            Assert.True(Ui.IsClosed(main));
        });
        Assert.True(FileAssociation.IsApplied(Fake.ExePath!, Fake.AssociationRoot));
    }

    [Fact]
    public void Guia_EnElPaquete_ComoAdministrador_YSiNoSePuedeAsociar()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            var main = new MainWindow(settings);
            main.Show();
            Fake.Elevated = true;
            Fake.AssociationRoot = Registry.CurrentUser.OpenSubKey(FakePlatform.RegistryBranch, writable: false)!;
            Ui.Expect<GuideWindow>(g =>
            {
                for (var i = 0; i < 3; i++)
                    Ui.Click(Ui.Find<Button>(g, "NextButton"));
                string? message = null;
                Ui.Answer("OkButton", m => message = m);
                Ui.Click(Ui.Find<Button>(g, "ActionButton"));
                Assert.Equal(Loc.Get("AssociateFailed"), message);
                Fake.IsPackaged = true;
                Ui.Click(Ui.Find<Button>(g, "NextButton"));
                Assert.Equal(Loc.Get("StepDone"), Ui.Find<TextBlock>(g, "StatusLabel").Text);   // ya es administrador
                Ui.Click(Ui.Find<Button>(g, "BackButton"));
                Assert.Equal(Loc.Get("StepDone"), Ui.Find<TextBlock>(g, "StatusLabel").Text);   // el paquete asocia
                Ui.Call(g, "Associate");   // en el paquete no hace nada
                Fake.IsPackaged = false;
                Sandbox.IsOn = true;
                Ui.Call(g, "Refresh");
                Assert.Equal(Loc.Get("StepOptional"), Ui.Find<TextBlock>(g, "StatusLabel").Text);
                Sandbox.IsOn = false;
                g.Close();
            });
            main.ShowGuide();
        });
    }

    // ------------------------------------------------------------ acerca de, novedades, dialogos

    [Fact]
    public void AcercaDe_Version_Idioma_Contacto_YNovedades()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            var main = new MainWindow(settings);
            main.Show();
            Fake.Elevated = true;
            var about = new AboutWindow { Owner = main };
            Ui.Expect<AboutWindow>(a =>
            {
                Assert.Equal("v" + AppInfo.Version + " · " + Loc.Get("RunningAsAdmin"), Ui.Find<TextBlock>(a, "VersionLabel").Text);
                Assert.NotNull(Ui.Find<System.Windows.Controls.Image>(a, "LogoImage").Source);
                Ui.Click(Ui.Find<Button>(a, "EnglishButton"));
                Assert.Equal("en", settings.Language);
                Assert.Equal("v" + AppInfo.Version + " · " + Loc.Get("RunningAsAdmin"), Ui.Find<TextBlock>(a, "VersionLabel").Text);
                Ui.Click(Ui.Find<Button>(a, "SpanishButton"));
                Assert.Equal("es", settings.Language);
                Ui.Click(Ui.Find<Button>(a, "ContactButton"));
                Assert.Equal(AppInfo.ContactAddress, Fake.Started[^1].FileName);
                Fake.StartError = new InvalidOperationException("sin correo");
                string? message = null;
                Ui.Answer("OkButton", m => message = m);
                Ui.Click(Ui.Find<Button>(a, "ContactButton"));
                Assert.Equal(Loc.Get("ContactFailed"), message);
                Ui.Expect<WhatsNewWindow>(n =>
                {
                    var cards = n.Content is DockPanel dock ? ((ScrollViewer)dock.Children[1]).Content as StackPanel : null;
                    Assert.Equal(WhatsNew.Latest().Count(), cards!.Children.Count);
                    Loc.Use("en");
                    Assert.Equal(Loc.Get("WhatsNew"), n.Title);
                    Loc.Use("es");
                    Ui.Click(Ui.Find<Button>(n, "CloseButton"));
                });
                Ui.Click(Ui.Find<Button>(a, "WhatsNewButton"));
                Ui.Click(Ui.Find<Button>(a, "CloseButton"));
            });
            about.ShowDialog();
        });
    }

    [Fact]
    public void AcercaDe_SinVentanaPrincipal_CambiaElIdiomaIgual()
    {
        Ui.Run(() =>
        {
            Ui.Expect<AboutWindow>(a =>
            {
                Ui.Click(Ui.Find<Button>(a, "EnglishButton"));
                Assert.Equal("en", Loc.Language);
                Ui.Click(Ui.Find<Button>(a, "SpanishButton"));
                a.Close();
            });
            new AboutWindow().ShowDialog();
        });
    }

    [Fact]
    public void Dialogos_ConfirmarPeligroso_EscapeCancela_YCambiosSinGuardar()
    {
        Ui.Run(() =>
        {
            Ui.Expect<PromptWindow>(p =>
            {
                Assert.Equal("delete", Ui.Find<Button>(p, "OkButton").Tag);
                Assert.True(p.ShowInTaskbar);
                p.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(p)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            });
            Assert.False(PromptWindow.Confirm(null, "t", "m", danger: true));
            Ui.Answer("OkButton");
            Assert.True(PromptWindow.Confirm(null, "t", "m"));
            Ui.Answer("OkButton");
            Assert.Equal(UnsavedChoice.Save, PromptWindow.AskUnsaved(null, "m"));
            Ui.Answer("DiscardButton");
            Assert.Equal(UnsavedChoice.Discard, PromptWindow.AskUnsaved(null, "m"));
            Ui.Answer("CancelButton");
            Assert.Equal(UnsavedChoice.Cancel, PromptWindow.AskUnsaved(null, "m"));
            Ui.Expect<PromptWindow>(p =>
            {
                Assert.True(Ui.Find<TextBox>(p, "ValueBox").IsFocused || true);
                p.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(p)!, 0, System.Windows.Input.Key.A) { RoutedEvent = Keyboard.KeyDownEvent });
                Ui.Click(Ui.Find<Button>(p, "OkButton"));
            });
            Assert.Equal(12, PromptWindow.AskNumber(null, "t", "m", 12, 0, 100));
        });
    }

    // ------------------------------------------------------------ controles

    private static KeyEventArgs Press(Visual target, Key key) =>
        new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };

    [Fact]
    public void CasillaDeAtajo_SinModificador_NoVale_YRetrocesoDeshace()
    {
        Ui.Run(() =>
        {
            Settings(Settings(), new PlaybackOptions(), s =>
            {
                var box = Ui.Find<HotkeyBox>(s, "RecordHotkeyBox");
                var original = box.Hotkey;
                var a = Press(box, System.Windows.Input.Key.A);
                box.RaiseEvent(a);
                Assert.True(a.Handled);
                Assert.Contains(Loc.Get("HotkeyNeedsModifier"), box.Text);
                Assert.Equal(original, box.Hotkey);
                box.RaiseEvent(Press(box, System.Windows.Input.Key.LeftCtrl));   // un modificador solo: nada
                var tab = Press(box, System.Windows.Input.Key.Tab);
                box.RaiseEvent(tab);
                Assert.False(tab.Handled);
                box.Hotkey = new Hotkey(HotkeyModifiers.Win, 0x42);
                box.RaiseEvent(Press(box, System.Windows.Input.Key.Back));
                Assert.Equal(original, box.Hotkey);
                Loc.Use("en");
                Assert.Equal(original.Display(), box.Text);
                Loc.Use("es");
                box.RaiseEvent(Press(box, System.Windows.Input.Key.B));
                box.Focus();
                Ui.Find<HotkeyBox>(s, "PlayHotkeyBox").Focus();       // al perder el foco vuelve el texto del atajo
                Assert.Equal(original.Display(), box.Text);
                s.Close();
            }, out _, out _);
        });
    }

    [Fact]
    public void Icono_DibujaElSvg_YSinNombreNada()
    {
        Ui.Run(() =>
        {
            var icon = new Icon { Kind = "record" };
            var canvas = Assert.IsType<Canvas>(icon.Child);
            Assert.NotEmpty(canvas.Children);
            icon.Kind = "flag_es";
            Assert.Contains(((Canvas)icon.Child).Children.OfType<System.Windows.Shapes.Path>(), p => p.Fill is not null);
            icon.Kind = null;
            Assert.Null(icon.Child);
        });
    }

    [Fact]
    public void Tema_ClaroOscuroYSistema_CambianLosPinceles()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            new WindowInteropHelper(w).EnsureHandle();
            ThemeManager.Apply(AppTheme.Dark);
            Assert.True(ThemeManager.IsDark);
            Assert.Equal(Color.FromRgb(0x14, 0x13, 0x18), ((SolidColorBrush)Application.Current.Resources["PageBackground"]).Color);
            ThemeManager.Apply(AppTheme.Light);
            Assert.False(ThemeManager.IsDark);
            Assert.Equal(Color.FromRgb(0xF8, 0xF9, 0xFA), ((SolidColorBrush)Application.Current.Resources["PageBackground"]).Color);
            ThemeManager.Apply(AppTheme.System);
            Assert.Equal(AppTheme.System, ThemeManager.Mode);
            ThemeManager.WatchSystem();
            ThemeManager.ApplyToWindow(new Window());   // sin ventana de Windows: nada
            ThemeManager.Apply(AppTheme.Light);
        });
    }

    [Fact]
    public void Textos_EnlazadosDelXaml_CambianConElIdioma()
    {
        var changed = new List<string?>();
        LocSource.Instance.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal(Loc.Get("Record"), LocSource.Instance["Record"]);
        Loc.Use("en");
        Assert.Equal("Item[]", changed.Last());
        Assert.Equal(Loc.Get("Record"), LocSource.Instance["Record"]);
        Loc.Use("es");
        Assert.Equal("Record", new TExtension("Record").Key);
        Assert.Equal(string.Empty, new TExtension().Key);
        Ui.Run(() =>
        {
            var text = new TextBlock();
            var binding = (System.Windows.Data.BindingExpression)new TExtension("Record").ProvideValue(new Target(text, TextBlock.TextProperty));
            Assert.NotNull(binding);
        });
    }

    private sealed class Target(DependencyObject obj, DependencyProperty prop) : IServiceProvider, System.Windows.Markup.IProvideValueTarget
    {
        public object TargetObject => obj;
        public object TargetProperty => prop;
        public object? GetService(Type serviceType) => serviceType == typeof(System.Windows.Markup.IProvideValueTarget) ? this : null;
    }

    [Fact]
    public void Sandbox_PoneLosDatosEnUnaCarpetaAparte()
    {
        _ = Ui.Run(() => 0);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "sOCAutoTask-sandbox"), Ui.SandboxFolder);
        Ui.Run(() => Sandbox.Apply());   // apagado: no cambia nada
        Assert.Equal(Temp.Path, AppPaths.Current.DataFolder);
    }

    // ------------------------------------------------------------ arranque (App)

    private static App App => (App)Application.Current;

    [Fact]
    public void Arranque_AbreLaVentana_ConElFicheroYReproduce()
    {
        var path = Temp.File("a.soctask");
        Format.RecordingFile.Save(new Recording(MainWindowTests.Events()), path);
        Settings().Save(AppPaths.Current.SettingsFile);
        Ui.Run(() =>
        {
            var w = App.Launch([path, "--play"])!;
            Assert.Same(w, App.MainWindow);
            Assert.True(w.IsVisible);
            Assert.Equal(path, w.Session.FilePath);
            Ui.WaitFor(() => Fake.Sink.Sent.Count == 5);
            App.ReleaseInstance();
        });
    }

    [Fact]
    public void Arranque_LaPrimeraVez_EnseñaLaGuia_YLuegoLasNovedades()
    {
        var settings = Settings();
        settings.GuideShown = false;
        settings.Save(AppPaths.Current.SettingsFile);
        Ui.Run(() =>
        {
            Ui.Expect<GuideWindow>(g => g.Close());
            var w = App.Launch([])!;
            Ui.WaitFor(() => Ui.Pending == 0);
            App.ReleaseInstance();
        });
        var saved = AppSettings.Load(AppPaths.Current.SettingsFile);
        Assert.True(saved.GuideShown);
        Assert.Equal(AppInfo.Version, saved.LastSeenVersion);

        saved.LastSeenVersion = "2000.1.1.0";
        saved.Save(AppPaths.Current.SettingsFile);
        Ui.Run(() =>
        {
            Ui.Expect<WhatsNewWindow>(n => n.Close());
            App.Launch([]);
            Ui.WaitFor(() => Ui.Pending == 0);
            App.ReleaseInstance();
        });
    }

    [Fact]
    public void Arranque_ConOtraCopiaAbierta_LePasaElFichero()
    {
        var path = Temp.File("b.soctask");
        Format.RecordingFile.Save(new Recording(MainWindowTests.Events()), path);
        Settings().Save(AppPaths.Current.SettingsFile);
        Ui.Run(() =>
        {
            var first = App.Launch([])!;
            var other = new SingleInstance(AppInfo.Version, Fake.InstanceSuffix);
            var claim = Task.Run(() => other.Claim(path));   // desde otro hilo, como otro proceso (el mutex es por hilo)
            Ui.WaitFor(() => claim.IsCompleted);
            Assert.False(claim.Result);        // la que ya estaba lo abre
            Ui.WaitFor(() => first.Session.FilePath == path);
            other.Dispose();
            App.ReleaseInstance();
        });
    }

    [Fact]
    public void Arranque_EnModoAislado_SinInstanciaUnica_YEsperaAlProcesoQueSeVa()
    {
        Ui.Run(() =>
        {
            Sandbox.IsOn = true;
            var w = App.Launch(["--wait-pid", "999999"])!;   // no existe: no espera
            Assert.NotNull(w);
            Assert.Null(Ui.Field<object?>(App, "_instance"));
        });
        SocAutoTask.Desktop.App.WaitForExit(Environment.ProcessId, timeoutMs: 1);
    }

    [Fact]
    public void ErrorInesperado_SeApuntaYSeAvisa_UnaSolaVez()
    {
        Ui.Run(() =>
        {
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            App.OnUnexpected(new InvalidOperationException("prueba"));
            Assert.Equal(Loc.Format("UnexpectedErrorText", AppLog.FilePath), message);
        });
        Assert.Contains("error no controlado: System.InvalidOperationException: prueba", File.ReadAllText(AppLog.FilePath));
    }
}
