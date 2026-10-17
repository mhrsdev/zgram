#!/usr/bin/env bash
# Creates (or, with --force, replaces) the 12-word identity of the server owner.
# The words are printed ONCE to this terminal; the server stores only a key derived from them.
set -euo pipefail
cd "$(dirname "$0")/.."
exec docker compose run --rm --no-deps -T zerogram --owner-init "$@"
