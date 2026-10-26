"""Speaks each line of a JSON array [{ id, text }] to <out_dir>/<id>.mp3 with a Microsoft neural voice via edge-tts.
Used by demo-video-i18n.mjs (Tamil voices are not shipped with Windows; this needs internet).
    python -I demo-video-edge-tts.py lines.json out_dir ta-IN-PallaviNeural +0%
"""
import asyncio
import json
import os
import sys

import edge_tts

lines_file, out_dir, voice = sys.argv[1], sys.argv[2], sys.argv[3]
rate = sys.argv[4] if len(sys.argv) > 4 else "+0%"
with open(lines_file, encoding="utf-8") as f:
    lines = json.load(f)
os.makedirs(out_dir, exist_ok=True)
sem = asyncio.Semaphore(4)


async def one(item):
    path = os.path.join(out_dir, f"{item['id']}.mp3")
    if os.path.exists(path):
        return 0
    async with sem:
        for attempt in range(4):
            try:
                await edge_tts.Communicate(item["text"], voice, rate=rate).save(path)
                return 1
            except Exception as e:  # network hiccups: retry
                if os.path.exists(path):
                    os.remove(path)
                if attempt == 3:
                    raise
                print(f"retry {item['id']}: {e}", file=sys.stderr)
                await asyncio.sleep(2 + attempt * 2)


async def main():
    done = sum(await asyncio.gather(*(one(i) for i in lines)))
    print(f"spoke {done} of {len(lines)} lines with {voice}")


asyncio.run(main())
