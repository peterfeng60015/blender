#!/usr/bin/env python3
"""《螢火礦坑》全部美術素材的批次渲染：建模 → render_sprites.py（--albedo＋法線）→ 檢查 → 落地。

用系統的 python3 執行（不是在 Blender 裡），每種素材開一個 Blender 背景行程：
    python3 Tools/Blender/Glowshaft/render_all.py                 # 全部
    python3 Tools/Blender/Glowshaft/render_all.py --only Miner,Floor --jobs 3

輸出到 Assets/Art/Glowshaft/Sprites/<Name>/，之後在 Unity 跑
Tools/Glowshaft/匯入 Sprite Sheet（或 batchmode 的 -executeMethod SpriteSheetImporter.ImportAll）。
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
DEST_ROOT = os.path.join(REPO, "Assets", "Art", "Glowshaft", "Sprites")
DEFAULT_BLENDER = "/Applications/Blender.app/Contents/MacOS/Blender"

# 全部素材統一的 Pixels Per Unit：1 Unity 單位＝1 Blender 公尺＝64 px。
# 每種素材的相機寬度固定為 size / PPU，所以不同素材之間比例一致。
PPU = 64
ELEVATION = 40  # 角色與道具的俯角；子彈、地板用 90（正俯視，子彈可任意旋轉、地板可平鋪）

ASSETS = [
    # name, anim-object, actions, dirs, size(px), elevation, tile
    ("Miner", "Miner_Rig", "Idle,Run", 8, 128, ELEVATION, False),
    ("SlimeGreen", "SlimeGreen_Body", "Move", 8, 128, ELEVATION, False),
    ("SlimePurple", "SlimePurple_Body", "Move", 8, 128, ELEVATION, False),
    ("SlimeOrange", "SlimeOrange_Body", "Move", 8, 160, ELEVATION, False),
    ("Bullet", "Bullet_Body", "Idle", 1, 32, 90, False),
    ("Battery", "Battery_Body", "Idle", 1, 64, ELEVATION, False),
    ("Floor", "Floor_Frames", "Idle", 1, 128, 90, True),
    ("Rock", "Rock_Frames", "Idle", 1, 128, ELEVATION, False),
    ("Crystal", "Crystal_Frames", "Idle", 1, 96, ELEVATION, False),
]


def render(asset, blender, work):
    name, anim, actions, dirs, size, elevation, tile = asset
    out = os.path.join(work, name)
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)
    cmd = [
        blender, "-b", "--factory-startup", "--python-exit-code", "1",
        "--python", os.path.join(HERE, "glowshaft_models.py"),
        "--python", RENDER_SPRITES,
        "--python", os.path.join(HERE, "glowshaft_finalize.py"),
        "--",
        "--target", name, "--anim-object", anim, "--actions", actions,
        "--dirs", str(dirs), "--size", str(size), "--elevation", str(elevation),
        "--ortho-scale", repr(size / PPU), "--albedo", "--out", out,
    ]
    env = dict(os.environ, GLOWSHAFT_PPU=str(PPU), GLOWSHAFT_TILE="1" if tile else "0",
               GLOWSHAFT_DEST=os.path.join(DEST_ROOT, name))
    log_path = os.path.join(work, f"{name}.log")
    start = time.time()
    with open(log_path, "w", encoding="utf-8") as log:
        code = subprocess.call(cmd, stdout=log, stderr=subprocess.STDOUT, env=env, cwd=REPO)
    return name, code, time.time() - start, log_path


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--only", default="", help="逗號分隔的素材名稱，留空＝全部")
    p.add_argument("--jobs", type=int, default=2, help="同時跑幾個 Blender")
    p.add_argument("--blender", default=os.environ.get("BLENDER", DEFAULT_BLENDER))
    p.add_argument("--work", default=os.path.join(tempfile.gettempdir(), "glowshaft_render"),
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
    print(f"全部完成，輸出在 {DEST_ROOT}；請到 Unity 執行 Tools/Glowshaft/匯入 Sprite Sheet")


if __name__ == "__main__":
    main()
