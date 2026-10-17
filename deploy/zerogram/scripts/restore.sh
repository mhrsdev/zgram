#!/usr/bin/env bash
# Restores a backup made by backup.sh into the data volume (replaces the current contents).
set -euo pipefail
cd "$(dirname "$0")/.."
[ $# -eq 1 ] || { echo "usage: $0 backups/zerogram-XXXX.tar.gz" >&2; exit 2; }
archive="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
read -r -p "This REPLACES all server data with $archive. Type YES to continue: " answer
[ "$answer" = "YES" ] || exit 1
docker compose stop zerogram
docker run --rm --network none -u 0 -v zerogram_zerogram-data:/data -v "$archive":/in.tgz:ro \
  --entrypoint sh "${ZEROGRAM_IMAGE:-zerogram:local}" -c 'rm -rf /data/* /data/.[!.]* 2>/dev/null; tar -xzf /in.tgz -C /data && chown -R 1654:1654 /data && chmod 700 /data'
docker compose up -d zerogram
