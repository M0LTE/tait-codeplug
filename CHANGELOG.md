# Changelog

What changed in each release. The section for a version is lifted into that version's GitHub release notes by `.github/workflows/publish.yml`, so this file is the source of truth for what a release says it did.

Newest first. Add a section before tagging.

## 0.12.0 - 2026-09-09

- **Every profile now programs the front-panel `F1` key to Squelch Override**, so the operator can open the speaker and hear what is actually on the channel whatever the squelch and subaudible signalling are doing - the thing you reach for the moment a packet station sounds wrong. It goes in at `audio-and-ptt`, which the other three all pass through, so `pdn-basic`, `pdn-extra` and `pdn-internal` all carry it. Applied to a factory-default TM8100 codeplug the key edit reproduces a CPS save of the same edit **byte for byte, in every record of the file** (DBVer 0094). Still no RF or channel config touched.
- **`audio-and-ptt` is no longer wiring-only.** It was documented as the aux-connector modem wiring and nothing else; it now also sets that key. If you apply it for the wiring alone, note that F1 moves too. Its three wiring records (0x19, 0x37, 0x3B) are unchanged, byte for byte, on every codeplug - the key lives in three items of its own.
- **The Key Settings form is readable and settable**: `key.f1` through `key.f4`, the radio's four front-panel function keys. `get radio.m8p | grep key.` lists them; `set radio.m8p key.f1 SquelchOverride` (or `Unassigned`) programs one, and `patch <port> key.f1 SquelchOverride` does it live. Programming a key touches three items, all pinned by a single-edit CPS diff: the key table (0x0F, four 20-bit entries in F1..F4 order), a 65-entry function table (0x03) that records whether a function is in use by any key, and a variable-length list of programmed keys (0x18) that a default codeplug does not carry at all. As with the digital I/O lines and the PTT sources, a key entry is read and written as a **whole validated pattern** rather than a bit per dropdown - one save cannot say which bits are the function and which its parameters. `AudibleIndicatorsVolume`, `ActionDigitalOutputLine` and `BacklightingToggle` are recognised on read from a second CPS capture but cannot be written, because their effect on the other two items has not been captured; anything else reads as `Other` and is preserved untouched.

## 0.11.0 - 2026-09-09

- **`pdn-basic` now sets the data port to Mic**, which it never did. The profile turned the CCDI command channel on and then left the data port wherever the codeplug happened to have it, so on a radio whose port was set to Aux or Internal Options the channel came out of a connector with nothing plugged into it and `Packet.Radio.Tait` saw a silent radio - the one setting still needing a CPS pass on an otherwise complete profile. Mic is the front-panel connector the host's serial lead plugs into, and the right answer for any radio without an options board. `pdn-extra` inherits it. **`pdn-internal` is unchanged**: it sets Internal Options after `pdn-basic` has run, so the board's port still wins.
- **`audio-and-ptt` is unchanged** and still never touches the data record. It is the wiring on its own, for a radio whose data settings are already right - forcing the data port there would defeat the point.

## 0.10.1 - 2026-09-08

- **An `audio-and-ptt` profile**: the modem's audio and PTT wiring on its own, for a radio that needs that and nothing else - one whose data settings are already right, or one being set up for an external modem without the CCDI side. It is the aux-connector wiring 0.9.0 added to `pdn-basic`, unchanged and unmoved: the `audio packet-defaults` block (Rx tap-out **R1**, type Split, unmute **Except on PTT**; EPTT1 tap-in **T13**), **AUX_GPI1 as an active-low External PTT 1 input**, and **External PTT 1 transmitting Data from the Audio Tap In**, and it still reproduces a CPS save of that configuration byte for byte in records 0x19, 0x37 and 0x3B. It never touches the data record. `set <file.m8p> profile audio-and-ptt` / `patch <port> profile audio-and-ptt`, and a preset in interactive mode above the three PDN ones.
- **`pdn-basic`, `pdn-extra` and `pdn-internal` are unchanged** - byte for byte, on every codeplug. `pdn-basic` still applies that wiring itself, so the profiles nest: `audio-and-ptt` < `pdn-basic` < `pdn-extra`, and `pdn-internal` is `pdn-extra` moved onto the internal options connector. Apply the one that describes the radio and it carries the rest; nothing that worked in 0.9.0 needs a second profile now.
- **Fixes 0.10.0**, which shipped the same `audio-and-ptt` profile but *removed* the wiring from `pdn-basic` and `pdn-extra` (see below) - a break from 0.9.0 that was never meant to ship. If you are on 0.10.0, upgrade: on 0.10.1 those two profiles behave exactly as they did on 0.9.0 again.

## 0.10.0 - 2026-09-08

**Superseded by 0.10.1 within the hour - do not use.** It made `pdn-basic` and `pdn-extra` stop wiring the modem's audio and PTT, a break from 0.9.0 that was never meant to ship. 0.10.1 restores them and keeps the new `audio-and-ptt` profile.

- **The modem's audio and PTT wiring is its own profile, `audio-and-ptt`, and no longer part of `pdn-basic` and `pdn-extra`.** 0.9.0 folded the aux-connector wiring into those two, which left no way to take the CCDI and FFSK settings without also having the Programmable I/O and PTT forms rewritten - unhelpful on a radio whose audio and keying are already wired, or wired somewhere other than the auxiliary connector. The wiring is unchanged and still comes out byte-identical to a CPS save of it (records 0x19, 0x37 and 0x3B on a default TM8100 codeplug); it has simply moved: `audio-and-ptt` applies the `audio packet-defaults` block (Rx tap-out **R1**, type Split, unmute **Except on PTT**; EPTT1 tap-in **T13**), programs **AUX_GPI1 as an active-low External PTT 1 input** and sets **External PTT 1 to transmit Data from the Audio Tap In**, and nothing else. `pdn-basic` and `pdn-extra` are back to the data record (0x09) alone. For a soundcard interface or TNC on the aux connector, apply both - `set radio.m8p profile audio-and-ptt` then `set radio.m8p profile pdn-extra`, or the matching pair of `patch` calls; in interactive mode the preset list now offers `audio-and-ptt` above the three PDN presets. **If you were relying on 0.9.0's `pdn-basic` or `pdn-extra` to wire the audio and PTT, you now need the extra profile.**
- **`pdn-internal` is unchanged**, and now carries the External PTT 1 transmission setting itself rather than inheriting it, since its audio and keying differ from the aux-connector set: data port Internal Options, tap-out R2, External PTT 1 transmitting Data from the Audio Tap In, IOP_GPIO1 as the active-low External PTT 1 input, and AUX_GPI1 released to Unassigned so a codeplug that has been through `audio-and-ptt` ends up keyable only by the board.

## 0.9.0 - 2026-09-06

- **`pdn-basic` and `pdn-extra` now wire the modem's audio and PTT**, which they never did: the profiles set up the CCDI channel and the FFSK modem and then left the radio with no way to hear the modem or be keyed by it, so every station still needed a CPS pass for the Programmable I/O and PTT forms. Both now apply the `audio packet-defaults` block (Rx tap-out **R1**, type Split so the speaker keeps working, unmute **Except on PTT**; EPTT1 tap-in **T13**), program **AUX_GPI1 as an active-low External PTT 1 input**, and set **External PTT 1 to transmit Data from the Audio Tap In** instead of Voice from the aux mic - without that last one the modem keys the radio but the wrong audio goes out. That is the auxiliary-connector wiring a soundcard interface or TNC uses. Applying `pdn-basic` to a factory-default TM8100 codeplug now produces records 0x19, 0x37 and 0x3B **byte-identical to a CPS save of the same configuration**, which is what the change is pinned against. RF config is still untouched, but these two profiles are no longer data-record-only: they also write the audio block (0x3B), one line of the digital I/O table (0x37) and one entry of the PTT table (0x19). `pdn-internal` inherits all of it and then **releases AUX_GPI1** again, so a radio with the options board fitted is keyable only by the board and not by a floating aux pin; External PTT 1 stays set to transmit Data from the Audio Tap In, since that is the keying source the board's line is wired to - only the pin changes. Applied to a default codeplug it moves exactly IOP_GPIO1's three bytes in the digital I/O table and nothing else.
- **The PTT form's transmission type and audio source are readable and settable.** Record 0x19 is three 31-bit entries packed with no padding, one per keying source (PTT, External PTT 1, External PTT 2), which is what let them be told apart. Bits 11-12 of an entry carry the CPS's "PTT Transmission Type" and "Audio Source" pair; as with the digital I/O lines they are mapped as whole validated combinations rather than a bit per dropdown - one CPS save cannot say which bit carries which - so `ptt.ptt`, `ptt.eptt1` and `ptt.eptt2` read `Voice`, `DataFromAudioTapIn`, or `Other` for anything unpinned, which is preserved and refused for writing.
- **Fixed: the `audio packet-defaults` block claimed the EPTT1 tap-in was both None and T13.** Byte 10 bit 7 of the audio block is the tap-in-is-None flag; the CPS clears it when you pick a node, and the block shipped with it still set. It is now cleared, in the preset and in `set <file> txtap T<n>`, so the bytes match what the CPS writes. This changes the output of `audio packet-defaults`, `pdn-internal` and `set txtap` by that one bit.
- Also now pinned by a real default codeplug: `TapOutUnmute.OnPtt` (0x00), the CPS default, which the enum was missing - a default codeplug used to read back an undefined value for `tapunmute`.
- **A `pdn-internal` profile**, for a radio with a Packet.NET internal options board (USB sound-card plus serial on the internal options connector). It is `pdn-extra` plus the three things that board needs and the CPS was the only way to set: data port Internal Options (flow control None), the audio taps for a sound-card modem (Rx tap-out R2 split, unmuted except on PTT, EPTT1 tap-in T13 - the `audio packet-defaults` block with the tap point moved to R2), and IOP_GPIO1 as an active-low External PTT 1 input. In the TUI preset list, and `set <file> profile pdn-internal` / `patch <port> profile pdn-internal` from the command line.
- **The Programmable I/O digital lines are readable and, for the roles a packet station needs, settable.** Record 0x37 decodes as fifteen variable-length entries (line index, label length, the CPS "Pin" label in 7-bit ASCII, 62 configuration bits), which is what let the lines be told apart. The configuration bits are mapped as whole validated patterns rather than fields - `Unassigned`, `ExternalPtt1Input`, `BusyStatusOutput`, lifted from a factory-default readout and the TARPN TM8105 CPS template - so `gpio.iop_gpio1` and friends read one of those (or `Other` for anything unmapped, which is preserved and refused for writing). Setting a role rewrites exactly that line's 62 bits and nothing else, and writing `ExternalPtt1Input` onto AUX_GPI1 of a default table reproduces the TARPN template's bytes.
- Bench status of the PTT line (TM8110, DBVer 0094): `gpio.iop_gpio1 ExternalPtt1Input` written and read back byte-identical, radio boots normally, answers CCDI on the internal options port, and reports no PTT at rest (so the line is not active-high). A one-second pulse from the internal options board's CM108 PTT transistor keyed the radio, reported over CCDI as `p0207` then `p0208` one second apart. The whole profile was then written to that radio and read back byte-identical, and the radio keys and answers CCDI as before.
- **Adding or deleting a channel now actually changes the radio.** The channel table and channel index grew and shrank correctly, but the entry counts for those two items in the item index (record 0x01) were left as they were. The radio sizes each item from that count, not from the bytes it is sent, so a two-channel write was accepted and committed and then read back as one channel: the added channel was silently dropped. Both counts now move with the channel count, the way the tone-table path already did. Bench-validated on a TM8100 (DBVer 0094): add a channel, write, power-cycle, read - byte-identical to what was sent; then delete it and the same again.
- **The interactive mode's backup is now the pre-change codeplug.** It was meant to be, and the README says it is, but the write backed up the image it was about to send, which already carried every edit made in the editor: `CodeplugFields` edits the image's records in place, so by write time there was no unedited copy left to save. The backup now comes from a snapshot serialised the moment the codeplug is read from the radio or loaded from a file, before anything can touch it, and is re-taken after each committed write so a second write in the same session backs up what the radio actually held. The `patch` verb was never affected: it takes its own byte snapshot before editing.

## 0.8.0 - 2026-08-21

- **`tui --driver <name>`**, and `tui --driver list` to see what your platform offers. Terminal.Gui ships three console drivers (`windows`, `ansi`, `dotnet`) and picks one for you. Since a repaint costs whatever the driver and console between them make it cost, and that varies enormously, this makes it something you can change rather than something you are stuck with.
- **`tui --bench`** times what one screen repaint actually costs on your console, because a repaint is exactly what one typed character costs. Run it per driver and use the quickest:

```
tait-codeplug tui --bench
tait-codeplug tui --bench --driver ansi
tait-codeplug tui --bench --driver dotnet
```

  It prints the screen size, the driver, and a median over 30 repaints. For scale, on Linux in tmux at 100x30 this machine gives 17.7 ms on `ansi` and 8.5 ms on `dotnet`; anything under about 30 ms feels instant, and a few hundred milliseconds is the editor feeling sluggish.

This is aimed at the report of typing being slow in the editor **on Windows, with the tool running locally**. That rules out the link, which leaves how the driver hands a repaint to the console, and on Windows that cost is far higher per call than on a Unix pty. Which of the three drivers is quickest there is not something that can be settled from a Linux box, so the tool now measures it where it matters.

## 0.7.0 - 2026-08-21

- **"Power-cycle the radio now" is a prompt, not a line in the log.** A read or a write puts it on the screen where it cannot be missed, and it takes itself back down the moment the radio answers - the normal case needs no keystroke at all. Cancel, or Esc, abandons the operation, which is the way out when the radio is not going to answer rather than sitting through the full 90-second wait.
- **Read and write show progress.** A bar and a percentage on the radio bar, from the library rather than guessed at: sections for a read, records for a write (`writing 52% (88/168)`).
- Cancelling a write is only offered up to the point where the write block opens. Past that the codeplug is being modified, and stopping half way would leave it open and partly applied, so a started write always runs to its commit.
- **The port box accepts typing again.** It is a dropdown of detected ports, and it shipped read-only, so on a machine where the radio's port does not enumerate - which a plain USB-serial cable often does not - there was no way to name one and the interactive mode could not be used at all.
- Progress redraws are throttled to a few a second. See below for why that matters more than it sounds.

**On typing being slow over SSH**, which is what prompted this release: it is real, it is measurable, and it is not something this tool can fix. Terminal.Gui repaints the entire screen for every character typed into a text box. Measured against a minimal Terminal.Gui app - one window, one text field, nothing else - so it is not something about this UI:

| terminal | per typed character |
|---|---|
| 80x24 | 13 KB |
| 100x30 | 22 KB |
| 120x40 | 37 KB |
| 200x50 | 82 KB |

That is ~7-8 bytes per cell on screen, every keystroke, and 2.4.18-develop.31 behaves identically. Locally it is invisible. On a maximised terminal over SSH it is the second or two per character that typing a frequency actually felt like. Until it is fixed upstream, three things help: a smaller terminal window while editing (80x24 is six times cheaper than 200x50), `patch <port> ch0.rxfreq 144.812500` from the command line instead of the editor, or running the tool on the machine the radio is plugged into rather than across a link.

## 0.6.1 - 2026-08-21

- **The interactive mode stops talking to the terminal when nobody is using it.** Terminal.Gui runs its main loop 25 times a second whether or not anything has changed, and rewrites cursor state every time round: sitting there with nothing happening, the tool was emitting ~315 bytes a second in 25 separate writes, for as long as it was open. The loop now steps down after ten seconds untouched and again after a minute. Measured idle output falls from 315 bytes/sec to 128 after a short pause and to 54 after a long one.
- Typing is not affected: ten keypresses measured at 29-49 ms before the change and 29-49 ms after, because ten seconds is far longer than any pause in typing. What it costs is the single keypress that wakes it up after a long pause, measured over four attempts at 248, 60, 235 and 70 ms - up to a quarter of a second, once, and everything after it is back to normal.

This is a candidate fix for "the UI goes laggy after a few minutes", not a confirmed one, and it is worth being straight about which. On a local terminal the lag does not reproduce: twelve minutes idle held latency flat at 44-73 ms, CPU at 2.1% and file handles constant, and 150 open-and-close cycles of the channel editor held latency flat at ~60 ms with no handle growth. What the tool was doing wrong regardless is the constant output, which over SSH is 25 packets a second the far end can never stop servicing. If it still goes laggy, the thing to say is which terminal and what connection - SSH, tmux, mosh, Windows Terminal - because that is where the remaining suspects live.

## 0.6.0 - 2026-08-21

- **Keyboard navigation between panels.** `F6` (and `Shift+F6`) moves between Radio, Channels, PDN preset and Log; `Tab` moves within a panel. The panel holding the keyboard now lights its border white, so where you are is visible rather than guesswork. Previously `Tab` could not leave the Radio panel at all, which also meant `Enter` on the channel list never fired: the list could not be reached.
- **Add and delete channels.** `F7` adds a channel and opens it for editing; `F8` deletes the selected one after a confirmation. There are buttons for both, and `channel add <file.m8p>` / `channel delete <file.m8p> <n>` do the same from the command line.
- A new channel starts as a copy of the one before it, because a zeroed channel is 0 Hz at power Off, which is never what you want.
- Deleting a channel shifts the ones above it down and clears a GPS poll-response channel left pointing past the end, which is exactly what the CPS rejects on load.
- **The serial port is a dropdown** of detected ports with a Rescan button, instead of a box you had to know what to type into. A port that did not enumerate can still be typed in.
- The log is capped at 500 lines so a session left open all day cannot grow it without bound.

Adding or removing a channel changes the codeplug's shape, so it is worth saying what it is pinned against: growing the CPS's own 1-channel default file to 2 and to 6 channels reproduces, byte for byte, the channel index table and the record chunking found in a real 2-channel radio readout and a real 6-channel CPS save. Nine tests hold that. It has not yet been written to a radio or loaded back into the CPS.

## 0.5.0 - 2026-08-21

- **`tait-codeplug --upgrade`**: fetch the latest release for this platform and replace the running binary in place. No more download-and-chmod to move up a version.
- The download is verified against the release's own `SHA256SUMS` and discarded on a mismatch, so nothing unaccounted-for is ever installed.
- The swap is an atomic rename, and the existing file mode is preserved, so a failure at any point leaves the working binary exactly as it was.
- Refuses early, in under a tenth of a second, when it cannot write where the binary lives, rather than pulling 40 MB down first to then fail. Points at `sudo` when the directory is system-owned.
- Refuses to replace a renamed copy or a build-tree binary.

## 0.4.1 - 2026-08-20

- The interactive mode is in colour instead of Terminal.Gui's stock grey-on-black: a dark slate palette with a blue accent on panel borders and titles.
- Green for read, amber for write because it is the one that changes your radio, red for the error dialog.
- The port box sits on an inset background so it reads as somewhere to type, and the status line turns green once a codeplug is loaded.
- Rounded panel borders; the window title carries the version.
- An empty channel pane now tells you to press F5 rather than showing a blank box.
- Colours are 24-bit and map down automatically on a 16- or 256-colour terminal.

## 0.4.0 - 2026-08-20

- **Interactive mode, and it is what you get when you run the tool with no arguments**: a serial port selector, the channel table (frequency, bandwidth, power), a PDN preset picker, and read/write buttons.
- `F5` reads the radio, `F3` or Enter edits the selected channel, `F2` writes back, `F10` quits.
- The PDN preset is staged rather than applied on selection, so choosing `pdn-basic` or `pdn-extra` changes nothing until you write.
- A write always snapshots the pre-change codeplug to a backup file first, the same rule the `patch` verb follows.
- The radio work runs off the UI thread, so the screen stays live through the ~25s read and the 90s the connect spends waiting for your power-cycle, with a log pane narrating.
- `tait-codeplug tui [file.m8p]` opens the same screen on a saved codeplug, so the editor can be used without a radio on the bench.
- `--help` / `-h` / `help` print usage and exit 0; a no-argument run with redirected output still prints usage rather than trying to draw a UI at a pipe.

## 0.3.0 - 2026-08-20

First release from this repository. The tool and its library moved here from [`packet-net/packet.net`](https://github.com/packet-net/packet.net), with their history, and continue that version numbering.

- The library is now published to nuget.org as [`M0LTE.Tait.Codeplug`](https://www.nuget.org/packages/M0LTE.Tait.Codeplug), so it can be consumed without vendoring the source.
- The CLI project and namespace are renamed to match; the shipped command is still `tait-codeplug`.
- Same six self-contained, single-file binaries as before: linux-x64 / arm64 / arm, win-x64, osx-x64 / arm64.

Carried over from the work done in packet.net, and what the tool can do as of this release:

- Read and write a TM8100 / TM8200 codeplug over the serial programming interface without the Windows CPS. Hardware-validated: a same-image write round-trips every writable record byte-identical.
- `parse` / `dump` / `get` take their source from either an `.m8p` file or a serial port, so the decode verbs work against a live radio.
- The whole CPS **Data** form is mapped and typed (General, Serial Communications, RF Modems, SDM, Transparent Mode, GPS, Customer Data), plus the channel table: frequency, bandwidth, power, split TX, squelch, TX inhibit, network, and full CTCSS/DCS read and write.
- The `pdn-basic` and `pdn-extra` upgrade profiles configure a radio for the Packet.NET feature set without touching its RF or channel config.
- Writes are version-pinned to a validated database version and refuse anything else; `patch` backs up the pre-change codeplug before writing; the raw whole-file write verb is deliberately absent.
- The field map enforces the CPS's own input rules and "only available if" dependencies, sourced from the manual, so the tool will not write a state the CPS rejects.
