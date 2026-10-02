#!/bin/bash
# Usage: wait.sh SECONDS — keeps Unity focused until play-mode time has advanced by SECONDS (Unity freezes in the background).
source "$(dirname "$0")/_env.sh"
now() { ueval 'return Time.time.ToString(System.Globalization.CultureInfo.InvariantCulture);'; }
start=""
for i in $(seq 1 20); do start=$(now); [[ "$start" =~ ^[0-9.]+$ ]] && break; ucmd editor_focus >/dev/null 2>&1; done
for i in $(seq 1 80); do
  ucmd editor_focus >/dev/null 2>&1
  t=$(now); [[ "$t" =~ ^[0-9.]+$ ]] || continue
  python3 -c "import sys; sys.exit(0 if float('$t') - float('$start') >= float('$1') else 1)" && { echo "t=$t"; exit 0; }
done
echo "timeout"
