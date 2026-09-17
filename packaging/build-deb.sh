#!/usr/bin/env bash
# Builds the tait-codeplug .deb for one architecture.
#
#   packaging/build-deb.sh <version> [amd64|arm64|armhf] [outdir]
#
# Produces <outdir>/tait-codeplug_<version>_<arch>.deb. Default outdir is <repo>/artifacts.
#
# The publish flags here are deliberately identical to the six-arch loop in
# .github/workflows/publish.yml, so the binary inside the .deb is the same program as the
# release asset for that arch. If one list changes, change both.
#
# This is a plain CLI tool: no daemon, no user to create, no config to seed. So no systemd
# unit and no maintainer scripts at all - dpkg unpacking the files is the whole install.
#
# Layout note, checked rather than assumed: IncludeNativeLibrariesForSelfExtract=true bundles
# the System.IO.Ports native shim into the single file, so `dotnet publish` emits exactly one
# executable and nothing beside it. /usr/bin/tait-codeplug is therefore a plain file. (The
# sibling pdn-soundmodem package needs a payload directory under /usr/lib and a symlink into
# it, because its publish leaves the .so loose and .NET resolves it relative to the real path
# of the running binary. If a future publish here starts dropping anything beside the
# executable, this script needs the same treatment - the Verify step in publish.yml would
# not catch it, so check the publish listing when the flags or the SDK change.)
set -euo pipefail

VERSION="${1:?usage: build-deb.sh <version> [arch] [outdir]}"
ARCH="${2:-amd64}"

# A Debian version must start with a digit. Note for later, if a prerelease scheme is ever
# wanted: 1.0.0~rc1 sorts BEFORE 1.0.0, whereas 1.0.0-rc1 sorts after it, so a tag like
# v1.0.0-rc1 would make apt treat the candidate as newer than the eventual release.
case "$VERSION" in
  [0-9]*) ;;
  *) echo "version '$VERSION' does not start with a digit, which Debian requires" >&2; exit 2 ;;
esac

case "$ARCH" in
  amd64) RID=linux-x64 ;;
  arm64) RID=linux-arm64 ;;
  armhf) RID=linux-arm ;;
  *) echo "unsupported arch $ARCH" >&2; exit 2 ;;
esac

# dpkg-deb ships in the Essential `dpkg` package, so this only trips on a non-Debian host.
command -v dpkg-deb >/dev/null || { echo "dpkg-deb not found - this needs a Debian-family host" >&2; exit 3; }

# Directories inherit the caller's umask, and a developer box set to 002 produces
# group-writable 0775 directories inside the package, which is not what a .deb should ship
# (lintian: non-standard-dir-perm). Pin it so the package is the same from any shell.
umask 022

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
OUTDIR="${3:-$ROOT/artifacts}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

DOCDIR=/usr/share/doc/tait-codeplug
DATADIR=/usr/share/tait-codeplug

dotnet publish "$ROOT/src/M0LTE.Tait.Codeplug.Cli/M0LTE.Tait.Codeplug.Cli.csproj" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:Version="$VERSION" \
  -p:DebugType=none \
  -p:GenerateDocumentationFile=false \
  --output "$STAGE/publish"

mkdir -p "$STAGE/root/usr/bin" \
         "$STAGE/root$DATADIR" \
         "$STAGE/root$DOCDIR" \
         "$STAGE/root/DEBIAN"

install -m 0755 "$STAGE/publish/tait-codeplug" "$STAGE/root/usr/bin/tait-codeplug"

# The marker `--upgrade` looks for. A copy installed by dpkg is dpkg's to replace: letting
# --upgrade overwrite it would leave the package manager's idea of the file wrong, and the
# next `apt install --only-upgrade` would put the old binary back. SelfUpgrade refuses when
# this file exists AND it is running from under /usr, and says to use apt instead. A file,
# not a `dpkg -S` shell-out: no dependency on dpkg being present, deterministic, and it
# cannot false-positive on a binary someone downloaded to a machine that has the package.
cat > "$STAGE/root$DATADIR/installed-from-apt" <<EOF
https://packet-net.github.io/apt
EOF
chmod 0644 "$STAGE/root$DATADIR/installed-from-apt"

install -m 0644 "$HERE/copyright" "$STAGE/root$DOCDIR/copyright"

# Debian changelog. A numeric SOURCE_DATE_EPOCH keeps rebuilds of a tag byte-identical;
# anything else (an ISO string from a CI event payload, say) falls back to now rather than
# failing the build on `date -R`.
case "${SOURCE_DATE_EPOCH:-}" in
  ''|*[!0-9]*) CHANGELOG_DATE="$(date -R)" ;;
  *)           CHANGELOG_DATE="$(date -R --date="@$SOURCE_DATE_EPOCH")" ;;
esac
cat > "$STAGE/changelog.Debian" <<EOF
tait-codeplug ($VERSION) unstable; urgency=medium

  * Release $VERSION. Notes:
    https://github.com/M0LTE/tait-codeplug/releases/tag/v$VERSION

 -- Tom Fanning M0LTE <tom@m0lte.uk>  $CHANGELOG_DATE
EOF
gzip -9n -c "$STAGE/changelog.Debian" > "$STAGE/root$DOCDIR/changelog.Debian.gz"
chmod 0644 "$STAGE/root$DOCDIR/changelog.Debian.gz"

INSTALLED_SIZE="$(du -k -s --exclude=DEBIAN "$STAGE/root" | cut -f1)"

# Depends: the native prerequisites a self-contained .NET app still needs from the system.
# ICU is the awkward one - Debian stamps the soname into the package name, so there is no
# stable name to depend on and the list has to be an alternation that dpkg satisfies with
# whichever one the target distro ships (76 trixie, 74 ubuntu 24.04, 72 bookworm, 71/70
# ubuntu 22.04, 67 bullseye). This is what Microsoft's own dotnet-runtime .debs do. The
# alternative would be InvariantGlobalization=true, which is a behaviour change, not a
# packaging one. ca-certificates is for the HTTPS fetch --upgrade does.
cat > "$STAGE/root/DEBIAN/control" <<EOF
Package: tait-codeplug
Version: $VERSION
Architecture: $ARCH
Maintainer: Tom Fanning M0LTE <tom@m0lte.uk>
Installed-Size: $INSTALLED_SIZE
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, ca-certificates, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67
Section: hamradio
Priority: optional
Homepage: https://github.com/M0LTE/tait-codeplug
Description: Tait TM8100/TM8200 codeplug tool
 Read, decode, edit and program the codeplug of a Tait TM8100 or TM8200 mobile
 over a serial cable, without the Windows CPS. There is an interactive mode for
 browsing and editing a radio or a saved file, and one-shot commands for
 scripting, plus the Packet.NET upgrade profiles that set a radio up for a
 packet modem in one step.
 .
 This is a self-contained build: it bundles the .NET runtime, so the machine it
 runs on needs no .NET installed.
 .
 AGPL-3.0-or-later.
EOF

mkdir -p "$OUTDIR"
DEB="$OUTDIR/tait-codeplug_${VERSION}_${ARCH}.deb"
# -Zxz, not the host dpkg's default. A recent dpkg (and Ubuntu's, for years) builds
# control.tar.zst/data.tar.zst, and dpkg only learned to read zstd in 1.21.18: bullseye ships
# 1.20.14, which refuses the archive outright with "unknown compression for member
# control.tar.zst" before it gets as far as the dependencies. Raspberry Pi OS bullseye on
# armhf is exactly the machine this package is for, and it is also the distro the libicu67
# alternative above exists to serve, so the package has to be readable there. xz is the
# portable choice and costs nothing here: the payload is a single-file bundle that is already
# compressed internally, so neither format gains much on it.
dpkg-deb -Zxz --build --root-owner-group "$STAGE/root" "$DEB"
echo "built $DEB"
