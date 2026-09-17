Standalone Tait TM8100/TM8200 codeplug CLI - read, decode, edit and program a codeplug over the serial interface without the Windows CPS, and apply the Packet.NET (`audio-and-ptt` / `pdn-basic` / `pdn-extra`) upgrade profiles.

Each binary is **self-contained** (the .NET runtime and the native serial library are embedded) and **single-file** - no .NET install needed. Download the one for your platform and run.

**Debian / Ubuntu / Raspberry Pi OS** - install it from the packet-net apt repository instead, and `apt upgrade` keeps it current:
```
curl -fsSL https://packet-net.github.io/apt/pubkey.asc | sudo gpg --dearmor -o /usr/share/keyrings/packet-net.gpg
echo "deb [signed-by=/usr/share/keyrings/packet-net.gpg] https://packet-net.github.io/apt ./" | sudo tee /etc/apt/sources.list.d/packet-net.list
sudo apt update && sudo apt install tait-codeplug
```
The `.deb` assets below are the same build, for installing by hand: `sudo apt install ./tait-codeplug___VER___amd64.deb`.

**Linux / macOS**
```
curl -LO https://github.com/__REPO__/releases/download/v__VER__/tait-codeplug-__VER__-linux-x64   # or linux-arm64 / linux-arm / osx-x64 / osx-arm64
chmod +x tait-codeplug-__VER__-*
./tait-codeplug-__VER__-linux-x64 --help
```

**Windows**: download `tait-codeplug-__VER__-win-x64.exe` and run it.

Assets: `linux-x64`, `linux-arm64`, `linux-arm` (armv7 / 32-bit Pi), `win-x64`, `osx-x64` (Intel), `osx-arm64` (Apple Silicon), plus `.deb` packages for `amd64`, `arm64` and `armhf`. `SHA256SUMS` covers every asset - verify with `sha256sum -c SHA256SUMS` (or `shasum -a 256 -c` on macOS).

Already have an older copy? `tait-codeplug --upgrade` pulls this release for your platform, verifies it against `SHA256SUMS`, and replaces itself in place. If you installed it from apt, use `sudo apt update && sudo apt install --only-upgrade tait-codeplug` instead: that copy belongs to dpkg, and `--upgrade` will say so rather than overwrite it.

Run it with no arguments for the interactive editor: a port selector, the channel table (frequency, bandwidth, power), the PDN preset picker, and read/write buttons. `tait-codeplug tui <file.m8p>` opens it on a saved codeplug instead of reading a radio.

Common commands: `dump <file.m8p | port>` (decode every field), `get` / `set <file.m8p> <field> <value>`, `set <file.m8p> profile audio-and-ptt|pdn-basic|pdn-extra`, and the hardware verbs `version` / `read` / `patch <port> ...` (power-cycle the radio into programming mode as you trigger). The write path is version-pinned and backs up before writing; it never touches firmware.

The library behind the CLI ships as [`M0LTE.Tait.Codeplug`](https://www.nuget.org/packages/M0LTE.Tait.Codeplug) at the same version.
