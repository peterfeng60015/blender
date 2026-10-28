#!/usr/bin/env python3
"""《熱浪急件》全部美術素材的批次渲染：建模 → render_sprites.py（NPR 色階＋法線）→ 檢查 → 落地。

用系統的 python3 執行（不是在 Blender 裡），每種素材開一個 Blender 背景行程：
    python3 Tools/Blender/Sidescroller/render_all.py                       # 全部
    python3 Tools/Blender/Sidescroller/render_all.py --only SsHeroDay,SsRat --jobs 4

輸出到 Assets/Art/Sidescroller/Sprites/<Name>/，之後在 Unity 跑 Tools/Sidescroller/匯入 Sprite Sheet
（兩款遊戲的素材一起匯入；batchmode 用 -executeMethod SpriteSheetImporter.ImportAndValidate）。
單幀 PNG 與 Blender log 留在 --work（預設系統暫存資料夾），不進專案。
"""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
RENDER_SPRITES = os.path.join(HERE, "..", "render_sprites.py")
DEST_ROOT = os.path.join(REPO, "Assets", "Art", "Sidescroller", "Sprites")
DEFAULT_BLENDER = "/Applications/Blender.app/Contents/MacOS/Blender"

PPU = 64       # 1 Unity 單位＝1 Blender 公尺＝64 px；相機寬度固定為 size / PPU，素材之間比例一致
CENTER = 0.5

HERO = "Idle:6,Run:8,Jump:6,Melee:5,Shoot:4,Hurt:3"

# name, anim 物件後綴（Rig＝骨架動作、Frames＝多張變化）, 動作:幀數（interfaces.md §6.4、design.md §8.3～8.5）,
# 格子 px, pivot y（x 一律 0.5）, tile, 接縫檢查, 必須不透明的格子
ASSETS = [
    ("SsHeroDay", "Rig", HERO, 128, 0.1, False, "", ""),
    ("SsHeroExpress", "Rig", HERO, 128, 0.1, False, "", ""),
    ("SsMentor", "Rig", "Idle:6,Talk:6", 128, 0.1, False, "", ""),
    ("SsRat", "Rig", "Idle:4,Walk:8,Attack:6,Hurt:3", 128, 0.1, False, "", ""),
    ("SsPigeon", "Rig", "Fly:6,Attack:4,Hurt:3", 128, 0.1, False, "", ""),
    ("SsRhino", "Rig", "Idle:4,Walk:8,Attack:6,Hurt:3", 160, 0.08, False, "", ""),
    ("SsBoss", "Rig", "Idle:6,Charge:8,Spray:6,Stomp:8,Hurt:3", 320, 0.06, False, "", ""),
    ("SsParcel", "Rig", "Spin:4", 32, CENTER, False, "", ""),
    ("SsCoin", "Rig", "Spin:4", 32, CENTER, False, "", ""),
    ("SsBomb", "Rig", "Fall:2", 32, CENTER, False, "", ""),
    ("SsShock", "Rig", "Move:4", 64, CENTER, False, "", ""),
    ("SsFx", "Rig", "Hit:4,Poof:6,Dust:4,Clank:3", 128, CENTER, False, "", ""),
    ("SsTiles", "Frames", "Idle:7", 64, CENTER, True, "0:x,1:xy,2:x,4:x,5:x,6:y", "0,1,6"),
    ("SsProps", "Frames", "Idle:6", 256, 0.04, False, "", ""),
    ("SsLandmark", "Frames", "Idle:3", 512, 0.03, False, "", ""),
    ("SsBgFar", "Frames", "Idle:1", 1024, CENTER, True, "0:x", "all"),
    ("SsBgMid", "Frames", "Idle:2", 1024, CENTER, True, "0:x,1:x", ""),
    ("SsUi", "Frames", "Idle:8", 64, CENTER, False, "", ""),
    ("SsPortraitHero", "Frames", "Face:4", 256, CENTER, False, "", ""),
    ("SsPortraitBoss", "Frames", "Face:4", 256, CENTER, False, "", ""),
    ("SsPortraitMentor", "Frames", "Face:4", 256, CENTER, False, "", ""),
    ("SsPortraitRat", "Frames", "Face:4", 256, CENTER, False, "", ""),
]


def render(asset, blender, work):
    name, anim, frames, size, pivot_y, tile, seams, opaque = asset
    out = os.path.join(work, name)
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)
    actions = ",".join(item.split(":")[0] for item in frames.split(","))
    cmd = [
        blender, "-b", "--factory-startup", "--python-exit-code", "1",
        "--python", os.path.join(HERE, "ss_models.py"),
        "--python", RENDER_SPRITES,
        "--python", os.path.join(HERE, "ss_finalize.py"),
        "--",
        "--target", name, "--anim-object", f"{name}_{anim}", "--actions", actions,
        "--dirs", "1", "--size", str(size), "--elevation", "0",
        "--ortho-scale", repr(size / PPU), "--out", out,
    ]
    env = dict(os.environ, SS_PPU=str(PPU), SS_TILE="1" if tile else "0", SS_SEAMS=seams, SS_OPAQUE=opaque,
               SS_FRAMES=frames, SS_PIVOT=f"0.5,{pivot_y}", SS_DEST=os.path.join(DEST_ROOT, name))
    log_path = os.path.join(work, f"{name}.log")
    start = time.time()
    with open(log_path, "w", encoding="utf-8") as log:
        code = subprocess.call(cmd, stdout=log, stderr=subprocess.STDOUT, env=env, cwd=REPO)
    return name, code, time.time() - start, log_path


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--only", default="", help="逗號分隔的素材名稱，留空＝全部")
    p.add_argument("--jobs", type=int, default=4, help="同時跑幾個 Blender")
    p.add_argument("--blender", default=os.environ.get("BLENDER", DEFAULT_BLENDER))
    p.add_argument("--work", default=os.path.join(tempfile.gettempdir(), "sidescroller_render"),
                   help="單幀 PNG 與 log 的暫存資料夾（同時跑兩個 render_all 時請各給一個，否則會互相清掉）")
    args = p.parse_args()

    names = [n.strip() for n in args.only.split(",") if n.strip()]
    known = {a[0] for a in ASSETS}
    unknown = [n for n in names if n not in known]
    if unknown:
        p.error(f"不認得的素材：{', '.join(unknown)}（可用：{', '.join(sorted(known))}）")
    todo = [a for a in ASSETS if not names or a[0] in names]
    os.makedirs(args.work, exist_ok=True)

    failed = []
    with ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
        for name, code, secs, log in pool.map(lambda a: render(a, args.blender, args.work), todo):
            if code == 0:
                print(f"  完成 {name}（{secs:.0f} 秒）")
            else:
                failed.append(name)
                print(f"  失敗 {name}（exit {code}），log：{log}")
                with open(log, encoding="utf-8", errors="replace") as fp:
                    tail = fp.readlines()[-15:]
                print("    " + "    ".join(tail))
    if failed:
        print(f"失敗：{', '.join(failed)}")
        sys.exit(1)
    print(f"全部完成，輸出在 {DEST_ROOT}；請到 Unity 執行 Tools/Sidescroller/匯入 Sprite Sheet")


if __name__ == "__main__":
    main()
