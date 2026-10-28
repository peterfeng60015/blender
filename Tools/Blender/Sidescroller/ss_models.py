"""《熱浪急件》素材建模入口（Blender 內執行）：依 --target 建立一種素材的模型與動作。

由 render_all.py 串在 render_sprites.py 之前，參數放在 `--` 之後（只讀 --target）。
"""

import argparse
import os
import sys

import bpy

sys.dont_write_bytecode = True   # 不在 Tools/ 底下留 __pycache__
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ss_cast  # noqa: E402
import ss_hero  # noqa: E402
import ss_items  # noqa: E402
import ss_lib  # noqa: E402
import ss_portraits  # noqa: E402
import ss_scenery  # noqa: E402

BUILDERS = {
    "SsHeroDay": ss_hero.build_day,
    "SsHeroExpress": ss_hero.build_express,
    **ss_cast.BUILDERS,
    **ss_items.BUILDERS,
    **ss_scenery.BUILDERS,
    **ss_portraits.BUILDERS,
}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(prog="ss_models.py")
    p.add_argument("--target", required=True)
    args, _ = p.parse_known_args(argv)
    if args.target not in BUILDERS:
        raise SystemExit(f"[ss_models] 不認得的素材：{args.target}（可用：{', '.join(BUILDERS)}）")
    ss_lib.reset_scene()
    BUILDERS[args.target]()
    bpy.context.scene.frame_set(1)
    print(f"[ss_models] 已建立 {args.target}")


if __name__ == "__main__":
    main()
