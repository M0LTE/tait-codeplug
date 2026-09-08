# M0LTE.Tait.Codeplug

A Tait TM8100/TM8200 codeplug library, reverse-engineered from Free Serial Analyzer captures of the
Windows CPS. It reads and writes the codeplug over the serial programming interface without the CPS,
exposes a typed, version-pinned field map for the whole CPS Data form and the channel table, and
applies the Packet.NET (PDN) upgrade profiles. The read + write path is hardware-validated against a
real TM8100.

The CLI front-end that ships from the same repo, `tait-codeplug`, is a thin layer over this library:
see [github.com/M0LTE/tait-codeplug](https://github.com/M0LTE/tait-codeplug) for prebuilt binaries.

## The protocol, in short

- ASCII-hex, line-oriented, CR-terminated, strictly lock-step (every command gets one `>` prompt
  before the next).
- Records share the `.m8p` framing `<addr:4hex><len:2hex><data><checksum:2hex>`; checksum is the
  CCDI-family negated sum over the decoded bytes (whole record sums to 0 mod 256). `addr` is
  `(section << 8) | index`.
- Session: `^` (reset -> `v`), `#` (enter programming -> `>`), `ld` -> `{C05}`, `d00` -> `{C01}`.
  Read a section: `r<section>`. Write: `b`, `i<arg>`, a run of `w<record>`, `e`. Teardown: `^`.
- Baud opens at 9600, switches to 19200 for the transfer.

The full write-up is in [`docs/research/tait-codeplug-protocol.md`](https://github.com/packet-net/packet.net/blob/main/docs/research/tait-codeplug-protocol.md)
and the programming brief it came from is [`docs/research/tait-codeplug-programming-brief.md`](https://github.com/packet-net/packet.net/blob/main/docs/research/tait-codeplug-programming-brief.md),
both in the packet.net repo where this code started life.

## What is here

- `CodeplugChecksum` / `CodeplugRecord` / `CodeplugImage` - the record model, checksum, and .m8p
  load/save + section map. Fully offline and unit-tested.
- `Fields/` - the typed, version-pinned field map (`CodeplugFields`, `CodeplugEnums`,
  `ChannelBits`): channels (frequency, bandwidth, power, split-TX, CTCSS/DCS), the whole CPS **Data**
  form (its General, Serial Communications, RF Modems, SDM and TOTAL Transparent Mode tabs live in the
  one data/signalling record; the GPS and Customer Data tabs are separate records; plus the unit data
  identity), and audio taps. Each field is pinned by a test.
- `FieldConsole` - name/value access used by the `dump`/`get`/`set` CLI verbs.
- `CodeplugFields.ApplyPdnBasic()` / `ApplyPdnExtra()` / `ApplyPdnInternal()` - the PDN upgrade profiles (see below).
- `ISerialLine` / `SerialPortLine` - the byte seam (mirrors `Packet.Radio.Tait.ISerialIo`); tests
  substitute a scripted mock radio.
- `TaitProgrammer` - the lock-step transport state machine (connect, interrogate, read, write).

## Using it

```csharp
using M0LTE.Tait.Codeplug;

// Offline: load a CPS .m8p save and read a field.
CodeplugImage image = CodeplugImage.LoadM8p(File.ReadAllText("radio.m8p"));
CodeplugFields fields = CodeplugFields.Open(image);
Console.WriteLine(FieldConsole.Get(fields, "ch0.bandwidth"));

// Live: read the codeplug off a radio latched into programming mode (power-cycle it as you connect).
using var programmer = new TaitProgrammer(new SerialPortLine("/dev/ttyUSB0"));
CodeplugImage live = programmer.ReadImage();
```

## Upgrade profiles

Composable patches that *upgrade a radio to the Packet.NET feature set* without touching its RF
config (channels, frequencies, power), so they layer safely onto a radio already provisioned for its
environment. They change the data record (0x09), the audio block (0x3B), the digital I/O lines they
program (0x37) and the PTT table (0x19). For a radio arriving from a foreign application, prefer a
clean flash of a full codeplug first, then apply a profile.

They nest: `audio-and-ptt` is the aux-connector modem wiring, `pdn-basic` is that plus the CCDI
command channel, `pdn-extra` is that plus the FFSK modem, and `pdn-internal` is `pdn-extra` moved onto
the internal options connector. Apply the one that describes the radio - the outer ones carry the inner
ones. `audio-and-ptt` is exposed on its own for a radio that needs only the wiring: one whose data
settings are already right, or one being set up for an external modem without the CCDI side.

- **`audio-and-ptt`** wires the modem to the **auxiliary connector**, and does nothing else: the
  `audio packet-defaults` block (Rx tap-out **R1**, type Split so the speaker keeps working, unmute
  **Except on PTT**; EPTT1 tap-in **T13**), **AUX_GPI1 as an active-low External PTT 1 input** (the line
  a soundcard interface or TNC keys), and **External PTT 1 transmitting Data from the Audio Tap In**
  rather than Voice from the aux mic - without that last one the line keys the radio but puts the wrong
  audio on air. Those three records - 0x19, 0x37 and 0x3B - come out byte-identical to a CPS save of the
  same configuration on a default TM8100 codeplug. It never touches the data record.
- **`pdn-basic`** is `audio-and-ptt` plus the CCDI command channel that carries `Packet.Radio.Tait`'s
  telemetry and control: averaged/instantaneous RSSI, forward/reverse power, PA temperature,
  status/identity, transmitter keying, and the PROGRESS stream for carrier-sense (DCD) and external-PTT
  edges. It sets CCDI-mode-allowed on, power-up state to Command (so the radio is always
  CCDI-reachable), progress messages on, and the command baud to 28800.
- **`pdn-extra`** includes `pdn-basic` and adds the TNC-less internal FFSK packet modem plus the SDM
  side channel used for mode signalling: transparent mode on, **ignore-escape-sequence off** (so the
  transport can escape back to command mode - without this the radio wedges), ignore-subaudible on the
  data path, the transparent terminal baud (28800) and over-air FFSK baud (2400), and SDM + CCDI SDM
  output. The over-air baud must match at both ends; adjust the bauds and the data port for your setup.
  Like `pdn-basic` it leaves the audio taps and AUX_GPI1 wired for the auxiliary connector, so the same
  codeplug also serves an external soundcard or TNC on that connector.
- **`pdn-internal`** is `pdn-extra` for a radio carrying a Packet.NET internal options board (a USB
  sound-card plus serial interface on the internal options connector). On top of `pdn-extra` it sets the
  data port to Internal Options with no flow control, routes the audio for a sound-card modem (Rx
  tap-out **R2** split, flat discriminator audio, unmuted Except on PTT so the modem hears every burst
  from its first millisecond and does its own carrier detect; EPTT1 tap-in T13 - the `audio
  packet-defaults` block with the tap point moved to R2), and programs **IOP_GPIO1 as an active-low External PTT 1 input**, the
  line the board's PTT transistor pulls low. Because the keying line moves onto the options connector
  it also **releases AUX_GPI1** back to Unassigned - the input `audio-and-ptt`, and so `pdn-basic`,
  programs for a modem on the auxiliary connector - so only the board can key the radio and a floating
  aux pin cannot. External PTT 1 stays set to transmit Data from the Audio Tap In: that is the keying
  source the board's line is wired to, only the pin changes. RF configuration is still untouched.

## PTT sources

The PTT form is record 0x19: three 31-bit entries packed LSB-first with no padding, one per keying
source - `PttSource.Ptt`, `ExternalPtt1`, `ExternalPtt2` - so entry *n* starts at bit 31*n*. One field
of each entry is mapped: bits 11-12, which carry the CPS's "PTT Transmission Type" and "Audio Source"
pair. As with the digital lines these are whole combinations lifted from real CPS saves rather than a
bit per dropdown, exposed as `PttTransmission`: `Voice` (Transmission Type Voice, Audio Source AUX MIC -
the default for all three sources) and `DataFromAudioTapIn` (Transmission Type Data, Audio Source Audio
Tap In - what an external modem keying the radio wants). Anything else reads `Other`, is preserved, and
is refused for writing. `get radio.m8p | grep ptt.` lists all three; `set radio.m8p ptt.eptt1
DataFromAudioTapIn` sets one.

## Programmable I/O digital lines

The Digital tab of the Programmable I/O form is record 0x37: one variable-length entry per line (a
6-bit line index, an 8-bit label length, the CPS "Pin" label as 7-bit ASCII, then 62 configuration
bits), fifteen entries on a TM8100. The 62 configuration bits are **not** mapped field by field. What
`CodeplugFields` knows are whole-line configurations lifted byte-for-byte from real CPS saves, exposed as
`DigitalIoRole`: `Unassigned` (the default for every line), `ExternalPtt1Input` (Input, External PTT 1,
active low - the configuration Tait's 3DK manual specifies for an external modem's PTT, as saved by the
CPS in the TARPN TM8105 template), and `BusyStatusOutput` (Output, Busy Status). Anything else reads as
`Other`, is preserved untouched, and cannot be written. `GetDigitalIoRole` / `SetDigitalIoRole` take a
`DigitalIoLine`; the console names them `gpio.aux_gpi1` .. `gpio.aux_gpio7`, `gpio.iop_gpio1` ..
`gpio.iop_gpio7` and `gpio.ch_gpio1`.

## Status and safety

The read + write path is hardware-validated against a real TM8100 (a same-image write round-tripped
every writable record byte-identical), and the field map is validated field-by-field against
real-radio CPS saves. Field writes are byte-identical to the CPS's own saves. Safety rails:

1. Snapshot the current codeplug before writing (the CLI's `patch` verb does this automatically).
2. Codeplug region only - this never writes firmware.
3. Version-pin: the write path refuses a radio whose database version is not in its validated set
   (currently 0094 / 0095); the field offsets are version-specific.
4. The field map enforces the CPS's own input rules (value ranges, character sets, and the "only
   available if ..." availability dependencies), so the library will not write a state the CPS rejects.
5. Bench on a sacrificial radio first, and re-read (after a power-cycle) to verify a write.

A single-record write is acked but not committed by the radio, so a live field change writes the whole
codeplug; that is the validated write path.

## Licence

AGPL-3.0-or-later. See [LICENSE](https://github.com/M0LTE/tait-codeplug/blob/main/LICENSE).
