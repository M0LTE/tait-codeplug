# Changelog

What changed in each release. The section for a version is lifted into that version's GitHub release notes by `.github/workflows/publish.yml`, so this file is the source of truth for what a release says it did.

Newest first. Add a section before tagging.

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
