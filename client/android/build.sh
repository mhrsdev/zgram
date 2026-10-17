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

# ---- release identity ------------------------------------------------------------------------------
# Version shown to users and used by the in-app updater (release tag on GitHub = v<ZG_VERSION_NAME>).
. "$here/VERSION"
# Releases are signed with the maintainer's OWN key. It is created on first use, lives outside git
# (client/android/.keystore, mode 600) and MUST be backed up: Android only installs an update that is signed
# with the same key as the installed app, so a lost key means every user has to uninstall and reinstall.
ks_dir=${ZEROGRAM_KEYSTORE_DIR:-$here/.keystore}
ks=${ZEROGRAM_KEYSTORE:-$ks_dir/zerogram-release.jks}
ks_pass=${ZEROGRAM_KEYSTORE_PASSWORD:-}
if [ ! -f "$ks" ]; then
  command -v keytool >/dev/null || { echo "keytool (JDK) is needed to create the release key" >&2; exit 1; }
  umask 077
  mkdir -p "$(dirname "$ks")"
  ks_pass=$(head -c 48 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 32)
  keytool -genkeypair -keystore "$ks" -storetype PKCS12 -storepass "$ks_pass" -keypass "$ks_pass" \
    -alias zerogram -keyalg RSA -keysize 4096 -validity 36500 \
    -dname "CN=Zerogram, O=Zerogram" >/dev/null 2>&1
  printf '%s\n' "$ks_pass" > "$(dirname "$ks")/password"
  chmod 600 "$ks" "$(dirname "$ks")/password"
  echo "Created a NEW release key: $ks (password in $(dirname "$ks")/password)." >&2
  echo "BACK IT UP NOW and never commit it. Updates are impossible without this exact key." >&2
fi
if [ -z "$ks_pass" ]; then
  ks_pass=$(cat "$(dirname "$ks")/password" 2>/dev/null || true)
fi
[ -n "$ks_pass" ] || { echo "set ZEROGRAM_KEYSTORE_PASSWORD (or keep it in $(dirname "$ks")/password)" >&2; exit 1; }
export ORG_GRADLE_PROJECT_RELEASE_STORE_PASSWORD="$ks_pass"
export ORG_GRADLE_PROJECT_RELEASE_KEY_PASSWORD="$ks_pass"
export ORG_GRADLE_PROJECT_RELEASE_KEY_ALIAS=${ZEROGRAM_KEY_ALIAS:-zerogram}

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
  :TMessagesProj_AppStandalone:assembleAfatStandalone -Pandroid.injected.build.abi="$abi" -Pandroid.injected.testOnly=false \
  -PZG_KEYSTORE="$ks" -PAPP_VERSION_CODE=$((7038 + ZG_RELEASE)) -PAPP_VERSION_NAME="$ZG_VERSION_NAME")

apk=$(find "$src/TMessagesProj_AppStandalone/build" -name 'app.apk' -path '*standalone*' | head -n1)
[ -n "$apk" ] || { echo "no APK produced" >&2; exit 1; }
name="zerogram-${abi//,/+}.apk"
cp "$apk" "$out/$name"
( cd "$out" && sha256sum "$name" > "$name.sha256" )
echo "APK: $out/$name"
