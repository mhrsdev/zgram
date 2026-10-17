#!/bin/sh
# Owner-only files (keys, database, queue). Keys and node id are generated on first start in /data.
set -eu
umask 077
mkdir -p /data/state
chmod 0700 /data /data/state
if [ -z "${FERRITE_PUBLIC_ADDRESS:-}" ]; then
  echo "FERRITE_PUBLIC_ADDRESS is not set: set it to the IP address or host name clients will use." >&2
  exit 64
fi
exec dotnet /app/Ferrite.dll "$@"
