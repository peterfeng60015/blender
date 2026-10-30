"""Meshy 模型的貼圖轉換：把 Meshy 匯出的 PBR 貼圖轉成 AhJiaToon 用的 _BaseMap／_MaskMap。

hero3d.py --source meshy 從 glb 解出貼圖後呼叫 build_meshy_textures()；純貼圖空間運算，不依賴網格。
也可以單獨跑（輸出分類可視化，檢查皮膚／殼／布／眼睛分得對不對）：
  blender -b --python Tools/Blender/Hero3D/meshy_texture.py -- --base <Base Color> [--metal-rough <路徑>] --out <資料夾> [--size 1024]
輸出（皆 size²）：
  AhJia3D_BaseMap.png      固有色（sRGB），Lanczos 縮圖；Meshy 畫進去的 AO／筆觸保留，和 toon 色階的疊加由材質的 _RampStrength 調
  AhJia3D_MaskMap.png      linear：R＝高光遮罩、G＝陰影偏移（0.5 不偏移）、B＝材質類別（0 皮膚、0.33 殼、0.66 布、1 金屬／眼睛）
  AhJia3D_MaskClasses.png  （只有單獨跑時）分類可視化：類別塗色，和 BaseMap 同一套 UV，可以直接貼回模型檢查
"""

import os
import sys

import bpy
import numpy as np

#region 參數

CLASS_VALUE = (0.0, 0.33, 0.66, 1.0)      # B 通道：皮膚、殼、布、金屬／眼睛
CLASS_SHADOW = (0.6, 0.5, 0.5, 0.8)      # G 通道（陰影偏移）：皮膚與眼睛偏亮，臉不容易碎成一塊塊陰影、眼白不會變灰
CLASS_PREVIEW = ((0.95, 0.6, 0.65), (0.75, 0.55, 0.2), (0.2, 0.45, 0.95), (1.0, 1.0, 0.2))

#endregion

#region 讀寫


def _load(path):
    """讀成 (H, W, 3) float，不做色彩轉換（byte 圖的 pixels 就是 0～1 的編碼值）。row 0 是圖的底部，存回去方向不變。"""
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[..., :3].astype(np.float64)


def _save(name, rgb, path, colorspace):
    h, w = rgb.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=False)
    img.colorspace_settings.name = colorspace
    px = np.ones((h, w, 4), dtype=np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(px.ravel())     # byte 圖的 pixels 直接對應 0～255，不做色彩轉換
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)

#endregion

#region 縮圖


def _lanczos_matrix(n_in, n_out, a=3):
    """一維 Lanczos 重取樣矩陣 (n_out, n_in)；縮小時核心依比例放寬（等同先低通），邊界重新正規化。"""
    scale = n_in / n_out
    support = a * max(scale, 1.0)
    centers = (np.arange(n_out) + 0.5) * scale - 0.5
    j = np.arange(n_in)
    x = (j[None, :] - centers[:, None]) / max(scale, 1.0)
    wgt = np.sinc(x) * np.sinc(x / a)
    wgt[np.abs(j[None, :] - centers[:, None]) >= support] = 0
    return wgt / wgt.sum(axis=1, keepdims=True)


def _resize(img, size):
    """高品質縮圖（可分離 Lanczos3，在 sRGB 編碼值上做：細黑線不會被平均得太淡）。"""
    h, w = img.shape[:2]
    if (h, w) == (size, size):
        return img.copy()
    wy, wx = _lanczos_matrix(h, size), _lanczos_matrix(w, size)
    out = np.stack([wy @ img[..., c] @ wx.T for c in range(img.shape[2])], -1)
    return np.clip(out, 0, 1)

#endregion

#region 分類


def _hsv(rgb):
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    dd = np.where(d > 1e-6, d, 1)
    h = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60
    h = np.where(d > 1e-6, h, 0)
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return h, s, mx


def _box(a, r):
    """(H, W[, C]) 的 (2r+1)² 方框平均，邊界夾住。"""
    p = np.pad(a, [(r + 1, r), (r + 1, r)] + [(0, 0)] * (a.ndim - 2), mode="edge")
    c = p.cumsum(0).cumsum(1)
    k = 2 * r + 1
    s = c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]
    return s / (k * k)


def classify(base, metal_rough=None):
    """依固有色（sRGB 0～1）分類，回傳 (H, W) int：0 皮膚、1 殼、2 布、3 金屬／眼睛。
    metal_rough（glTF：G＝粗糙度、B＝金屬度）只拿來找眼睛：Meshy 只有眼睛（含瞳孔）是光滑的；
    金屬度只標在看不到的內側面，不採用。沒有 metal_rough 時退回「近白色＝眼白」。"""
    h, s, v = _hsv(base)
    lum = base @ np.array([0.299, 0.587, 0.114])
    texture = np.sqrt(np.maximum(_box(lum ** 2, 3) - _box(lum, 3) ** 2, 0))
    cls = np.ones(h.shape, dtype=np.int64)                        # 預設殼（土黃、褐色、殼帶、鞋）
    skin = (v > 0.62) & (s < 0.36) & ((h < 22) | (h > 340)) & (texture < 0.035)
    cls[skin] = 0
    red = (s > 0.5) & ((h < 14) | (h > 340)) & (v > 0.35)
    yellow = (s > 0.5) & (h >= 30) & (h <= 62) & (v > 0.5)
    blue = (s > 0.3) & (h >= 165) & (h <= 260)
    cls[red | yellow | blue] = 2
    if metal_rough is not None:
        eye = metal_rough[..., 1] < 0.45
    else:
        eye = (v > 0.82) & (s < 0.16)                            # 包包的白滾邊也會被當成眼白
    cls[eye] = 3
    return _majority(cls, 3, keep=eye)


def _majority(cls, r, keep=None):
    """(2r+1)² 多數決去雜點；keep 標記的像素（細小的眼睛）不動。"""
    votes = np.stack([_box((cls == k).astype(np.float64), r) for k in range(4)], -1)
    out = votes.argmax(-1)
    if keep is not None:
        out[keep] = cls[keep]
    return out

#endregion

#region 輸出


def build_meshy_textures(images, out, size=1024):
    """images：{"base": Base Color 路徑, "metal_rough": 路徑或 None, "normal": 路徑或 None（目前不用）}。
    寫出 out/AhJia3D_BaseMap.png（sRGB）與 out/AhJia3D_MaskMap.png（linear），回傳 (base_path, mask_path)。"""
    base, mask, _ = _compute(images, size)
    return _write(base, mask, out)


def _write(base, mask, out):
    os.makedirs(out, exist_ok=True)
    base_path = os.path.join(out, "AhJia3D_BaseMap.png")
    mask_path = os.path.join(out, "AhJia3D_MaskMap.png")
    _save("AhJia3D_BaseMap", base, base_path, "sRGB")
    _save("AhJia3D_MaskMap", mask, mask_path, "Non-Color")
    print("貼圖：", base_path, mask_path)
    return base_path, mask_path


def _compute(images, size):
    base = _resize(_load(images["base"]), size)
    mr = images.get("metal_rough")
    mr = _resize(_load(mr), size) if mr else None
    cls = classify(base, mr)
    mask = np.zeros(base.shape)
    mask[..., 0] = (cls == 3)
    mask[..., 1] = np.take(CLASS_SHADOW, cls)
    mask[..., 2] = np.take(CLASS_VALUE, cls)
    return base, mask, cls


def _main():
    import argparse
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", required=True)
    ap.add_argument("--metal-rough")
    ap.add_argument("--normal")
    ap.add_argument("--out", required=True)
    ap.add_argument("--size", type=int, default=1024)
    a = ap.parse_args(argv)
    images = {"base": a.base, "metal_rough": a.metal_rough, "normal": a.normal}
    base, mask, cls = _compute(images, a.size)
    _write(base, mask, a.out)
    _save("AhJia3D_MaskClasses", np.take(np.array(CLASS_PREVIEW), cls, axis=0),
          os.path.join(a.out, "AhJia3D_MaskClasses.png"), "sRGB")


if __name__ == "__main__":
    _main()

#endregion
