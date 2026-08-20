# tait-codeplug

Read, decode, edit and program a **Tait TM8100 / TM8200** codeplug over the serial programming
interface, on Linux, macOS or Windows, without the Windows CPS.

Reverse-engineered from Free Serial Analyzer captures of the CPS and hardware-validated against a
real TM8100: a same-image write round-trips every writable record byte-identical, and the field map
is validated field-by-field against real-radio CPS saves.

Two things ship from this repo, at the same version:

| | |
|---|---|
| **`tait-codeplug`** | the CLI, as a self-contained single-file binary for six platforms - [latest release](https://github.com/M0LTE/tait-codeplug/releases/latest) |
| **`M0LTE.Tait.Codeplug`** | the library behind it, on [nuget.org](https://www.nuget.org/packages/M0LTE.Tait.Codeplug) |

## Install the CLI

Each binary embeds the .NET runtime and the native serial library, so there is nothing else to
install. Grab the one for your platform from the [latest release](https://github.com/M0LTE/tait-codeplug/releases/latest):

```sh
curl -LO https://github.com/M0LTE/tait-codeplug/releases/latest/download/tait-codeplug-<version>-linux-x64
chmod +x tait-codeplug-<version>-linux-x64
./tait-codeplug-<version>-linux-x64
```

Assets: `linux-x64`, `linux-arm64`, `linux-arm` (armv7 / 32-bit Pi), `win-x64`, `osx-x64` (Intel),
`osx-arm64` (Apple Silicon). `SHA256SUMS` covers every asset.

Or build it yourself: `dotnet run --project src/M0LTE.Tait.Codeplug.Cli -- <verb> ...` (.NET 10 SDK).

## Use it

```
# decode - the source is an .m8p file OR a serial port (reads the live radio)
tait-codeplug parse   <file.m8p | port>            verify checksums + print the section map
tait-codeplug dump    <file.m8p | port>            decode every mapped field
tait-codeplug get     <file.m8p | port> [field]    read one field, or all as name=value
tait-codeplug set     <file.m8p> <field> <value>   set one field and save (e.g. ch0.bandwidth Wide)
tait-codeplug set     <file.m8p> profile <name>    apply a PDN upgrade profile to a file

# hardware (radio latched into programming mode on <port>: power-cycle it as you trigger)
tait-codeplug version <port>                       interrogate: model / firmware / serial
tait-codeplug read    <port> [out.m8p]             read the codeplug (to a file, or stdout if omitted)
tait-codeplug patch   <port> <field> <value>       live-set one field (backs up first)
tait-codeplug patch   <port> profile <name>        live-apply a PDN upgrade profile
```

The radio must be latched into programming mode: power-cycle it as the command connects. Progress and
prompts go to stderr, so `read <port> > radio.m8p` gives you a clean `.m8p` on stdout.

## PDN upgrade profiles

`pdn-basic` and `pdn-extra` upgrade a radio to the [Packet.NET](https://github.com/packet-net/packet.net)
feature set - CCDI telemetry and control, and the TNC-less internal FFSK packet modem plus SDM mode
signalling - **without touching RF config** (channels, frequencies, power), so they layer safely onto a
radio already provisioned for its environment. See the
[library README](src/M0LTE.Tait.Codeplug/README.md#pdn-upgrade-profiles) for exactly what each one sets.

## Safety

1. `patch` snapshots the current codeplug to a backup file before writing. Keep it.
2. Codeplug region only. This never writes firmware.
3. Version-pinned: the write path refuses a radio whose database version is not in its validated set
   (currently 0094 / 0095), because the field offsets are version-specific.
4. The field map enforces the CPS's own input rules, so the tool will not write a state the CPS rejects.
5. Bench on a sacrificial radio first, and re-read after a power-cycle to verify a write.

No RF is involved in any of this, and no part of it transmits.

## Protocol and provenance

The protocol write-up is [`docs/research/tait-codeplug-protocol.md`](https://github.com/packet-net/packet.net/blob/main/docs/research/tait-codeplug-protocol.md)
and the programming brief is [`docs/research/tait-codeplug-programming-brief.md`](https://github.com/packet-net/packet.net/blob/main/docs/research/tait-codeplug-programming-brief.md),
both in [packet-net/packet.net](https://github.com/packet-net/packet.net), where this code was
developed before moving here. Its history came with it. That repo also holds `Packet.Radio.Tait`, the
runtime CCDI/transparent-mode driver these profiles provision a radio for.

## Releasing

A `v*` tag runs [`.github/workflows/release.yml`](.github/workflows/release.yml): it gates on the test
suite, pushes `M0LTE.Tait.Codeplug` to nuget.org via trusted publishing (OIDC, no stored API key), then
cross-publishes the six CLI binaries and attaches them plus `SHA256SUMS` to a GitHub Release.

```sh
git tag -a v0.3.0 -m "v0.3.0 - <one-line summary>" && git push origin v0.3.0
```

## Licence

AGPL-3.0-or-later. See [LICENSE](LICENSE).
