#!/usr/bin/env bash
# Runs closed_network.py in a network namespace that contains only the loopback interface.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
rm -rf "${ZG_WORK:-/tmp}/zerogram-e2e/closed-net"*
exec unshare -n bash -c "python3 '$here/lo_up.py' && exec ${PYTHON:-python3} -u '$here/closed_network.py'"
