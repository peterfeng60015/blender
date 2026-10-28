"""render_sprites.py 跑完之後的檢查與落地：檢查 PPU、裁切或 tile 接縫，補 json 欄位，複製到 Unity 專案。

由 render_all.py 串在同一個 Blender 行程的最後一支執行，參數同樣放在 `--` 之後（只讀
--target、--name、--out），其餘設定走環境變數：
    GLOWSHAFT_PPU   全素材統一的 Pixels Per Unit（整數）
    GLOWSHAFT_TILE  "1"＝可平鋪的 tile（檢查俯角 90、全不透明、接縫色差，不檢查邊框）
    GLOWSHAFT_DEST  落地資料夾（Assets/Art/Glowshaft/Sprites/<Name>）

檢查失敗會丟 SystemExit，Blender 以非 0 結束，render_all.py 會回報失敗，不會落地。
"""

import argparse
import json
import os
import shutil
import sys

import bpy
import numpy as np

EDGE_ALPHA_MAX = 2.0 / 255.0   # 非 tile 素材：每格最外圈像素的 alpha 上限（超過＝被裁切）
SEAM_RATIO_MAX = 1.5           # tile：接縫兩側的平均色差，最多是內部相鄰像素平均色差的幾倍
FLAT_NORMAL = (0.5, 0.5, 1.0)  # 朝鏡頭的法線；法線 sheet 的透明像素填這個，bilinear 取樣輪廓才不會混到 (0,0,0)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(prog="glowshaft_finalize.py")
    p.add_argument("--target", required=True)
    p.add_argument("--name", default="")
    p.add_argument("--out", required=True)
    args, _ = p.parse_known_args(argv)
    return args


def load_rgba(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    bpy.data.images.remove(img)
    return buf.reshape(h, w, 4)[::-1]  # Blender 由下往上，翻成由上往下（第 0 列在最上面）


def check_sheet(path, rows, cols, cell, tile):
    px = load_rgba(path)
    if px.shape[:2] != (rows * cell, cols * cell):
        return [f"{os.path.basename(path)} 尺寸 {px.shape[1]}x{px.shape[0]} 與 json 不符"]
    errors = []
    alpha = px[:, :, 3]
    for r in range(rows):
        for c in range(cols):
            a = alpha[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell]
            idx = r * cols + c
            if a.max() <= 0.0:
                errors.append(f"第 {idx} 格是空的")
                continue
            if tile:
                if a.min() < 0.999:
                    errors.append(f"第 {idx} 格有透明像素，tile 會出現縫")
                rgb = px[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell, :3]
                # 平鋪時左欄接右欄、上列接下列；接縫的色差要跟內部相鄰像素同一量級
                inner_x = np.abs(np.diff(rgb, axis=1)).mean()
                inner_y = np.abs(np.diff(rgb, axis=0)).mean()
                seam_x = np.abs(rgb[:, 0] - rgb[:, -1]).mean()
                seam_y = np.abs(rgb[0] - rgb[-1]).mean()
                if seam_x > SEAM_RATIO_MAX * inner_x or seam_y > SEAM_RATIO_MAX * inner_y:
                    errors.append(f"第 {idx} 格不是無縫（左右接縫 {seam_x:.3f}／內部 {inner_x:.3f}，"
                                  f"上下接縫 {seam_y:.3f}／內部 {inner_y:.3f}）")
                continue
            ring = np.concatenate([a[0], a[-1], a[:, 0], a[:, -1]])
            if ring.max() > EDGE_ALPHA_MAX:
                errors.append(f"第 {idx} 格碰到邊框（alpha {ring.max():.3f}），可能被裁切")
    return [f"{os.path.basename(path)}：{e}" for e in errors]


def save_rgba(px, path):
    """px 是由上往下的 (h, w, 4)；照 render_sprites.build_sheet 的方式原值寫回 8-bit PNG。"""
    h, w = px.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(px[::-1]).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def fill_normal_background(src, dst):
    """法線 sheet 的透明像素（背景、描邊外殼）改成朝鏡頭的法線，alpha 不變。"""
    px = load_rgba(src).copy()
    mask = px[:, :, 3] <= 0.0
    px[mask, :3] = FLAT_NORMAL
    save_rgba(px, dst)


def main():
    args = parse_args()
    name = args.name or args.target
    out_dir = os.path.abspath(args.out)
    dest = os.environ["GLOWSHAFT_DEST"]
    ppu = int(os.environ["GLOWSHAFT_PPU"])
    tile = os.environ.get("GLOWSHAFT_TILE") == "1"

    manifest_path = os.path.join(out_dir, f"{name}_sprites.json")
    with open(manifest_path, encoding="utf-8") as fp:
        manifest = json.load(fp)

    errors = []
    actual = manifest["cellSize"] / manifest["orthoScale"]  # orthoScale 在 json 裡只留 4 位小數，用相對誤差比
    if abs(actual - ppu) / ppu > 1e-3:
        errors.append(f"cellSize/orthoScale＝{actual:.4f}，不等於 PPU {ppu}（--ortho-scale 要是 size/PPU）")
    if tile and abs(manifest["elevation"] - 90.0) > 1e-6:
        errors.append(f"tile 的俯角是 {manifest['elevation']}，必須是 90（正俯視）才能無縫平鋪")
    outputs = []
    for sheet in manifest["sheets"]:
        if "normal" not in sheet:
            errors.append(f"{sheet['action']} 沒有法線 sheet")
            continue
        outputs += [sheet["color"], sheet["normal"]]
        errors += check_sheet(os.path.join(out_dir, sheet["color"]), sheet["rows"], sheet["columns"],
                              manifest["cellSize"], tile)
        normal = load_rgba(os.path.join(out_dir, sheet["normal"]))
        if normal.shape[:2] != (sheet["rows"] * manifest["cellSize"], sheet["columns"] * manifest["cellSize"]):
            errors.append(f"{sheet['normal']} 尺寸與 json 不符")
        elif normal[:, :, 3].max() <= 0.0:
            errors.append(f"{sheet['normal']} 是空的")
    if errors:
        raise SystemExit("[glowshaft_finalize] 檢查失敗：\n  " + "\n  ".join(errors))

    # 只新增欄位，不改 render_sprites.py 原有欄位（介面約定）
    manifest["pixelsPerUnit"] = ppu
    manifest["tile"] = tile
    json_name = f"{name}_sprites.json"

    # 先全部寫到 dest/.staging（Unity 不匯入點開頭的資料夾），都成功了才一次搬進 dest，
    # 中途失敗不會留下半套素材
    os.makedirs(dest, exist_ok=True)
    staging = os.path.join(dest, ".staging")
    shutil.rmtree(staging, ignore_errors=True)
    os.makedirs(staging)
    for sheet in manifest["sheets"]:
        shutil.copyfile(os.path.join(out_dir, sheet["color"]), os.path.join(staging, sheet["color"]))
        fill_normal_background(os.path.join(out_dir, sheet["normal"]), os.path.join(staging, sheet["normal"]))
    with open(os.path.join(staging, json_name), "w", encoding="utf-8") as fp:
        json.dump(manifest, fp, ensure_ascii=False, indent=2)
        fp.write("\n")
    for f in os.listdir(staging):
        os.replace(os.path.join(staging, f), os.path.join(dest, f))
    os.rmdir(staging)

    # 動作改名或刪掉時，清掉這個素材自己的舊 sheet（連同 .meta），避免 AssetPackage 收到過期素材；
    # 只動 <Name>_*.png 與 <Name>_sprites.json，資料夾裡其他檔案不碰
    keep = set(outputs) | {json_name}
    for f in os.listdir(dest):
        base = f[:-5] if f.endswith(".meta") else f
        ours = base.startswith(f"{name}_") and (base.endswith(".png") or base == json_name)
        if ours and base not in keep:
            os.remove(os.path.join(dest, f))
            print(f"[glowshaft_finalize] 移除過期檔案 {f}")
    print(f"[glowshaft_finalize] 已落地 {name} → {dest}（{len(outputs)} 張 sheet，PPU {ppu}）")


if __name__ == "__main__":
    main()
