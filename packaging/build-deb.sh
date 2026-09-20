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
# readelf reads the library version floors out of the published binary (see the Depends
# section below). Refuse to build rather than quietly fall back to an unversioned Depends:
# understating what the package needs is the defect that block exists to fix, and a silent
# fallback would reintroduce it on any machine that happens to be missing binutils.
command -v readelf >/dev/null || { echo "readelf not found - install binutils" >&2; exit 3; }

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

# --- library version floors, read from the ELFs we just published -------------
# The executable is Microsoft's `singlefilehost` with our payload bundled into it, so its
# symbol-version floor is whatever .NET's runtime pack for this RID was built against, not
# anything this repo controls, and it moves without warning: .NET 10 raised linux-arm from
# glibc 2.16 to 2.34, which is above the 2.31 that bullseye and 32-bit Raspberry Pi OS ship.
# While Depends: said a bare `libc6`, apt installed that armhf package onto bullseye quite
# happily and the binary then died in the dynamic loader with "version `GLIBC_2.33' not
# found". So derive the floor from the ELF rather than asserting one here, and let apt refuse
# the install with a reason a user can act on. Deriving it also means the next runtime pack
# that moves the floor is handled by the build instead of by another bug report.
#
# Every ELF the package ships, not just the executable. Today that is exactly one file, because
# IncludeNativeLibrariesForSelfExtract bundles the native shims inside the single file (see the
# layout note at the top), but reading only the executable would be right by luck rather than by
# construction: a shim that publishes loose beside the binary is linked separately and does not
# share its floor. The sibling pdn-soundmodem package is the worked example - its
# libe_sqlite3.so needs glibc 2.34 on amd64 while the executable beside it needs only 2.27, so
# reading the executable alone understated that package and merely deferred this same crash to
# the first dlopen. Scanning the staged tree is what dpkg-shlibdeps would do and costs nothing.
#
# Detect ELF by its magic bytes rather than shelling out to `file`, which is not Essential and
# need not exist on a build host.
#
# The limit of the method, so nobody assumes more of it than it gives: the shims bundled inside
# the single file are compressed blobs, invisible to readelf, and .NET extracts and dlopens them
# at run time. Their floors were checked by hand from a non-single-file publish of all three
# RIDs and none exceeds the host's: linux-x64 and linux-arm64 top out at GLIBC_2.27 and
# GLIBCXX_3.4.22, linux-arm at GLIBC_2.34 and GLIBCXX_3.4.30, which is exactly what the host
# declares. Worth re-checking by hand if a package reference ever adds a native of its own, as
# Terminal.Gui's libonigwrap.so already does at a harmless GLIBC_2.14.
elf_files() {
  find "$STAGE/root" -type f -print | while IFS= read -r f; do
    [ "$(od -An -tx1 -N4 "$f" 2>/dev/null | tr -d ' \n')" = "7f454c46" ] && printf '%s\n' "$f"
  done
}

# .gnu.version_r is the authoritative record of which symbol versions of which libraries the
# loader must satisfy. Take the highest of one family (GLIBC, GLIBCXX) across the lot. "GLIBC_"
# cannot match inside "GLIBCXX_", so the two families do not overlap.
max_needed() {
  local family="$1" max="" v f
  while IFS= read -r f; do
    [ -n "$f" ] || continue
    v="$(readelf --version-info "$f" 2>/dev/null \
      | awk '/Version needs section/,0' \
      | grep -oE "${family}_[0-9][0-9.]*" \
      | sed "s/^${family}_//" \
      | sort -uV \
      | tail -1)"
    [ -n "$v" ] && max="$(printf '%s\n%s\n' "$max" "$v" | sort -uV | tail -1)"
  done <<EOF
$(elf_files)
EOF
  printf '%s' "$max"
}

# A glibc symbol version is the glibc release that introduced it, and libc6's package version
# is that same release, so this maps straight onto a Debian version constraint.
GLIBC_MIN="$(max_needed GLIBC)"
GLIBCXX_MIN="$(max_needed GLIBCXX)"
[ -n "$GLIBC_MIN" ] || { echo "could not read a GLIBC floor from the staged package" >&2; exit 4; }
[ -n "$GLIBCXX_MIN" ] || { echo "could not read a GLIBCXX floor from the staged package" >&2; exit 4; }

# libstdc++ versions its symbols by C++ ABI, not by package version, so this needs a table.
# Anchors measured against the distributions themselves: Debian 10 ships GCC 8 and tops out
# at 3.4.25, Debian 11 / GCC 10 at 3.4.28, Debian 12 / GCC 12 at 3.4.30, Debian 13 / GCC 14
# at 3.4.33. Unmeasured points round up to the next anchor, because the failure modes are not
# symmetric: too high refuses an install that would have worked and says why, too low ships
# the loader crash this whole block exists to prevent. An unknown value is a new GCC ABI
# nobody has checked, so stop and make someone extend the table.
case "$GLIBCXX_MIN" in
  3.4|3.4.[0-9]|3.4.1[0-9]|3.4.2[01]) STDCXX_MIN=5 ;;
  3.4.22)        STDCXX_MIN=6 ;;
  3.4.23|3.4.24) STDCXX_MIN=7 ;;
  3.4.25)        STDCXX_MIN=8 ;;
  3.4.26)        STDCXX_MIN=9 ;;
  3.4.27|3.4.28) STDCXX_MIN=10 ;;
  3.4.29)        STDCXX_MIN=11 ;;
  3.4.30)        STDCXX_MIN=12 ;;
  3.4.31|3.4.32) STDCXX_MIN=13 ;;
  3.4.33)        STDCXX_MIN=14 ;;
  3.4.34)        STDCXX_MIN=15 ;;
  *) echo "unknown GLIBCXX_$GLIBCXX_MIN - extend the table in $0" >&2; exit 4 ;;
esac

# Only libc6 and libstdc++6 get a constraint, and both for the same reason: they are the two
# the floor actually moved on. libgcc-s1 is left unversioned because what we ship asks it only
# for GCC_3.0, GCC_3.5 and GCC_4.2.0, which every distribution in scope has carried for twenty
# years. zlib1g and the libicu alternation are not in any shipped DT_NEEDED at all, so
# .gnu.version_r says nothing about them and there is nothing here to derive.
echo "floors for $ARCH: libc6 >= $GLIBC_MIN, libstdc++6 >= $STDCXX_MIN (GLIBCXX_$GLIBCXX_MIN)"

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
Depends: libc6 (>= $GLIBC_MIN), libgcc-s1, libstdc++6 (>= $STDCXX_MIN), zlib1g, ca-certificates, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67
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
