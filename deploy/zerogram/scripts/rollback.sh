#!/usr/bin/env bash
# Go back to the image that was running before the last update.sh.
set -euo pipefail
cd "$(dirname "$0")/.."
image="${ZEROGRAM_IMAGE:-zerogram:local}"
docker image inspect zerogram:previous >/dev/null 2>&1 || { echo "no previous image to roll back to" >&2; exit 1; }
docker tag "$image" zerogram:rolled-back-from 2>/dev/null || true
docker tag zerogram:previous "$image"
docker compose up -d --force-recreate zerogram
echo "Rolled back to the previous image. Data volume was not touched."
