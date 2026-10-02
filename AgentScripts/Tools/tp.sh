#!/bin/bash
# Usage: tp.sh X Z [YAW] — teleports the player (play mode). Don't use while someone else is playing.
source "$(dirname "$0")/_env.sh"
ueval "ScrapYardKing.Core.Services.TryGet(out ScrapYardKing.Player.PlayerCharacter pc); pc.Controller.Teleport(new Vector3(${1}f,0,${2}f), Quaternion.Euler(0,${3:-0},0)); return \"ok\";" >/dev/null
