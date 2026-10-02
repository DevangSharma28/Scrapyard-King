#!/bin/bash
# Usage: keepfocus.sh SECONDS — keeps the Unity Editor in front (play mode freezes in the background) for SECONDS.
source "$(dirname "$0")/_env.sh"
end=$(( $(date +%s) + $1 ))
while [ "$(date +%s)" -lt "$end" ]; do ucmd editor_focus >/dev/null 2>&1; sleep 2; done
