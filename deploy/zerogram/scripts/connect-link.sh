#!/usr/bin/env bash
# Prints the zerogram://host:port?key=... link (public information) and the key fingerprint line.
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose exec -T zerogram cat /data/state/connect-link.txt
