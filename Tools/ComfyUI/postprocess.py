"""
把 generate.py 產生的原圖（out/raw）後製成遊戲用的 sprite sheet，格式和 Blender 素材相同（顏色 sheet＋平的法線 sheet＋_sprites.json），
匯入交給 Unity 的 SpriteSheetImporter。用 ComfyUI 的 venv 執行（要 Pillow、numpy）：

    ~/ComfyUI/.venv/bin/python Tools/ComfyUI/postprocess.py

做的事：
  1. 去背：有 BiRefNet 遮罩的用遮罩當 alpha；光芒、星光（黑底）用亮度當 alpha 並反推原色。
  2. 裁到內容範圍，依種類對稱化（AI 圖左右、上下不會完全對稱，九宮格的四角要一致）：
     quad＝左上四分之一鏡射到四角（面板、圓盤）；half＝左半鏡射到右半（橫向外框）；none＝不動（圖示）。
  3. 縮放排進 256 px 的格子：方形素材撐滿一格；橫向外框（2:1）放在格子中間 256×128（上下透明）。
  4. 衍生變化：未選中按鈕（壓暗轉紫）、陰影（面板 alpha 模糊）、按下的觸控底盤（橘粉漸層＋奶油圈）。
  5. 觸控圖示加深紫描邊與往下的硬陰影（亮底上也讀得到）。

格子編號、橫向外框在格子裡的位置與九宮格邊界寫在遊戲程式 UiSkin.cs、TouchControlsView.cs；改這裡的排列要一起改。
選單主視覺（assets.json 有 output 的素材：SsMenuBg、SsMenuHero、SsMenuLogoBg）輸出成單張 PNG，不切格。
"""
import json
import os

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "out", "raw")
PREVIEW = os.path.join(HERE, "out", "preview")
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SPRITES = os.path.join(REPO, "Assets", "Art", "Sidescroller", "Sprites")
CELL = 256
PPU = 64
STROKE = (0x2A / 255, 0x0F / 255, 0x2E / 255)

# 各 sheet 的格子：(素材 id 或衍生名, 對稱方式, 形狀)；形狀 square＝撐滿一格，wide＝格子中間 256×128
SKIN = [
    ("panel", "quad", "square"),
    ("button", "half", "wide"),
    ("button_off", "half", "wide"),
    ("shadow", "quad", "square"),
    ("sunburst", "none", "square"),
    ("pill", "half", "wide"),
    ("ribbon", "half", "wide"),
    ("sparkle", "none", "square"),
    ("dialogue", "half", "wide"),
    ("nameplate", "half", "wide"),
]
TOUCH = [
    ("touch_disc", "quad", "disc"),
    ("touch_disc_pressed", "quad", "disc"),
    ("stick_base", "quad", "disc"),
    ("stick_knob", "quad", "disc"),
    ("icon_jump", "none", "icon"),
    ("icon_melee", "none", "icon"),
    ("icon_shoot", "none", "icon"),
    ("icon_roll", "none", "icon"),
    ("icon_enhance", "none", "icon"),
    ("icon_switch", "none", "icon"),
    ("icon_pause", "none", "icon"),
]


# ---------- 去背與裁切 ----------
def load_rgba(asset_id):
    rgb = np.asarray(Image.open(os.path.join(RAW, f"{asset_id}.png")).convert("RGB")).astype(np.float32) / 255.0
    mask_path = os.path.join(RAW, f"{asset_id}_mask.png")
    if os.path.exists(mask_path):
        a = np.asarray(Image.open(mask_path).convert("L")).astype(np.float32) / 255.0
        # 遮罩邊緣收一點，避免帶到背景色的毛邊
        a = np.clip((a - 0.15) / 0.7, 0.0, 1.0)
    else:
        # 黑底發光素材：亮度當 alpha，再把顏色除回去（未預乘）。
        # FLUX 的「純黑底」常是接近黑的深灰（實測 sunburst 是 RGB 17,14,16）：先用四邊的亮度當黑點扣掉，
        # 否則整張背景會變成幾 % 不透明的灰方塊（通關畫面放大旋轉後很明顯）
        lum = rgb.max(axis=2)
        border = np.concatenate([lum[:16].ravel(), lum[-16:].ravel(), lum[:, :16].ravel(), lum[:, -16:].ravel()])
        black = min(float(np.percentile(border, 95)) + 0.01, 0.2)
        a = np.clip((lum - black) / (1.0 - black) * 1.15, 0.0, 1.0)
        rgb = np.clip((rgb - black) / (1.0 - black) / np.maximum(a[..., None], 1e-3), 0.0, 1.0)
    return np.dstack([rgb, a])


def crop(img, pad=4):
    ys, xs = np.where(img[..., 3] > 0.08)
    if len(xs) == 0:
        raise ValueError("整張圖都是透明的（去背失敗？）")
    y0, y1 = max(0, ys.min() - pad), min(img.shape[0], ys.max() + pad + 1)
    x0, x1 = max(0, xs.min() - pad), min(img.shape[1], xs.max() + pad + 1)
    return img[y0:y1, x0:x1]


def resize(img, w, h):
    # 預乘 alpha 再縮放，邊緣才不會混進透明像素的顏色
    pm = img.copy()
    pm[..., :3] *= pm[..., 3:4]
    out = np.stack([np.asarray(Image.fromarray(pm[..., c]).resize((w, h), Image.LANCZOS)) for c in range(4)], axis=2)
    out = np.clip(out, 0.0, 1.0)
    out[..., :3] /= np.maximum(out[..., 3:4], 1e-4)
    return np.clip(out, 0.0, 1.0)


def symmetrize(img, mode):
    h, w = img.shape[:2]
    if mode in ("half", "quad"):
        left = img[:, : w // 2]
        img = np.concatenate([left, left[:, ::-1]], axis=1)
    if mode == "quad":
        top = img[: h // 2]
        img = np.concatenate([top, top[::-1]], axis=0)
    return img


# ---------- 衍生變化 ----------
def darken_violet(img):
    """選中按鈕 → 未選中：壓暗、轉成深紫，邊框的亮部保留一點。"""
    out = img.copy()
    lum = img[..., :3] @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    violet = np.array([0.17, 0.11, 0.29], dtype=np.float32)
    rim = np.array([0.88, 0.27, 0.48], dtype=np.float32)
    k = np.clip((lum - 0.75) / 0.25, 0.0, 1.0)[..., None]
    out[..., :3] = violet * (0.6 + 0.6 * lum[..., None]) * (1 - k) + rim * k
    return out


def pressed(img):
    """按鈕底盤 → 按下：底改橘→粉直向漸層（和選中按鈕同色），金圈提亮成奶油色。"""
    out = img.copy()
    h = img.shape[0]
    t = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None, None]
    grad = np.array([0xFD, 0xB3, 0x6A], np.float32) / 255 * (1 - t) + np.array([0xE2, 0x49, 0x7C], np.float32) / 255 * t
    lum = img[..., :3] @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    ring = np.clip((lum - 0.45) / 0.3, 0.0, 1.0)[..., None]
    cream = np.array([0xFF, 0xF3, 0xC4], np.float32) / 255
    out[..., :3] = grad * (1 - ring) + cream * ring
    out[..., 3] = np.maximum(img[..., 3], np.where(img[..., 3] > 0.05, 0.92, 0.0))
    return out


def shadow_from(img):
    a = Image.fromarray((img[..., 3] * 255).astype(np.uint8)).resize((CELL - 60, CELL - 60), Image.LANCZOS)
    canvas = Image.new("L", (CELL, CELL), 0)
    canvas.paste(a, (30, 30))
    blurred = np.asarray(canvas.filter(ImageFilter.GaussianBlur(14))).astype(np.float32) / 255.0 * 0.65
    return np.dstack([np.zeros((CELL, CELL, 3), np.float32), blurred])


# ---------- 排版與輸出 ----------
def to_cell(img, shape):
    cell = np.zeros((CELL, CELL, 4), np.float32)
    if shape == "square":
        cell[:] = resize(img, CELL, CELL)
    elif shape == "wide":
        cell[64:192] = resize(img, CELL, 128)
    elif shape == "disc":
        # 觸控圓盤：撐滿格子、四周留 8 px 透明邊
        cell[8:CELL - 8, 8:CELL - 8] = resize(img, CELL - 16, CELL - 16)
    else:
        # 圖示：等比例縮到格子約 70%、置中，再加深紫描邊與往下的硬陰影（亮底上也讀得到）
        h, w = img.shape[:2]
        s = 180.0 / max(h, w)
        nw, nh = max(1, round(w * s)), max(1, round(h * s))
        y, x = (CELL - nh) // 2 - 3, (CELL - nw) // 2
        body = np.zeros((CELL, CELL, 4), np.float32)
        body[y:y + nh, x:x + nw] = resize(img, nw, nh)
        cell = outline_and_shadow(body)
    return cell


def outline_and_shadow(body, stroke=6, drop=6):
    """在 body 外面加 stroke px 的深紫描邊，再往下 drop px 疊一層深色硬陰影。"""
    alpha = Image.fromarray((body[..., 3] * 255).astype(np.uint8))
    grown = np.asarray(alpha.filter(ImageFilter.MaxFilter(stroke * 2 + 1))).astype(np.float32) / 255.0
    ink = np.array(STROKE, np.float32)
    out = np.zeros_like(body)
    shadow = np.zeros_like(grown)
    shadow[drop:] = grown[:-drop]
    out[..., :3] = ink * 0.6
    out[..., 3] = shadow * 0.55
    out = over(out, np.dstack([np.broadcast_to(ink, grown.shape + (3,)), grown]))
    return over(out, body)


def over(dst, src):
    a = src[..., 3:4] + dst[..., 3:4] * (1 - src[..., 3:4])
    rgb = (src[..., :3] * src[..., 3:4] + dst[..., :3] * dst[..., 3:4] * (1 - src[..., 3:4])) / np.maximum(a, 1e-4)
    return np.dstack([rgb, a])


def build(names):
    cache = {}

    def base(asset_id, mode):
        if asset_id not in cache:
            cache[asset_id] = symmetrize(crop(load_rgba(asset_id)), mode)
        return cache[asset_id]

    cells = []
    for name, mode, shape in names:
        if name == "button_off":
            img = darken_violet(base("button", mode))
        elif name == "shadow":
            cells.append(shadow_from(to_cell(base("panel", "quad"), "square")))
            continue
        elif name == "touch_disc_pressed":
            img = pressed(base("touch_disc", mode))
        else:
            img = base(name, mode)
        cells.append(to_cell(img, shape))
    return np.concatenate(cells, axis=1)


def write_sheet(name, sheet):
    out_dir = os.path.join(SPRITES, name)
    os.makedirs(out_dir, exist_ok=True)
    rgba = (np.clip(sheet, 0.0, 1.0) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(rgba, "RGBA").save(os.path.join(out_dir, f"{name}_Idle.png"))
    flat = np.zeros_like(rgba)
    flat[..., 0], flat[..., 1], flat[..., 2], flat[..., 3] = 128, 128, 255, 255
    Image.fromarray(flat, "RGBA").save(os.path.join(out_dir, f"{name}_Idle_n.png"))
    columns = sheet.shape[1] // CELL
    manifest = {
        "name": name, "cellSize": CELL, "directions": ["a000"], "fps": 12.0, "orthoScale": 1.0,
        "suggestedPixelsPerUnit": float(PPU), "pivot": [0.5, 0.5], "elevation": 0.0, "albedo": False,
        "sheets": [{"action": "Idle", "columns": columns, "rows": 1, "color": f"{name}_Idle.png", "normal": f"{name}_Idle_n.png"}],
        "pixelsPerUnit": PPU, "tile": False,
    }
    with open(os.path.join(out_dir, f"{name}_sprites.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")
    # 預覽：疊在深色底上看
    os.makedirs(PREVIEW, exist_ok=True)
    bg = Image.new("RGBA", (rgba.shape[1], rgba.shape[0]), (40, 30, 60, 255))
    bg.alpha_composite(Image.fromarray(rgba, "RGBA"))
    bg.save(os.path.join(PREVIEW, f"{name}.png"))
    print(f"{name}：{columns} 格 → {out_dir}")


def write_singles():
    """assets.json 有 output 的素材輸出成單張 PNG（不切格、沒有 json，匯入時是單張 sprite；location＝<名稱>_Idle）。"""
    with open(os.path.join(HERE, "assets.json"), encoding="utf-8") as f:
        spec = json.load(f)
    for a in spec["assets"]:
        out = a.get("output")
        if not out or not os.path.exists(os.path.join(RAW, f"{a['id']}.png")):
            continue
        img = load_rgba(a["id"]) if a.get("mask", True) else np.dstack([
            np.asarray(Image.open(os.path.join(RAW, f"{a['id']}.png")).convert("RGB")).astype(np.float32) / 255.0,
            np.ones((a["height"], a["width"]), np.float32)])
        if "fit_height" in out:
            # 角色：裁到內容、等比例縮到指定高度，腳底貼齊下緣
            img = crop(img, pad=0)
            h, w = img.shape[:2]
            img = resize(img, max(1, round(w * out["fit_height"] / h)), out["fit_height"])
        else:
            if a.get("mask", True):
                img = crop(img)
            img = resize(img, out["width"], out["height"])
        name = out["name"]
        out_dir = os.path.join(SPRITES, name)
        os.makedirs(out_dir, exist_ok=True)
        rgba = (np.clip(img, 0.0, 1.0) * 255 + 0.5).astype(np.uint8)
        Image.fromarray(rgba, "RGBA").save(os.path.join(out_dir, f"{name}_Idle.png"))
        print(f"{name}：{rgba.shape[1]}×{rgba.shape[0]} → {out_dir}")


def main():
    # 原圖還沒到齊的 sheet 先跳過（生圖是分批的）
    for name, cells in (("SsUiSkin", SKIN), ("SsTouchUi", TOUCH)):
        need = {"button" if n == "button_off" else "panel" if n == "shadow" else "touch_disc" if n == "touch_disc_pressed" else n
                for n, _, _ in cells}
        missing = sorted(n for n in need if not os.path.exists(os.path.join(RAW, f"{n}.png")))
        if missing:
            print(f"{name}：還缺 {', '.join(missing)}，略過")
            continue
        write_sheet(name, build(cells))
    write_singles()


if __name__ == "__main__":
    main()
