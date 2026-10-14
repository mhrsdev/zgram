#!/usr/bin/env python3
"""Pull ONLY public JavaScript / source-map files out of a browser HAR export.

Why: the HAR also contains login requests, cookies and tokens. This script
never writes any of that. It reads the HAR, keeps responses whose URL path ends
in .js / .mjs / .map and whose host is web.bale.ai (or --host), and writes each
body to <out>/<path-from-url>. Requests, headers, cookies and non-JS responses
are dropped. Standard library only (Python 3.8+).

    python har_extract_js.py bale.har out_folder
"""
import argparse, base64, json, os, sys
from urllib.parse import urlparse


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("har")
    ap.add_argument("out")
    ap.add_argument("--host", default="bale.ai", help="keep URLs whose host ends with this")
    a = ap.parse_args()

    with open(a.har, encoding="utf-8") as f:
        entries = json.load(f)["log"]["entries"]

    kept = skipped = 0
    for e in entries:
        u = urlparse(e["request"]["url"])
        name = u.path.lstrip("/") or "index.html"
        if not (u.hostname or "").endswith(a.host):
            continue
        if not name.endswith((".js", ".mjs", ".map")):
            continue
        c = e["response"].get("content", {})
        text = c.get("text")
        if text is None:
            skipped += 1
            continue
        data = base64.b64decode(text) if c.get("encoding") == "base64" else text.encode("utf-8")
        # keep the host in the path so same-named files from different hosts do not collide
        dest = os.path.normpath(os.path.join(a.out, u.hostname, name))
        if not dest.startswith(os.path.normpath(a.out)):
            continue  # never write outside the output folder
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "wb") as f:
            f.write(data)
        kept += 1

    print(f"saved {kept} files to {a.out}; {skipped} JS entries had no body in the HAR")
    if kept == 0:
        sys.exit("nothing saved: export the HAR with content ('Save all as HAR with content')")


if __name__ == "__main__":
    main()
