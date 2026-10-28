"""render_sprites.py 跑完之後的檢查與落地（《熱浪急件》）：檢查幀數、PPU、pivot、裁切或 tile 接縫，
補 json 欄位，原子地複製到 Assets/Art/Sidescroller/Sprites/<Name>/。

由 render_all.py 串在同一個 Blender 行程的最後一支執行，參數同樣放在 `--` 之後（只讀
--target、--name、--out），其餘設定走環境變數：
    SS_PPU     全素材統一的 Pixels Per Unit（整數，64）
    SS_TILE    "1"＝可平鋪（匯入用 FullRect mesh；不檢查邊框裁切）
    SS_SEAMS   tile 的接縫檢查，例如 "0:x,1:xy,6:y"（第幾格：x＝左右可接、y＝上下可接；
               比較的是格子最外兩欄／兩列，左右對稱但內容接不起來的圖抓不到）
    SS_OPAQUE  必須完全不透明的格子，例如 "0,1"；"all"＝全部
    SS_FRAMES  每個動作的幀數，例如 "Idle:6,Run:8"（interfaces.md §6.4、design.md §8.3）
    SS_PIVOT   預期的 pivot，例如 "0.5,0.1"
    SS_DEST    落地資料夾

檢查失敗會丟 SystemExit，Blender 以非 0 結束，render_all.py 會回報失敗，不會落地。
"""

import argparse
import json
import os
import re
import shutil
import sys

import bpy
import numpy as np

EDGE_ALPHA_MAX = 2.0 / 255.0   # 非 tile：每格最外圈像素的 alpha 上限（超過＝被裁切）
SEAM_JUMP = 0.1                # tile：接縫兩側同一列（或同一欄）像素差超過這個值，算一處「斷開」
SEAM_BAD_MIN = 0.01            # 斷開的列數比例超過這個值，而且超過內部同樣跳動比例的 2 倍，就判定不是無縫
FLAT_NORMAL = (0.5, 0.5, 1.0)  # 法線 sheet 的透明像素填這個，bilinear 取樣輪廓才不會混到 (0,0,0)
NAME_RE = re.compile(r"^Ss[A-Z][A-Za-z0-9]*$")
ACTION_RE = re.compile(r"^[A-Z][A-Za-z0-9]*$")


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(prog="ss_finalize.py")
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


def parse_map(text):
    out = {}
    for item in filter(None, (s.strip() for s in text.split(","))):
        k, _, v = item.partition(":")
        out[k.strip()] = v.strip()
    return out


def seam_errors(cell_px, axes):
    """cell_px 是一格 (h, w, 4)；用預乘 alpha 的 RGBA 逐列比較，透明邊緣也算進接縫。

    不能只比整欄平均：1024px 的背景就算有一段大樓被切斷，整欄平均也很小。改看「接縫兩側差異超過
    SEAM_JUMP 的列數比例」，並和格子內部相鄰像素的同樣比例比較（窗戶、招牌本來就有硬邊）。
    """
    px = np.concatenate([cell_px[..., :3] * cell_px[..., 3:4], cell_px[..., 3:4]], axis=2)
    errs = []
    for axis in axes:
        if axis == "x":
            seam = np.abs(px[:, 0] - px[:, -1]).max(axis=1)            # 每一列一個值
            inner = np.abs(np.diff(px, axis=1)).max(axis=2)
        else:
            seam = np.abs(px[0] - px[-1]).max(axis=1)                  # 每一欄一個值
            inner = np.abs(np.diff(px, axis=0)).max(axis=2)
        bad = float((seam > SEAM_JUMP).mean())
        inner_bad = float((inner > SEAM_JUMP).mean())
        if bad > SEAM_BAD_MIN and bad > 2.0 * inner_bad:
            errs.append(f"{'左右' if axis == 'x' else '上下'}接縫有 {bad:.1%} 斷開（內部只有 {inner_bad:.1%}）")
    return errs


def check_sheet(path, rows, cols, cell, tile, seams, opaque):
    px = load_rgba(path)
    if px.shape[:2] != (rows * cell, cols * cell):
        return [f"{os.path.basename(path)} 尺寸 {px.shape[1]}x{px.shape[0]} 與 json 不符"]
    errors = []
    for r in range(rows):
        for c in range(cols):
            cpx = px[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell]
            a = cpx[..., 3]
            idx = r * cols + c
            if a.max() <= 0.0:
                errors.append(f"第 {idx} 格是空的")
                continue
            if (opaque == "all" or str(idx) in opaque) and a.min() < 0.999:
                errors.append(f"第 {idx} 格有透明像素（規格要求不透明）")
            if tile:
                if str(idx) in seams:
                    errors += [f"第 {idx} 格不是無縫（{e}）" for e in seam_errors(cpx, seams[str(idx)])]
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
    px = load_rgba(src).copy()
    px[px[:, :, 3] <= 0.0, :3] = FLAT_NORMAL
    save_rgba(px, dst)


def main():
    args = parse_args()
    name = args.name or args.target
    out_dir = os.path.abspath(args.out)
    dest = os.environ["SS_DEST"]
    ppu = int(os.environ["SS_PPU"])
    tile = os.environ.get("SS_TILE") == "1"
    seams = parse_map(os.environ.get("SS_SEAMS", ""))
    opaque_env = os.environ.get("SS_OPAQUE", "")
    opaque = "all" if opaque_env == "all" else {s.strip() for s in opaque_env.split(",") if s.strip()}
    frames = {k: int(v) for k, v in parse_map(os.environ.get("SS_FRAMES", "")).items()}
    pivot = [float(v) for v in os.environ["SS_PIVOT"].split(",")]
    total_cells = sum(frames.values())

    with open(os.path.join(out_dir, f"{name}_sprites.json"), encoding="utf-8") as fp:
        manifest = json.load(fp)

    errors = []
    if not NAME_RE.match(name):
        errors.append(f"素材名 {name} 必須 Ss 開頭、PascalCase、不含底線（interfaces.md §6.1）")
    actual = manifest["cellSize"] / manifest["orthoScale"]
    if abs(actual - ppu) / ppu > 1e-3:
        errors.append(f"cellSize/orthoScale＝{actual:.4f}，不等於 PPU {ppu}")
    if abs(manifest["elevation"]) > 1e-6 or manifest["directions"] != ["a000"]:
        errors.append(f"必須是單方向純側視（--dirs 1 --elevation 0），現在是 {manifest['directions']}、"
                      f"{manifest['elevation']}°")
    if any(abs(a - b) > 1e-3 for a, b in zip(manifest["pivot"], pivot)):
        errors.append(f"pivot {manifest['pivot']} 不是預期的 {pivot}（取景框 frame_box 沒有包住全部部件？）")
    if tile and len(manifest["sheets"]) != 1:
        errors.append("tile 素材只能有一個動作（SS_SEAMS／SS_OPAQUE 的格子編號以它為準）")
    for label, ids in (("SS_SEAMS", seams.keys()), ("SS_OPAQUE", opaque if opaque != "all" else ())):
        bad_ids = [i for i in ids if not i.isdigit() or int(i) >= total_cells]
        if bad_ids:
            errors.append(f"{label} 的格子編號 {bad_ids} 超出範圍（共 {total_cells} 格）")
    got = {s["action"]: s["columns"] for s in manifest["sheets"]}
    if got != frames:
        errors.append(f"動作與幀數 {got} 不符合規格 {frames}")
    outputs = []
    for sheet in manifest["sheets"]:
        if not ACTION_RE.match(sheet["action"]):
            errors.append(f"動作名 {sheet['action']} 必須 PascalCase、不含底線")
        if "normal" not in sheet:
            errors.append(f"{sheet['action']} 沒有法線 sheet")
            continue
        outputs += [sheet["color"], sheet["normal"]]
        errors += check_sheet(os.path.join(out_dir, sheet["color"]), sheet["rows"], sheet["columns"],
                              manifest["cellSize"], tile, seams, opaque)
        normal = load_rgba(os.path.join(out_dir, sheet["normal"]))
        if normal.shape[:2] != (sheet["rows"] * manifest["cellSize"], sheet["columns"] * manifest["cellSize"]):
            errors.append(f"{sheet['normal']} 尺寸與 json 不符")
        elif normal[:, :, 3].max() <= 0.0:
            errors.append(f"{sheet['normal']} 是空的")
    if errors:
        raise SystemExit(f"[ss_finalize] {name} 檢查失敗：\n  " + "\n  ".join(errors))

    # 只新增欄位，不改 render_sprites.py 原有欄位（interfaces.md §6.2）
    manifest["pixelsPerUnit"] = ppu
    manifest["tile"] = tile
    json_name = f"{name}_sprites.json"

    # 先全部寫到 dest/.staging（Unity 不匯入點開頭的資料夾，AssetPackage 也會略過），都成功了才一次搬進 dest
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

    # 動作改名或刪掉時，清掉這個素材自己的舊 sheet（連同 .meta）；只動 <Name>_*.png 與 <Name>_sprites.json
    keep = set(outputs) | {json_name}
    for f in os.listdir(dest):
        base = f[:-5] if f.endswith(".meta") else f
        ours = base.startswith(f"{name}_") and (base.endswith(".png") or base == json_name)
        if ours and base not in keep:
            os.remove(os.path.join(dest, f))
            print(f"[ss_finalize] 移除過期檔案 {f}")
    print(f"[ss_finalize] 已落地 {name} → {dest}（{len(outputs)} 張 sheet，PPU {ppu}）")


if __name__ == "__main__":
    main()
