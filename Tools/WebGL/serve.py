#!/usr/bin/env python3
"""本機測試 WebGL 包用的 http 伺服器：同一個來源同時提供遊戲本體與 ServerData。

  /                 → Builds/WebGL/（index.html、Build/、TemplateData/）
  /ServerData/...   → ServerData/...（CodePackage／AssetPackage）

用法（在專案根目錄或任何地方都可以）：
  python3 Tools/WebGL/serve.py [--port 8080] [--bind 127.0.0.1]
然後開 http://127.0.0.1:8080/?serverRoot=ServerData

同網段手機實測：加 --bind 0.0.0.0，手機開啟動時印出的區網網址（Mac 防火牆要允許 python3 連入）。

?serverRoot=ServerData 會以頁面網址為基準解析成 http://127.0.0.1:8080/ServerData，
EntrySceneController 只接受與頁面同源的值（見 ResolveWebServerRoot）。

只給本機測試用：沒有 Range（斷點續傳）、沒有 HTTPS。預設只綁 127.0.0.1。
綁 0.0.0.0 時同網段任何人都能讀 Builds/WebGL 與 ServerData，測完就關掉。
"""

import argparse
import http.server
import os
import posixpath
import socket
import urllib.parse

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
GAME_ROOT = os.path.join(PROJECT_ROOT, "Builds", "WebGL")
SERVER_DATA_ROOT = os.path.join(PROJECT_ROOT, "ServerData")
SERVER_DATA_PREFIX = "/ServerData/"

# WebGL 需要正確的 MIME：.wasm 不是 application/wasm 時瀏覽器無法串流編譯。
EXTRA_TYPES = {
    ".wasm": "application/wasm",
    ".js": "application/javascript",
    ".data": "application/octet-stream",
    ".bundle": "application/octet-stream",
    ".archive": "application/octet-stream",
    ".bytes": "application/octet-stream",
    ".hash": "text/plain",
    ".version": "text/plain",
}

# 以壓縮格式出包時（Brotli／Gzip，且沒開 Decompression Fallback）要回 Content-Encoding。
ENCODINGS = {".br": "br", ".gz": "gzip"}

# 開了 Decompression Fallback 時檔名是 .unityweb，內容可能是 gzip、brotli 或未壓縮。
# 只有 gzip 有固定檔頭（1f 8b），看得出來就回 Content-Encoding: gzip 讓瀏覽器原生解壓（省下 JS 解壓的時間）；
# 其他情況不回，交給載入器用 JS 解壓。載入器會自己檢查內容是否還是壓縮的，兩種情況都能跑。
FALLBACK_EXT = ".unityweb"
GZIP_MAGIC = b"\x1f\x8b"


def _is_gzip_file(path):
    try:
        with open(path, "rb") as f:
            return f.read(2) == GZIP_MAGIC
    except OSError:
        return False


class Handler(http.server.SimpleHTTPRequestHandler):
    def translate_path(self, path):
        # 去掉查詢字串（YooAsset 讀版本指針會加 ?時間戳）與 fragment
        path = urllib.parse.urlsplit(path).path
        path = posixpath.normpath(urllib.parse.unquote(path))
        if path == SERVER_DATA_PREFIX.rstrip("/") or path.startswith(SERVER_DATA_PREFIX):
            root, rel = SERVER_DATA_ROOT, path[len(SERVER_DATA_PREFIX.rstrip("/")):]
        else:
            root, rel = GAME_ROOT, path
        # 比照標準庫：略過 . ..、含路徑分隔字元（Windows 的 \）或磁碟代號（C:）的段，
        # 最後再確認實際路徑仍在 root 底下，擋掉路徑穿越。
        parts = []
        for p in rel.split("/"):
            if not p or p in (".", "..") or os.path.dirname(p) or os.path.splitdrive(p)[0]:
                continue
            if os.sep in p or (os.altsep and os.altsep in p):
                continue
            parts.append(p)
        full = os.path.join(root, *parts)
        real_root = os.path.realpath(root)
        if os.path.commonpath([real_root, os.path.realpath(full)]) != real_root:
            return os.path.join(root, "__forbidden__")
        return full

    def guess_type(self, path):
        base, ext = os.path.splitext(path)
        if ext in ENCODINGS or ext == FALLBACK_EXT:
            ext = os.path.splitext(base)[1]
        return EXTRA_TYPES.get(ext) or super().guess_type(path)

    def end_headers(self):
        ext = os.path.splitext(urllib.parse.urlsplit(self.path).path)[1]
        if ext in ENCODINGS:
            self.send_header("Content-Encoding", ENCODINGS[ext])
        elif ext == FALLBACK_EXT and _is_gzip_file(self.translate_path(self.path)):
            self.send_header("Content-Encoding", "gzip")
        # 測試時每次都拿最新的檔：版本指針、manifest、重新出的包都不該被快取住。
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()


def _lan_ip():
    """本機對外的區網 IP（不真的送封包，只讓系統選路由）。拿不到時回 None。"""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("10.255.255.255", 1))
        ip = s.getsockname()[0]
        return None if ip.startswith("127.") else ip
    except OSError:
        return None
    finally:
        s.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--bind", default="127.0.0.1")
    args = parser.parse_args()

    if not os.path.isdir(GAME_ROOT):
        print(f"找不到 {GAME_ROOT}，請先出 WebGL 包（Tools/WebGL/一鍵出 WebGL 包）")
    if not os.path.isdir(SERVER_DATA_ROOT):
        print(f"找不到 {SERVER_DATA_ROOT}，請先打代碼包與資源包")

    server = http.server.ThreadingHTTPServer((args.bind, args.port), Handler)
    # 0.0.0.0 不是能開的網址（新版 Chrome 會擋），本機一律印 127.0.0.1
    local_host = "127.0.0.1" if args.bind == "0.0.0.0" else args.bind
    print(f"遊戲：  http://{local_host}:{args.port}/?serverRoot=ServerData")
    if args.bind == "0.0.0.0":
        lan_ip = _lan_ip()
        if lan_ip:
            print(f"區網：  http://{lan_ip}:{args.port}/?serverRoot=ServerData")
    print(f"根目錄：{GAME_ROOT}")
    print(f"資料：  {SERVER_DATA_ROOT}（/ServerData/）")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
