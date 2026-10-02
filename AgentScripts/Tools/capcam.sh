#!/bin/bash
# Usage: capcam.sh OUT.png X Z [DISTANCE] — world-only render from a game-like camera aimed at (X, 0, Z); never moves the player.
source "$(dirname "$0")/_env.sh"
D=${4:-24}
r=$(ueval "var main=Camera.main; var go=new GameObject(\"TmpCapCam\"); var cam=go.AddComponent<Camera>(); cam.CopyFrom(main);
var rot=Quaternion.Euler(52f,0f,0f); cam.transform.SetPositionAndRotation(new Vector3(${2}f,0.5f,${3}f+1f)-rot*Vector3.forward*${D}f, rot);
var rt=new RenderTexture(540,960,24); rt.antiAliasing=1; cam.targetTexture=rt; cam.Render(); RenderTexture.active=rt;
var tex=new Texture2D(540,960,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,540,960),0,0); tex.Apply();
System.IO.File.WriteAllBytes(\"$1\", tex.EncodeToPNG()); RenderTexture.active=null; cam.targetTexture=null; UnityEngine.Object.DestroyImmediate(go); rt.Release(); return \"ok\";")
[ "$r" = "ok" ] && echo "saved $1" || echo "failed: $r"
