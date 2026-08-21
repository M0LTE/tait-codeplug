using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using M0LTE.Tait.Codeplug;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// The interactive mode, and what you get when the tool is run with no arguments: pick a port, read
/// the radio, edit the packet-relevant essentials, write it back. Everything here is a front-end over
/// the same library calls the verbs use - no separate code path to the radio.
///
/// The radio work happens on a worker thread (a read is ~25s, and the connect can sit for 90s waiting
/// for the operator's power-cycle), so the UI stays responsive and the log pane can narrate. Anything
/// that touches a view from that thread goes through <c>IApplication.Invoke</c>.
/// </summary>
internal static class Tui
{
    private static readonly string[] PresetLabels = ["none", "pdn-basic", "pdn-extra"];

    /// <summary>Keep the log bounded: a session left open for hours should not grow a list view for ever.</summary>
    private const int MaxLogLines = 500;

    private static CodeplugImage? _image;
    private static CodeplugFields? _fields;
    private static bool _busy;

    private static readonly ObservableCollection<string> LogLines = [];
    private static readonly ObservableCollection<string> ChannelRows = [];

    private static DropDownList _portField = null!;
    private static ListView _channelList = null!;
    private static ListView _logList = null!;
    private static OptionSelector _presetSelector = null!;
    private static Label _statusLabel = null!;
    private static Button _readButton = null!;
    private static Button _writeButton = null!;
    private static Label _detectedLabel = null!;
    private static Window _window = null!;
    private static IApplication _app = null!;

    internal static int Run(CodeplugImage? initial = null, string? source = null)
    {
        _app = Application.Create();
        try
        {
            _app.Init();
            TuiTheme.Apply();
            _window = Build();
            if (initial is not null)
            {
                _image = initial;
                Log($"loaded {source} ({initial.Records.Count} records).");
                LoadFields();
            }
            else
            {
                Log("ready. pick a port, then Read from radio (power-cycle the radio when prompted).");
            }

            RefreshChannels();
            Log("F6 moves between panels, Tab moves within one.");
            SetBusy(false, StatusText());
            _app.Run(_window);
        }
        finally
        {
            _app.Dispose();
        }

        return 0;
    }

    private static Window Build()
    {
        var win = new Window
        {
            Title = $"tait-codeplug {CliVersion.Current} - Tait TM8100/TM8200 codeplug editor",
            BorderStyle = LineStyle.Rounded,
        };

        // --- radio bar: port + the two hardware buttons ------------------------------------------
        var radio = new FrameView
        {
            Title = "Radio",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 5,
        };

        var portLabel = new Label { Text = "Port:", X = 1, Y = 0 };

        // A dropdown of what is actually plugged in, rather than a box you have to know what to type
        // into. It still derives from a text field, so a port that did not enumerate can be typed.
        _portField = new DropDownList { X = 7, Y = 0, Width = 26 };
        RefreshPorts();

        var rescanButton = new Button { Text = "Re_scan", X = 35, Y = 0 };
        rescanButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            RefreshPorts();
            Log($"rescanned: {DetectedPortSummary()}");
        };

        var detected = new Label { X = 45, Y = 0, Text = DetectedPortSummary() };
        _detectedLabel = detected;

        _readButton = new Button { Text = "_Read from radio", X = 1, Y = 2 };
        _readButton.Accepting += (_, e) => { e.Handled = true; StartRead(); };

        _writeButton = new Button { Text = "_Write to radio", X = 22, Y = 2 };
        _writeButton.Accepting += (_, e) => { e.Handled = true; StartWrite(); };

        _statusLabel = new Label { X = 42, Y = 2, Text = "no codeplug loaded" };

        TuiTheme.Panelise(radio);
        TuiTheme.Body(portLabel);
        TuiTheme.Input(_portField);
        TuiTheme.Action(rescanButton, TuiAccent.Neutral);
        TuiTheme.Secondary(detected);
        TuiTheme.Action(_readButton, TuiAccent.Read);
        TuiTheme.Action(_writeButton, TuiAccent.Write);
        TuiTheme.Status(_statusLabel, loaded: false);
        radio.Add(portLabel, _portField, rescanButton, detected, _readButton, _writeButton, _statusLabel);

        // --- channels (left) ----------------------------------------------------------------------
        var channels = new FrameView
        {
            Title = "Channels",
            X = 0,
            Y = 5,
            Width = Dim.Fill() - 28,
            Height = Dim.Fill() - 9,
        };

        var header = new Label
        {
            X = 1,
            Y = 0,
            Text = $"{"#",-4}{"RX (MHz)",-14}{"TX (MHz)",-14}{"Bandwidth",-11}{"Power",-8}",
        };

        _channelList = new ListView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _channelList.SetSource(ChannelRows);
        _channelList.Accepting += (_, e) => { e.Handled = true; EditSelectedChannel(); };

        TuiTheme.Panelise(channels);
        TuiTheme.Secondary(header);
        TuiTheme.Body(_channelList);
        channels.Add(header, _channelList);

        // --- preset (right) -----------------------------------------------------------------------
        var preset = new FrameView
        {
            Title = "PDN preset",
            X = Pos.Right(channels),
            Y = 5,
            Width = 28,
            Height = Dim.Fill() - 9,
        };

        _presetSelector = new OptionSelector
        {
            X = 1,
            Y = 0,
            Labels = PresetLabels,
            Value = 0,
        };

        var presetHelp = new Label
        {
            X = 1,
            Y = 4,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = "Applied when you\nwrite. Neither preset\ntouches RF or channel\nconfig.\n\n"
                + "basic: CCDI control\n(RSSI, power, status,\nPTT, DCD).\n\n"
                + "extra: adds the\nTNC-less FFSK modem\nand SDM signalling.",
        };

        TuiTheme.Panelise(preset);
        TuiTheme.Body(_presetSelector);
        TuiTheme.Secondary(presetHelp);
        preset.Add(_presetSelector, presetHelp);

        // --- log (bottom) -------------------------------------------------------------------------
        var log = new FrameView
        {
            Title = "Log",
            X = 0,
            Y = Pos.Bottom(channels),
            Width = Dim.Fill(),
            Height = 9,
        };

        _logList = new ListView { X = 1, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _logList.SetSource(LogLines);
        TuiTheme.Panelise(log);
        TuiTheme.Secondary(_logList);
        log.Add(_logList);

        var status = new StatusBar(
        [
            new Shortcut(Key.F10, "Quit", () => _app.RequestStop(_window)),
            new Shortcut(Key.F6, "Panel", () =>
                _app.Navigation?.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabGroup)),
            new Shortcut(Key.F3, "Edit", EditSelectedChannel),
            new Shortcut(Key.F7, "Add", AddChannel),
            new Shortcut(Key.F8, "Del", DeleteSelectedChannel),
            new Shortcut(Key.F5, "Read", StartRead),
            new Shortcut(Key.F2, "Write", StartWrite),
        ]);

        // Without this a panel is not a focus stop, so Tab never leaves the first one and the rest of
        // the screen is unreachable from the keyboard.
        foreach (View panel in new View[] { radio, channels, preset, log })
        {
            panel.CanFocus = true;
            panel.TabStop = TabBehavior.TabGroup;
            TuiTheme.TrackFocus(panel);
        }

        win.Add(radio, channels, preset, log, status);
        return win;
    }

    /// <summary>Re-enumerate the serial ports into the dropdown, keeping whatever is typed if it is
    /// not one of them (a port that did not enumerate is still worth trying).</summary>
    private static void RefreshPorts()
    {
        string[] ports = SerialPort.GetPortNames();
        Array.Sort(ports, StringComparer.Ordinal);

        string current = _portField.Text?.Trim() ?? string.Empty;
        _portField.Source = new ListWrapper<string>(new ObservableCollection<string>(ports));
        _portField.Text = current.Length > 0 ? current : ports.FirstOrDefault() ?? string.Empty;

        if (_detectedLabel is not null)
        {
            _detectedLabel.Text = DetectedPortSummary();
        }
    }

    private static string DetectedPortSummary()
    {
        string[] ports = SerialPort.GetPortNames();
        return ports.Length == 0
            ? "no ports detected - type one, or Rescan"
            : $"{ports.Length} port(s) detected";
    }

    // --- channels ---------------------------------------------------------------------------------

    private static void AddChannel()
    {
        if (_fields is null)
        {
            Error("Nothing to add to", "Read the radio (F5) or open an .m8p first.");
            return;
        }

        try
        {
            int added = _fields.AddChannel();
            RefreshChannels();
            _channelList.Value = added;
            Log($"added channel {added}, copied from {added - 1} (not yet written to the radio).");
            EditSelectedChannel();
        }
        catch (InvalidOperationException ex)
        {
            Error("Cannot add a channel", ex.Message);
        }
    }

    private static void DeleteSelectedChannel()
    {
        if (_fields is null)
        {
            Error("Nothing to delete", "Read the radio (F5) or open an .m8p first.");
            return;
        }

        int index = _channelList.Value ?? 0;
        if (index < 0 || index >= _fields.ChannelCount)
        {
            return;
        }

        int? answer = MessageBox.Query(
            _app,
            "Delete channel",
            $"Delete channel {index} ({FormatChannel(_fields, index)})?\n\nChannels above it shift down. Nothing reaches the radio until you write.",
            "Cancel",
            "Delete");
        if (answer != 1)
        {
            return;
        }

        try
        {
            _fields.RemoveChannel(index);
            RefreshChannels();
            _channelList.Value = Math.Min(index, Math.Max(0, _fields.ChannelCount - 1));
            Log($"deleted channel {index} (not yet written to the radio).");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            Error("Cannot delete that channel", ex.Message);
        }
    }

    // --- radio operations -------------------------------------------------------------------------

    private static void StartRead()
    {
        if (_busy)
        {
            return;
        }

        string port = _portField.Text?.Trim() ?? string.Empty;
        if (port.Length == 0)
        {
            Error("No port", "Type a serial port (e.g. /dev/ttyUSB0 or COM3) into the Port box first.");
            return;
        }

        SetBusy(true, $"reading {port}...");
        Log($"opening {port} at 19200 8N1 - POWER-CYCLE THE RADIO NOW to latch programming mode.");

        RunOffThread(
            () =>
            {
                using var programmer = new TaitProgrammer(new SerialPortLine(port), HardwareOptions());
                return programmer.ReadImage();
            },
            image =>
            {
                _image = image;
                Log($"read {image.Records.Count} records, all checksums verified.");
                LoadFields();
            });
    }

    private static void StartWrite()
    {
        if (_busy)
        {
            return;
        }

        if (_image is null || _fields is null)
        {
            Error("Nothing to write", "Read the radio first - the tool writes back the codeplug it read, with your edits.");
            return;
        }

        string port = _portField.Text?.Trim() ?? string.Empty;
        if (port.Length == 0)
        {
            Error("No port", "Type a serial port into the Port box first.");
            return;
        }

        int presetIndex = _presetSelector.Value ?? 0;
        string presetName = PresetLabels[presetIndex];
        string what = presetIndex == 0
            ? "Write the whole codeplug back to the radio?"
            : $"Apply the {presetName} preset and write the whole codeplug back to the radio?";

        int? answer = MessageBox.Query(
            _app,
            "Write to radio",
            $"{what}\n\nThe pre-change codeplug is backed up to a file first.\nPower-cycle the radio when prompted.",
            "Cancel",
            "Write");
        if (answer != 1)
        {
            Log("write cancelled.");
            return;
        }

        // Golden rule 1: snapshot before writing. The image in hand is the radio's own bytes plus
        // whatever was edited here, so the backup is taken from a re-decode of the original read.
        CodeplugImage image = _image;
        CodeplugFields fields = _fields;

        SetBusy(true, $"writing {port}...");

        RunOffThread(
            () =>
            {
                if (presetIndex == 1)
                {
                    fields.ApplyPdnBasic();
                }
                else if (presetIndex == 2)
                {
                    fields.ApplyPdnExtra();
                }

                string backup = $"tait-codeplug-backup-{DateTime.UtcNow:yyyyMMddHHmmss}.m8p";
                File.WriteAllText(backup, image.ToM8p());

                using var programmer = new TaitProgrammer(new SerialPortLine(port), HardwareOptions());
                int written = programmer.WriteImage(image);
                return (backup, written);
            },
            result =>
            {
                Log($"backed up to {result.backup}");
                if (presetIndex != 0)
                {
                    Log($"applied preset {presetName}.");
                }

                Log($"wrote {result.written} records. Power-cycle and re-read to verify - "
                    + "read-back in the same session is unreliable after a write.");
            });
    }

    private static ProgrammerOptions HardwareOptions() => new()
    {
        ConnectWaitMs = 90_000, // wait up to 90s for the operator to power-cycle into programming mode
    };

    /// <summary>Run a blocking radio operation off the UI thread, then hand the result back on it.
    /// Failures land in the log and a dialog rather than taking the app down.</summary>
    private static void RunOffThread<T>(Func<T> work, Action<T> onSuccess)
    {
        _ = Task.Run(() =>
        {
            try
            {
                T result = work();
                _app.Invoke(() =>
                {
                    onSuccess(result);
                    SetBusy(false, StatusText());
                });
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException
                                       or ArgumentException or UnauthorizedAccessException or FormatException)
            {
                _app.Invoke(() =>
                {
                    Log($"error: {ex.Message}");
                    SetBusy(false, StatusText());
                    Error("Radio error", ex.Message);
                });
            }
        });
    }

    // --- codeplug state ---------------------------------------------------------------------------

    private static void LoadFields()
    {
        if (_image is null)
        {
            return;
        }

        if (!CodeplugFields.IsSupported(_image))
        {
            _fields = null;
            Log($"database version {_image.DatabaseVersion} has no field map - channels not editable, "
                + "and writing is refused. Read-only.");
            ChannelRows.Clear();
            return;
        }

        _fields = CodeplugFields.Open(_image);
        RefreshChannels();
        Log($"DBVer {_image.DatabaseVersion}, {_fields.ChannelCount} channel(s) decoded.");
    }

    private static void RefreshChannels()
    {
        ChannelRows.Clear();
        if (_fields is null)
        {
            ChannelRows.Add(_image is null
                ? "  no codeplug loaded - press F5 to read the radio"
                : "  no field map for this database version - read-only");
            return;
        }

        if (_fields.ChannelCount == 0)
        {
            ChannelRows.Add("(no channels in this codeplug)");
            return;
        }

        for (int i = 0; i < _fields.ChannelCount; i++)
        {
            ChannelRows.Add(FormatChannel(_fields, i));
        }
    }

    private static string FormatChannel(CodeplugFields f, int i)
    {
        string rx = Mhz(f.GetRxFrequencyHz(i));
        string tx = f.GetSeparateTxFrequency(i) ? Mhz(f.GetTxFrequencyHz(i)) : "(= RX)";
        return $"{i,-4}{rx,-14}{tx,-14}{f.GetBandwidth(i),-11}{f.GetPowerLevel(i),-8}";
    }

    private static string Mhz(long hz) =>
        (hz / 1_000_000.0).ToString("F6", CultureInfo.InvariantCulture);

    private static string StatusText() =>
        _image is null
            ? "no codeplug loaded"
            : $"DBVer {_image.DatabaseVersion}, {_image.Records.Count} records";

    // --- channel editing --------------------------------------------------------------------------

    private static void EditSelectedChannel()
    {
        if (_fields is null)
        {
            Error("Nothing to edit", "Read the radio first.");
            return;
        }

        int index = _channelList.Value ?? 0;
        if (index < 0 || index >= _fields.ChannelCount)
        {
            return;
        }

        CodeplugFields f = _fields;

        var dialog = new Dialog { Title = $"Channel {index}", Width = 62, Height = 15, BorderStyle = LineStyle.Rounded };

        var rxLabel = new Label { Text = "RX (MHz):", X = 1, Y = 1 };
        var rxField = new TextField { X = 14, Y = 1, Width = 16, Text = Mhz(f.GetRxFrequencyHz(index)) };

        var splitBox = new CheckBox
        {
            Text = "Separate TX frequency",
            X = 1,
            Y = 3,
            Value = f.GetSeparateTxFrequency(index) ? CheckState.Checked : CheckState.UnChecked,
        };

        var txLabel = new Label { Text = "TX (MHz):", X = 1, Y = 4 };
        var txField = new TextField { X = 14, Y = 4, Width = 16, Text = Mhz(f.GetTxFrequencyHz(index)) };

        var bwLabel = new Label { Text = "Bandwidth:", X = 1, Y = 6 };
        var bwSelector = new OptionSelector
        {
            X = 14,
            Y = 6,
            Orientation = Orientation.Horizontal,
            Labels = Enum.GetNames<Bandwidth>(),
            Value = (int)f.GetBandwidth(index),
        };

        var powerLabel = new Label { Text = "Power:", X = 1, Y = 8 };
        var powerSelector = new OptionSelector
        {
            X = 14,
            Y = 8,
            Orientation = Orientation.Horizontal,
            Labels = Enum.GetNames<PowerLevel>(),
            Value = (int)f.GetPowerLevel(index),
        };

        var cancel = new Button { Text = "Cancel", IsDefault = false };
        cancel.Accepting += (_, e) => { e.Handled = true; _app.RequestStop(dialog); };

        var ok = new Button { Text = "OK", IsDefault = true };
        ok.Accepting += (_, e) =>
        {
            e.Handled = true;
            try
            {
                bool split = splitBox.Value == CheckState.Checked;
                f.SetRxFrequencyHz(index, ParseMhz(rxField.Text, "RX"));
                f.SetSeparateTxFrequency(index, split);
                f.SetTxFrequencyHz(index, split ? ParseMhz(txField.Text, "TX") : ParseMhz(rxField.Text, "RX"));
                f.SetBandwidth(index, (Bandwidth)(bwSelector.Value ?? 0));
                f.SetPowerLevel(index, (PowerLevel)(powerSelector.Value ?? 0));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
            {
                Error("Invalid value", ex.Message);
                return;
            }

            ChannelRows[index] = FormatChannel(f, index);
            Log($"channel {index} edited (not yet written to the radio).");
            _app.RequestStop(dialog);
        };

        dialog.AddButton(cancel);
        dialog.AddButton(ok);
        dialog.Add(rxLabel, rxField, splitBox, txLabel, txField, bwLabel, bwSelector, powerLabel, powerSelector);
        _app.Run(dialog);
        dialog.Dispose();
    }

    private static long ParseMhz(string? text, string which)
    {
        if (!double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double mhz)
            || mhz < 0)
        {
            throw new FormatException($"{which} frequency must be a number of MHz, e.g. 144.800000");
        }

        return (long)Math.Round(mhz * 1_000_000.0);
    }

    // --- chrome -----------------------------------------------------------------------------------

    private static void SetBusy(bool busy, string status)
    {
        _busy = busy;
        _readButton.Enabled = !busy;
        _writeButton.Enabled = !busy;
        _statusLabel.Text = status;
        TuiTheme.Status(_statusLabel, loaded: _image is not null);
    }

    private static void Log(string line)
    {
        LogLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        while (LogLines.Count > MaxLogLines)
        {
            LogLines.RemoveAt(0);
        }

        _logList.Value = LogLines.Count - 1;
    }

    private static void Error(string title, string message) =>
        MessageBox.ErrorQuery(_app, title, message, "OK");
}
