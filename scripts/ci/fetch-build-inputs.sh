#!/usr/bin/env bash
# Fetches every gitignored build input from pinned public sources and lays it
# out where the csproj and Gradle expect it. Used by .github/workflows/release.yml;
# also works locally for a fresh checkout.
#
# Public sources (all sha256/sha512 pinned):
#   - Ekyso StS2-Launcher v0.2.0 APK: BCL, mono/Spine natives, libgodot_android.so
#     (the custom engine), 0Harmony.dll and GodotSharp.dll compile references.
#   - iunius612 StS2-Launcher_Mod_Manager v0.4.2 APK: the FMOD 2.03.13 runtime
#     (libfmod.so, libfmodstudio.so), its matching org.fmod Java classes, and
#     the patched libGodotFmod build that android/libs/README.md requires
#     (issue #78). Ekyso's APK has FMOD 2.02; its Java classes lack methods
#     the 2.03 libfmod.so looks up over JNI (FMOD.supportsSpatial), which
#     aborts the app on the first frame, so they must not be mixed.
#   - Godot 4.5.1 mono export templates: the godot-lib AAR.
#   - dex2jar v2.4: turns the org.fmod classes in classes.dex back into fmod.jar
#     (the FMOD SDK download itself needs an FMOD account).
#   - Godot's mono crypto JAR (raw.githubusercontent.com, 4.5.1-stable).
#
# Not public: the game's sts2.dll (compile-time reference only, never shipped).
# Pass its path in STS2_DLL; STS2_DEPS_JSON is optional.
#
# Env:
#   STS2_DLL        path to sts2.dll (required)
#   STS2_DEPS_JSON  path to sts2.deps.json (optional)
#   DEPS_CACHE      download cache dir (default: $ROOT/.ci-deps)
#   GODOT_TPZ       optional local copy of the export templates (still verified)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DEPS_CACHE="${DEPS_CACHE:-$ROOT/.ci-deps}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

EKYSO_APK_URL="https://github.com/Ekyso/StS2-Launcher/releases/download/0.2.0/StS2Launcher-v0.2.0.apk"
EKYSO_APK_SHA256="6dddbd6716c3830ec802e2672be2402bd06d4270dcf1bcd72c27e46ca05acf41"
MM_APK_URL="https://github.com/iunius612/StS2-Launcher_Mod_Manager/releases/download/v0.4.2/StS2Launcher-v0.4.2.apk"
MM_APK_SHA256="885259b87536cf4f3057ca4c30e9d12f8e0f621b42d0a4f17f708baa8baa6b01"
GODOT_TPZ_URL="https://github.com/godotengine/godot/releases/download/4.5.1-stable/Godot_v4.5.1-stable_mono_export_templates.tpz"
GODOT_TPZ_SHA512="5f4379634ca1cfc176a30da1ca5cee4407cdce0da18e1f60de12a3e7bf497f91b7e525479d90a8c13feaa4bee6a592b4ea332c81ac31501bf76f01804afe76ba"
DEX2JAR_URL="https://github.com/pxb1988/dex2jar/releases/download/v2.4/dex-tools-v2.4.zip"
DEX2JAR_SHA256="ee7c45eb3c1d2474a6145d8d447e651a736a22d9664b6d3d3be5a5a817dda23a"
CRYPTO_JAR_URL="https://raw.githubusercontent.com/godotengine/godot/4.5.1-stable/modules/mono/thirdparty/libSystem.Security.Cryptography.Native.Android.jar"
CRYPTO_JAR_SHA256="175ed3188225418aac3a6d99219a4cc8525f5fbf021175e466402b7bc1dcc7bb"
GRADLE_WRAPPER_SHA256="81a82aaea5abcc8ff68b3dfcb58b3c3c429378efd98e7433460610fecd7ae45f"

# FMOD files taken from the v0.4.2 APK, with the hashes they must have.
FMOD_LIBS=(
    "libfmod.so fbb2ee0f88bcbd79ad1449d74215f421efa2456b3397da49229986bcfc2f27ad"
    "libfmodstudio.so cfd56bcf1cc3a8f683f6aa8f23dabdb01226fb93200f50e90a914e36c785f095"
    "libGodotFmod.android.template_release.arm64.so 98b5e001103b973aea6f1757b819d290f12a240a6c712211da0f106e2d8d8ad9"
    "libGodotFmod.android.template_debug.arm64.so 98b5e001103b973aea6f1757b819d290f12a240a6c712211da0f106e2d8d8ad9"
)

UPSTREAM_PUBLISH="$ROOT/upstream/godot-export/.godot/mono/publish/arm64"
LIBS_RELEASE="$ROOT/android/libs/release"
LIBS_DEBUG="$ROOT/android/libs/debug"
BCL_DIR="$ROOT/android/assets/dotnet_bcl"
CRYPTO_JAR="$ROOT/vendor/godot/modules/mono/thirdparty/libSystem.Security.Cryptography.Native.Android.jar"

log() { echo "==> $*"; }
fail() { echo "ERROR: $*" >&2; exit 1; }

# fetch <url> <dest> <algo> <hash>: download once into the cache, always verify.
fetch() {
    local url="$1" dest="$2" algo="$3" want="$4" got
    if [ ! -f "$dest" ]; then
        log "Downloading $(basename "$dest")"
        mkdir -p "$(dirname "$dest")"
        curl -fsSL --retry 3 -o "$dest.part" "$url"
        mv "$dest.part" "$dest"
    fi
    got="$("${algo}sum" "$dest" | cut -d' ' -f1)"
    [ "$got" = "$want" ] || fail "$(basename "$dest"): $algo mismatch (got $got)"
}

check_sha256() {
    local file="$1" want="$2" got
    got="$(sha256sum "$file" | cut -d' ' -f1)"
    [ "$got" = "$want" ] || fail "$(basename "$file"): sha256 mismatch (got $got)"
}

[ -n "${STS2_DLL:-}" ] || fail "STS2_DLL is not set"
[ -f "$STS2_DLL" ] || fail "STS2_DLL not found: $STS2_DLL"
check_sha256 "$ROOT/android/gradle/wrapper/gradle-wrapper.jar" "$GRADLE_WRAPPER_SHA256"

fetch "$EKYSO_APK_URL" "$DEPS_CACHE/ekyso-v0.2.0.apk" sha256 "$EKYSO_APK_SHA256"
fetch "$MM_APK_URL" "$DEPS_CACHE/modmanager-v0.4.2.apk" sha256 "$MM_APK_SHA256"
fetch "$DEX2JAR_URL" "$DEPS_CACHE/dex-tools-v2.4.zip" sha256 "$DEX2JAR_SHA256"
fetch "$CRYPTO_JAR_URL" "$DEPS_CACHE/crypto.jar" sha256 "$CRYPTO_JAR_SHA256"

# The 1.2 GB template archive is only needed for the AAR; cache the patched
# AARs instead of the archive.
AAR_CACHE="$DEPS_CACHE/aar-4.5.1-ekyso"
if [ ! -f "$AAR_CACHE/godot-lib.template_release.aar" ] || [ ! -f "$AAR_CACHE/godot-lib.template_debug.aar" ]; then
    if [ -n "${GODOT_TPZ:-}" ]; then
        ln -s "$(realpath "$GODOT_TPZ")" "$WORK/templates.tpz"
    fi
    fetch "$GODOT_TPZ_URL" "$WORK/templates.tpz" sha512 "$GODOT_TPZ_SHA512"
    log "Extracting godot-lib AARs"
    unzip -q "$WORK/templates.tpz" templates/android_source.zip -d "$WORK/tpz"
    rm -f "$WORK/templates.tpz"
    unzip -q "$WORK/tpz/templates/android_source.zip" \
        libs/release/godot-lib.template_release.aar libs/debug/godot-lib.template_debug.aar \
        -d "$WORK/android_source"
    unzip -q "$DEPS_CACHE/ekyso-v0.2.0.apk" lib/arm64-v8a/libgodot_android.so -d "$WORK/ekyso_so"
    mkdir -p "$AAR_CACHE"
    for v in release debug; do
        aar="$WORK/android_source/libs/$v/godot-lib.template_$v.aar"
        # Same patch as setup-deps.sh: swap in Ekyso's custom libgodot_android.so.
        python3 - "$aar" "$WORK/ekyso_so/lib/arm64-v8a/libgodot_android.so" <<'PYEOF'
import os, sys, zipfile
aar, so = sys.argv[1], sys.argv[2]
entry = "jni/arm64-v8a/libgodot_android.so"
tmp = aar + ".new"
with zipfile.ZipFile(aar) as src, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as dst:
    for item in src.infolist():
        if item.filename != entry:
            dst.writestr(item, src.read(item.filename))
    dst.write(so, entry)
os.replace(tmp, aar)
PYEOF
        cp "$aar" "$AAR_CACHE/"
    done
fi

log "Extracting APKs"
unzip -q "$DEPS_CACHE/ekyso-v0.2.0.apk" 'lib/arm64-v8a/*' 'assets/dotnet_bcl/*' -d "$WORK/ekyso"
unzip -q "$DEPS_CACHE/modmanager-v0.4.2.apk" 'lib/arm64-v8a/*' 'classes*.dex' -d "$WORK/mm"

log "Populating android/libs"
for dir in "$LIBS_RELEASE" "$LIBS_DEBUG"; do
    mkdir -p "$dir/arm64-v8a"
    for so in "$WORK/ekyso/lib/arm64-v8a/"*.so; do
        case "$(basename "$so")" in
            libgodot_android.so | libc++_shared.so) continue ;; # inside the AAR
        esac
        cp -f "$so" "$dir/arm64-v8a/"
    done
    for entry in "${FMOD_LIBS[@]}"; do
        read -r name sha <<<"$entry"
        src="$WORK/mm/lib/arm64-v8a/$name"
        [ -f "$src" ] || fail "$name missing from the v0.4.2 APK"
        check_sha256 "$src" "$sha"
        cp -f "$src" "$dir/arm64-v8a/$name"
    done
done
cp -f "$AAR_CACHE/godot-lib.template_release.aar" "$LIBS_RELEASE/"
cp -f "$AAR_CACHE/godot-lib.template_debug.aar" "$LIBS_DEBUG/"

log "Rebuilding fmod.jar from the v0.4.2 classes.dex (FMOD 2.03.13)"
unzip -q "$DEPS_CACHE/dex-tools-v2.4.zip" -d "$WORK/dex2jar"
D2J="$(find "$WORK/dex2jar" -name d2j-dex2jar.sh | head -n1)"
[ -n "$D2J" ] || fail "d2j-dex2jar.sh not found in dex-tools zip"
chmod +x "$(dirname "$D2J")"/*.sh
mkdir -p "$WORK/classes"
for dex in "$WORK/mm/"classes*.dex; do
    "$D2J" -f -o "$WORK/$(basename "$dex" .dex).jar" "$dex" >/dev/null 2>&1 \
        || fail "dex2jar failed on $(basename "$dex")"
    unzip -qo "$WORK/$(basename "$dex" .dex).jar" 'org/fmod/*' -d "$WORK/classes" 2>/dev/null || true
done
[ -f "$WORK/classes/org/fmod/FMOD.class" ] || fail "org.fmod.FMOD not found in classes.dex"
# libfmod.so 2.03 calls this over JNI; a 2.02 FMOD.class crashes the app at start.
grep -aq supportsSpatial "$WORK/classes/org/fmod/FMOD.class" \
    || fail "org.fmod.FMOD has no supportsSpatial(): Java classes do not match FMOD 2.03.13"
(cd "$WORK/classes" && jar cf "$WORK/fmod.jar" org/fmod)
cp -f "$WORK/fmod.jar" "$LIBS_RELEASE/fmod.jar"
cp -f "$WORK/fmod.jar" "$LIBS_DEBUG/fmod.jar"

log "Placing the mono crypto JAR"
mkdir -p "$(dirname "$CRYPTO_JAR")"
cp -f "$DEPS_CACHE/crypto.jar" "$CRYPTO_JAR"

log "Populating android/assets/dotnet_bcl"
mkdir -p "$BCL_DIR"
cp -f "$WORK/ekyso/assets/dotnet_bcl/"*.dll "$BCL_DIR/"

log "Populating compile-time references"
mkdir -p "$UPSTREAM_PUBLISH"
cp -f "$WORK/ekyso/assets/dotnet_bcl/0Harmony.dll" "$UPSTREAM_PUBLISH/"
cp -f "$WORK/ekyso/assets/dotnet_bcl/GodotSharp.dll" "$UPSTREAM_PUBLISH/"
cp -f "$STS2_DLL" "$UPSTREAM_PUBLISH/sts2.dll"
if [ -n "${STS2_DEPS_JSON:-}" ] && [ -f "$STS2_DEPS_JSON" ]; then
    cp -f "$STS2_DEPS_JSON" "$UPSTREAM_PUBLISH/sts2.deps.json"
fi

log "Done"
