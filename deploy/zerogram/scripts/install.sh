#!/usr/bin/env bash
# First-time setup: creates .env with a fresh master key, then prints the next steps.
set -euo pipefail
cd "$(dirname "$0")/.."
umask 077
if [ -f .env ] && grep -q '^ZEROGRAM_MASTER_KEY=.\+' .env; then
  echo ".env already has a master key; leaving it untouched."
  if ! grep -q '^ZEROGRAM_OWNER_PASSWORD=.\+' .env; then
    owner=$(head -c 64 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 24)
    if grep -q '^ZEROGRAM_OWNER_PASSWORD=' .env; then sed -i "s|^ZEROGRAM_OWNER_PASSWORD=.*|ZEROGRAM_OWNER_PASSWORD=${owner}|" .env
    else echo "ZEROGRAM_OWNER_PASSWORD=${owner}" >> .env; fi
    echo "Created ZEROGRAM_OWNER_PASSWORD in .env (shown below once; keep it secret):"
    echo "  ${owner}"
  fi
  exit 0
fi
[ -f .env ] || cp .env.example .env
key=$(head -c 32 /dev/urandom | base64 -w0 2>/dev/null || head -c 32 /dev/urandom | base64 | tr -d '\n')
if grep -q '^ZEROGRAM_MASTER_KEY=' .env; then
  sed -i "s|^ZEROGRAM_MASTER_KEY=.*|ZEROGRAM_MASTER_KEY=${key}|" .env
else
  echo "ZEROGRAM_MASTER_KEY=${key}" >> .env
fi
owner=$(head -c 64 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 24)
if grep -q '^ZEROGRAM_OWNER_PASSWORD=' .env; then
  sed -i "s|^ZEROGRAM_OWNER_PASSWORD=.*|ZEROGRAM_OWNER_PASSWORD=${owner}|" .env
else
  echo "ZEROGRAM_OWNER_PASSWORD=${owner}" >> .env
fi
chmod 600 .env
cat <<MSG
Created $(pwd)/.env (mode 600).

NEXT:
 1. Edit .env and set ZEROGRAM_PUBLIC_ADDRESS to the IP/domain clients will use.
 2. Save a copy of ZEROGRAM_MASTER_KEY somewhere OFF this server (password manager / safe).
 3. Start:    docker compose up -d
 4. Open the app > "Sign in as server owner" > enter this server's IP and the owner password:
        ${owner}
    (kept in .env as ZEROGRAM_OWNER_PASSWORD). The app then shows the encrypted connection message
    to hand to users and one random 32-digit token per group.
MSG
