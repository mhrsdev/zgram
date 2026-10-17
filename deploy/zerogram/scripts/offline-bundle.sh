#!/usr/bin/env bash
# Run on a machine WITH internet access: builds the image and packs everything needed to install
# on a server inside Iran that cannot reach the internet.
set -euo pipefail
cd "$(dirname "$0")/../../.."
umask 022
out="${1:-dist}"
mkdir -p "$out"
docker build -f deploy/zerogram/Dockerfile -t zerogram:local .
docker save zerogram:local | gzip -9 > "$out/zerogram-image.tar.gz"
tar -czf "$out/zerogram-deploy.tar.gz" -C deploy zerogram --exclude=zerogram/publish --exclude=zerogram/.env --exclude=zerogram/backups
( cd "$out" && sha256sum zerogram-image.tar.gz zerogram-deploy.tar.gz > SHA256SUMS )
echo "Bundle in $out/: zerogram-image.tar.gz zerogram-deploy.tar.gz SHA256SUMS"
