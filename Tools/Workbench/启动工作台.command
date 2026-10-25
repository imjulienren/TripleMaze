#!/bin/zsh
SCRIPT_DIR="$(cd -- "$(dirname -- "$0")" && pwd)"
if /usr/bin/curl --silent --fail --max-time 1 http://127.0.0.1:8877/api/status >/dev/null; then
  /usr/bin/open http://127.0.0.1:8877/
  exit 0
fi
exec /usr/bin/python3 "$SCRIPT_DIR/server.py" --port 8877 --open
