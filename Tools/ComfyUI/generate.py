"""
用本機 ComfyUI（HTTP API）批次產生《熱浪急件》的 UI 素材原圖。

前置：ComfyUI 已在跑（預設 http://127.0.0.1:8188），模型放在 ComfyUI 的 models 底下（檔名見 README.md）。
用 ComfyUI 的 venv 執行（只用標準函式庫）：

    ~/ComfyUI/.venv/bin/python Tools/ComfyUI/generate.py            # 全部
    ~/ComfyUI/.venv/bin/python Tools/ComfyUI/generate.py panel button  # 只產生指定 id
    --seed-offset N：所有 seed 加 N（重抽時用，挑到滿意的再把 seed 寫回 assets.json）
    --model flux|sdxl：暫時改用另一個模型（比較用）

每個素材依 assets.json 的 model 走：
  flux：FLUX.1 schnell（4 步、cfg 1）
  sdxl：SDXL base 1.0＋IP-Adapter（reference 指定風格參考圖，相對於本資料夾）
mask 不是 false 時另外用 BiRefNet 算前景遮罩。輸出到 out/raw/<id>.png 與 out/raw/<id>_mask.png，
再由 postprocess.py 去背、裁切、對稱化並排進 sprite sheet。
"""
import argparse
import json
import os
import sys
import time
import urllib.parse
import urllib.request
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "out", "raw")

FLUX_CKPT = "flux1-schnell-fp8.safetensors"
SDXL_CKPT = "sd_xl_base_1.0.safetensors"
IPADAPTER = "ip-adapter_sdxl_vit-h.safetensors"
CLIP_VISION = "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors"
BIREFNET = "General.safetensors"
SDXL_NEGATIVE = "text, letters, watermark, signature, blurry, jpeg artifacts, photo, realistic, 3d render, noisy, cropped"


def build_workflow(asset, style, background, seed):
    """回傳 ComfyUI API 格式的 workflow（節點 id 是字串）。"""
    bg = asset.get("background_override", background)
    prompt = ", ".join(p for p in (asset["prompt"], asset.get("style_override", style), bg) if p)
    w, h = asset["width"], asset["height"]
    nodes = {}
    if asset["model"] == "flux":
        nodes["1"] = {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": FLUX_CKPT}}
        nodes["2"] = {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["1", 1]}}
        nodes["3"] = {"class_type": "CLIPTextEncode", "inputs": {"text": "", "clip": ["1", 1]}}
        nodes["4"] = {"class_type": "EmptySD3LatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}}
        model = ["1", 0]
        sampler = {"steps": 4, "cfg": 1.0, "sampler_name": "euler", "scheduler": "simple"}
    elif asset["model"] == "sdxl":
        nodes["1"] = {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": SDXL_CKPT}}
        nodes["2"] = {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["1", 1]}}
        nodes["3"] = {"class_type": "CLIPTextEncode", "inputs": {"text": SDXL_NEGATIVE, "clip": ["1", 1]}}
        nodes["4"] = {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}}
        model = ["1", 0]
        if asset.get("reference"):
            nodes["20"] = {"class_type": "IPAdapterModelLoader", "inputs": {"ipadapter_file": IPADAPTER}}
            nodes["21"] = {"class_type": "CLIPVisionLoader", "inputs": {"clip_name": CLIP_VISION}}
            nodes["22"] = {"class_type": "LoadImage", "inputs": {"image": upload(os.path.join(HERE, asset["reference"]))}}
            nodes["23"] = {"class_type": "IPAdapterAdvanced", "inputs": {
                "model": model, "ipadapter": ["20", 0], "image": ["22", 0], "clip_vision": ["21", 0],
                "weight": asset.get("reference_weight", 0.6), "weight_type": "style transfer", "combine_embeds": "concat",
                "start_at": 0.0, "end_at": 1.0, "embeds_scaling": "V only"}}
            model = ["23", 0]
        sampler = {"steps": 30, "cfg": 6.0, "sampler_name": "dpmpp_2m", "scheduler": "karras"}
    else:
        raise ValueError(f"{asset['id']}：不認得的 model {asset['model']}")

    nodes["5"] = {"class_type": "KSampler", "inputs": dict(sampler, seed=seed, denoise=1.0, model=model,
                                                            positive=["2", 0], negative=["3", 0], latent_image=["4", 0])}
    nodes["6"] = {"class_type": "VAEDecode", "inputs": {"samples": ["5", 0], "vae": ["1", 2]}}
    nodes["7"] = {"class_type": "SaveImage", "inputs": {"images": ["6", 0], "filename_prefix": f"ssui/{asset['id']}"}}
    if asset.get("mask", True):
        nodes["10"] = {"class_type": "LoadRembgByBiRefNetModel", "inputs": {"model": BIREFNET, "device": "AUTO", "dtype": "float32"}}
        nodes["11"] = {"class_type": "GetMaskByBiRefNet", "inputs": {"model": ["10", 0], "images": ["6", 0], "width": 1024,
                                                                      "height": 1024, "upscale_method": "bilinear", "mask_threshold": 0.0}}
        nodes["12"] = {"class_type": "MaskToImage", "inputs": {"mask": ["11", 0]}}
        nodes["13"] = {"class_type": "SaveImage", "inputs": {"images": ["12", 0], "filename_prefix": f"ssui/{asset['id']}_mask"}}
    return nodes


# ---------- ComfyUI API ----------
SERVER = "http://127.0.0.1:8188"


def post_json(path, data):
    req = urllib.request.Request(SERVER + path, data=json.dumps(data).encode(), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())


def get_json(path):
    with urllib.request.urlopen(SERVER + path) as r:
        return json.loads(r.read())


def upload(path):
    """把參考圖上傳到 ComfyUI 的 input 資料夾，回傳檔名。"""
    boundary = uuid.uuid4().hex
    with open(path, "rb") as f:
        data = f.read()
    name = os.path.basename(path)
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{name}\"\r\n"
            f"Content-Type: image/png\r\n\r\n").encode() + data + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(SERVER + "/upload/image", data=body,
                                 headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())["name"]


def run(workflow, timeout=1800):
    """送出 workflow、等它跑完，回傳 {節點 id: [圖片資訊…]}。"""
    pid = post_json("/prompt", {"prompt": workflow, "client_id": "ssui"})["prompt_id"]
    start = time.time()
    while time.time() - start < timeout:
        hist = get_json(f"/history/{pid}")
        if pid in hist:
            entry = hist[pid]
            status = entry.get("status", {})
            if status.get("status_str") == "error":
                raise RuntimeError(json.dumps(status.get("messages", []), ensure_ascii=False)[:2000])
            return {k: v.get("images", []) for k, v in entry.get("outputs", {}).items()}
        time.sleep(1.0)
    raise TimeoutError(f"workflow {pid} 逾時")


def download(image, dest):
    q = urllib.parse.urlencode({"filename": image["filename"], "subfolder": image["subfolder"], "type": image["type"]})
    with urllib.request.urlopen(f"{SERVER}/view?{q}") as r, open(dest, "wb") as f:
        f.write(r.read())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("ids", nargs="*", help="只產生這些 id（預設全部）")
    ap.add_argument("--seed-offset", type=int, default=0)
    ap.add_argument("--model", choices=["flux", "sdxl"], help="暫時改用這個模型（不改 assets.json）")
    args = ap.parse_args()

    with open(os.path.join(HERE, "assets.json"), encoding="utf-8") as f:
        spec = json.load(f)
    assets = [a for a in spec["assets"] if not args.ids or a["id"] in args.ids]
    missing = set(args.ids) - {a["id"] for a in assets}
    if missing:
        sys.exit(f"assets.json 沒有：{', '.join(sorted(missing))}")
    os.makedirs(RAW, exist_ok=True)

    for a in assets:
        seed = a["seed"] + args.seed_offset
        if args.model:
            a = dict(a, model=args.model)
        t = time.time()
        outputs = run(build_workflow(a, spec["style"], spec["background"], seed))
        download(outputs["7"][0], os.path.join(RAW, f"{a['id']}.png"))
        if "13" in outputs:
            download(outputs["13"][0], os.path.join(RAW, f"{a['id']}_mask.png"))
        print(f"{a['id']}：seed {seed}，{time.time() - t:.0f} 秒", flush=True)


if __name__ == "__main__":
    main()
