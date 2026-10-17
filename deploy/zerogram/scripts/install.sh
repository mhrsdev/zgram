#!/usr/bin/env bash
# First-time setup: creates .env with a fresh master key, then prints the next steps.
set -euo pipefail
cd "$(dirname "$0")/.."
umask 077
if [ -f .env ] && grep -q '^ZEROGRAM_MASTER_KEY=.\+' .env; then
  echo ".env already has a master key; leaving it untouched."
  exit 0
fi
[ -f .env ] || cp .env.example .env
key=$(head -c 32 /dev/urandom | base64 -w0 2>/dev/null || head -c 32 /dev/urandom | base64 | tr -d '\n')
if grep -q '^ZEROGRAM_MASTER_KEY=' .env; then
  sed -i "s|^ZEROGRAM_MASTER_KEY=.*|ZEROGRAM_MASTER_KEY=${key}|" .env
else
  echo "ZEROGRAM_MASTER_KEY=${key}" >> .env
fi
chmod 600 .env
cat <<MSG
Created $(pwd)/.env (mode 600).

NEXT:
 1. Edit .env and set ZEROGRAM_PUBLIC_ADDRESS to the IP/domain clients will use.
 2. Save a copy of ZEROGRAM_MASTER_KEY somewhere OFF this server (password manager / safe).
 3. Start:    docker compose up -d
 4. Show the link users paste into the app:   ./scripts/connect-link.sh
MSG
