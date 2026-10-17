#!/usr/bin/env bash
# Update to a new image. Keeps the previous image tagged zerogram:previous so rollback.sh can undo it.
#   ./scripts/update.sh                      # rebuild from the source tree next to this deployment
#   ./scripts/update.sh zerogram-image.tar.gz  # load an offline image bundle instead
set -euo pipefail
cd "$(dirname "$0")/.."
image="${ZEROGRAM_IMAGE:-zerogram:local}"
if docker image inspect "$image" >/dev/null 2>&1; then
  docker tag "$image" zerogram:previous
fi
if [ $# -ge 1 ]; then
  gzip -dc "$1" | docker load
else
  docker compose build zerogram
fi
# The server shuts down gracefully (SIGTERM) so counters and queue are flushed before the new one starts.
docker compose up -d zerogram
echo "Updated. If something is wrong run ./scripts/rollback.sh"
