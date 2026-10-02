#!/bin/bash
# Usage: cap.sh OUT.png [W H] — Game view capture including UI.
source "$(dirname "$0")/_env.sh"
unity command capture_game_view --project-path "$PROJECT" --format json --width "${2:-540}" --height "${3:-960}" --result-only 2>/dev/null |
  python3 -c "import sys,json,base64; d=json.loads(sys.stdin.read()); open('$1','wb').write(base64.b64decode(d['base64'])); print('saved $1')"
