#!/usr/bin/env bash
# Consistent backup of the data volume (stops the server for a few seconds).
# WARNING: the archive contains the server RSA private key, account database and any *undelivered*
# messages still queued. Treat it as secret; do not keep old copies longer than needed.
set -euo pipefail
cd "$(dirname "$0")/.."
umask 077
out="${1:-backups/zerogram-$(date +%Y%m%d-%H%M%S).tar.gz}"
mkdir -p "$(dirname "$out")"
docker compose stop zerogram
trap 'docker compose start zerogram >/dev/null' EXIT
docker run --rm --network none -v zerogram_zerogram-data:/data:ro -v "$(cd "$(dirname "$out")" && pwd)":/out \
  --entrypoint tar "${ZEROGRAM_IMAGE:-zerogram:local}" -C /data -czf "/out/$(basename "$out")" .
chmod 600 "$out"
echo "Backup written to $out"
