#!/usr/bin/env bash
# CI build: patcher publish -> DLL copy -> bootstrap.pck -> signed APK.
# The explicit version of scripts/build.sh without its csharpier format step
# (a different csharpier version reformats unrelated files) or its version bump
# (CI builds exactly what gradle.properties says).
#
# Run scripts/ci/fetch-build-inputs.sh first.
#
# Env (all required unless noted):
#   RELEASE_KEYSTORE_FILE      path to the release keystore (.p12)
#   RELEASE_KEYSTORE_ALIAS     key alias
#   RELEASE_KEYSTORE_PASSWORD  store and key password (same value)
#   GRADLE                     gradle command (optional, default ./gradlew)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PATCHER_DIR="$ROOT/src/STS2Mobile"
PUBLISH_DIR="$PATCHER_DIR/bin/Release/net9.0/publish"
BCL_DIR="$ROOT/android/assets/dotnet_bcl"
GRADLE_PROPS="$ROOT/android/gradle.properties"

fail() { echo "ERROR: $*" >&2; exit 1; }

for v in RELEASE_KEYSTORE_FILE RELEASE_KEYSTORE_ALIAS RELEASE_KEYSTORE_PASSWORD; do
    [ -n "${!v:-}" ] || fail "$v is not set"
done
[ -f "$RELEASE_KEYSTORE_FILE" ] || fail "keystore not found: $RELEASE_KEYSTORE_FILE"

echo "==> dotnet publish"
dotnet publish "$PATCHER_DIR" -c Release --nologo

echo "==> Copying patcher assemblies into $BCL_DIR"
mkdir -p "$BCL_DIR"
# Every assembly the patcher publishes ships in the APK (the game/engine
# references are Private=false, so they never land in publish/).
shopt -s nullglob
dlls=("$PUBLISH_DIR"/*.dll)
[ ${#dlls[@]} -gt 0 ] || fail "no DLLs in $PUBLISH_DIR"
cp -f "${dlls[@]}" "$BCL_DIR/"
cp -f "$ROOT/upstream/godot-export/.godot/mono/publish/arm64/GodotSharp.dll" "$BCL_DIR/"

echo "==> bootstrap.pck"
python3 "$ROOT/scripts/make-bootstrap-pck.py"
[ -f "$ROOT/android/assets/bootstrap.pck" ] || fail "bootstrap.pck was not generated"

VERSION_NAME="$(grep '^export_version_name=' "$GRADLE_PROPS" | cut -d= -f2)"

echo "==> Gradle assembleMonoRelease ($VERSION_NAME)"
cd "$ROOT/android"
# The password goes in through the environment (ORG_GRADLE_PROJECT_*), never on
# the command line, so it can't show up in process listings or logs.
ORG_GRADLE_PROJECT_release_keystore_file="$RELEASE_KEYSTORE_FILE" \
ORG_GRADLE_PROJECT_release_keystore_alias="$RELEASE_KEYSTORE_ALIAS" \
ORG_GRADLE_PROJECT_release_keystore_password="$RELEASE_KEYSTORE_PASSWORD" \
    ${GRADLE:-./gradlew} --no-daemon assembleMonoRelease

APK="$ROOT/android/build/outputs/apk/mono/release/StS2LauncherRedux-v$VERSION_NAME.apk"
[ -f "$APK" ] || fail "APK not found: $APK"
echo "APK=$APK"
