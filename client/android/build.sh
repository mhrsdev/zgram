#!/usr/bin/env bash
# Builds the Zerogram Android APK from the pinned upstream Telegram for Android source plus our patches.
#   ./client/android/build.sh [abi]       abi: arm64-v8a (default) | armeabi-v7a | "arm64-v8a,armeabi-v7a"
# Needs: git, JDK 17/21, Android SDK (ANDROID_HOME) with platform 36, build-tools 36.0.0, NDK 27.2.12479018, CMake 3.22.1.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
. "$here/PIN"
abi=${1:-arm64-v8a}
work=${ZEROGRAM_ANDROID_WORK:-$here/.work}
src=$work/source
out=$here/out
sdk=${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}
[ -d "$sdk" ] || { echo "ANDROID_HOME is not set / not a directory" >&2; exit 1; }
[ -d "$sdk/ndk/$ANDROID_NDK" ] || { echo "NDK $ANDROID_NDK missing in $sdk/ndk" >&2; exit 1; }
[ -d "$sdk/platforms/android-$ANDROID_COMPILE_SDK" ] || { echo "platform android-$ANDROID_COMPILE_SDK missing" >&2; exit 1; }
mkdir -p "$work" "$out"

if [ ! -d "$src/.git" ]; then
  git init -q "$src"
  git -C "$src" remote add origin "$ANDROID_REPOSITORY"
fi
git -C "$src" fetch --depth 1 origin "$ANDROID_COMMIT"
git -C "$src" checkout -q -f "$ANDROID_COMMIT"
git -C "$src" clean -fdxq -e local.properties -e build -e .gradle || true
GIT_LFS_SKIP_SMUDGE=1 git -C "$src" submodule update --init --recursive --depth 1

for patch in "$here"/patches/*.patch; do
  echo "applying $(basename "$patch")"
  git -C "$src" apply --index "$patch"
done

cat > "$src/local.properties" <<PROPS
sdk.dir=$sdk
ndk.dir=$sdk/ndk/$ANDROID_NDK
PROPS

(cd "$src" && ./gradlew --no-daemon -Dorg.gradle.jvmargs="-Xmx4g -XX:MaxMetaspaceSize=1g" \
  :TMessagesProj_AppStandalone:assembleAfatStandalone -Pandroid.injected.build.abi="$abi")

apk=$(find "$src/TMessagesProj_AppStandalone/build" -name 'app.apk' -path '*standalone*' | head -n1)
[ -n "$apk" ] || { echo "no APK produced" >&2; exit 1; }
name="zerogram-${abi//,/+}.apk"
cp "$apk" "$out/$name"
( cd "$out" && sha256sum "$name" > "$name.sha256" )
echo "APK: $out/$name"
