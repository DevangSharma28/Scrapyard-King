# Shared settings for the play-test helpers. Source it, don't run it.
PROJECT="${SYK_PROJECT:-$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")/../.." && pwd)}"
ucmd() { unity command "$@" --project-path "$PROJECT"; }
# Runs a C# block in the live Editor and prints the returned string (or the error).
ueval() {
  unity command eval --project-path "$PROJECT" --result-only --code "$1" 2>/dev/null | python3 -c "
import sys, json
raw = sys.stdin.read()
try:
    d = json.loads(raw)
    print(d['result'] if 'result' in d else d)
except Exception:
    print(raw.strip()[:400])"
}
