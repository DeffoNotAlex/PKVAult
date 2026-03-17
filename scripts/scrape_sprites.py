#!/usr/bin/env python3
"""
Scrape Pokémon animated sprites from the Pokémon Showdown CDN.

For each species it tries:
  1. https://play.pokemonshowdown.com/sprites/ani/<slug>.gif          (modern animated)
  2. https://play.pokemonshowdown.com/sprites/gen5ani/<slug>.gif      (Gen 5 fallback)

And the shiny variants:
  1. https://play.pokemonshowdown.com/sprites/ani-shiny/<slug>.gif
  2. https://play.pokemonshowdown.com/sprites/gen5ani-shiny/<slug>.gif

Output layout:
  <out>/
    ani/           *.gif   (normal, modern)
    ani-shiny/     *.gif   (shiny, modern)
    gen5ani/       *.gif   (normal, gen5 fallback)
    gen5ani-shiny/ *.gif   (shiny, gen5 fallback)

  <out>/missing.txt     — slugs with no sprite in any folder
  <out>/report.json     — full per-slug status

Usage:
  pip install requests
  python scripts/scrape_sprites.py [--out sprites] [--workers 16] [--dry-run]
  python scripts/scrape_sprites.py --folders ani gen5ani   # skip shiny to save space
  python scripts/scrape_sprites.py --slugs bulbasaur charizard  # targeted test
"""

import argparse
import json
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

BASE_URL     = "https://play.pokemonshowdown.com/sprites"
POKEDEX_URL  = "https://play.pokemonshowdown.com/data/pokedex.json"

ALL_FOLDERS = [
    "ani",
    "ani-shiny",
    "gen5ani",
    "gen5ani-shiny",
]


# ── Slug helpers ──────────────────────────────────────────────────────────────

def to_slug(name: str) -> str:
    """Mirror the ToShowdownSlug() logic in GamePage.xaml.cs.
    Showdown strips ALL non-alphanumeric characters — no hyphens from spaces,
    no -f/-m gender suffixes. E.g. 'Mr. Mime' → 'mrmime', 'Nidoran♀' → 'nidoranf'.
    """
    import re
    s = name.lower().replace("♀", "f").replace("♂", "m").replace("é", "e")
    return re.sub(r"[^a-z0-9]", "", s)


# ── Species list ──────────────────────────────────────────────────────────────

def fetch_slugs(session: requests.Session) -> list[str]:
    """
    Pull the full species slug list from Showdown's pokedex.json.
    Keys are already in slug form (e.g. 'bulbasaur', 'nidoranf', 'mr-mime').
    """
    print("Fetching species list from Showdown pokedex …", flush=True)
    r = session.get(POKEDEX_URL, timeout=15)
    r.raise_for_status()
    data = r.json()
    slugs = sorted(data.keys())
    print(f"  {len(slugs)} entries found.\n")
    return slugs


# ── Download helpers ──────────────────────────────────────────────────────────

def make_session(workers: int) -> requests.Session:
    session = requests.Session()
    retry = Retry(total=3, backoff_factor=0.5,
                  status_forcelist=[429, 500, 502, 503, 504])
    adapter = HTTPAdapter(max_retries=retry,
                          pool_connections=workers,
                          pool_maxsize=workers * 2)
    session.mount("https://", adapter)
    session.mount("http://", adapter)
    session.headers["User-Agent"] = "PKHeX-sprite-scraper/1.0"
    return session


def try_download(session: requests.Session, folder: str, slug: str,
                 out_dir: Path, dry_run: bool) -> str:
    """
    Try to download sprites/<folder>/<slug>.gif.
    Returns: 'cached' | 'ok' | 'missing' | 'error:<msg>'
    """
    dest = out_dir / folder / f"{slug}.gif"
    if dest.exists() and dest.stat().st_size > 50:
        return "cached"

    url = f"{BASE_URL}/{folder}/{slug}.gif"

    try:
        if dry_run:
            r = session.head(url, timeout=12, allow_redirects=True)
            return "ok" if r.status_code == 200 else "missing"
        r = session.get(url, timeout=12)
        if r.status_code == 200 and len(r.content) > 50:
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(r.content)
            return "ok"
        return "missing"
    except Exception as exc:
        return f"error:{exc}"


def process_slug(session: requests.Session, slug: str, folders: list[str],
                 out_dir: Path, dry_run: bool) -> dict:
    result: dict[str, str] = {}
    for folder in folders:
        result[folder] = try_download(session, folder, slug, out_dir, dry_run)
    return result


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out",     default="scripts/sprites",
                        help="Output directory (default: scripts/sprites)")
    parser.add_argument("--workers", type=int, default=16,
                        help="Parallel download threads (default: 16)")
    parser.add_argument("--dry-run", action="store_true",
                        help="HEAD-check URLs without saving files")
    parser.add_argument("--folders", nargs="+", default=ALL_FOLDERS,
                        choices=ALL_FOLDERS, metavar="FOLDER",
                        help="Which sprite folders to fetch (default: all four)")
    parser.add_argument("--slugs",  nargs="*",
                        help="Only process these slugs (for targeted runs/testing)")
    args = parser.parse_args()

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    session = make_session(args.workers)

    slugs = args.slugs if args.slugs else fetch_slugs(session)
    total = len(slugs)

    print(f"Folders : {', '.join(args.folders)}")
    print(f"Slugs   : {total}")
    print(f"Workers : {args.workers}")
    if args.dry_run:
        print("Mode    : dry-run (HEAD only, no files written)")
    print()

    report: dict[str, dict[str, str]] = {}
    missing: list[str] = []

    start = time.monotonic()
    done  = 0

    with ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = {
            pool.submit(process_slug, session, slug, args.folders, out_dir, args.dry_run): slug
            for slug in slugs
        }
        for future in as_completed(futures):
            slug   = futures[future]
            result = future.result()
            report[slug] = result
            done += 1

            has_any = any(v in ("ok", "cached") for v in result.values())
            if not has_any:
                missing.append(slug)

            if done % 100 == 0 or done == total:
                elapsed = time.monotonic() - start
                rate    = done / elapsed
                eta     = (total - done) / rate if rate > 0 else 0
                print(f"  {done}/{total}  ({elapsed:.0f}s elapsed, ~{eta:.0f}s left)  "
                      f"missing so far: {len(missing)}")

    # ── Reports ───────────────────────────────────────────────────────────────
    report_path  = out_dir / "report.json"
    missing_path = out_dir / "missing.txt"

    report_path.write_text(
        json.dumps(dict(sorted(report.items())), indent=2), encoding="utf-8")
    missing_path.write_text(
        "\n".join(sorted(missing)) + ("\n" if missing else ""), encoding="utf-8")

    # ── Summary ───────────────────────────────────────────────────────────────
    elapsed = time.monotonic() - start
    counts  = {f: 0 for f in args.folders}
    for slug_result in report.values():
        for folder, status in slug_result.items():
            if status in ("ok", "cached"):
                counts[folder] += 1

    print(f"\n{'─' * 52}")
    print(f"Finished in {elapsed:.0f}s\n")
    print("Sprites available per folder:")
    for folder in args.folders:
        bar_len = 30
        frac = counts[folder] / total if total else 0
        bar  = "█" * int(frac * bar_len) + "░" * (bar_len - int(frac * bar_len))
        print(f"  {folder:20s}  {bar}  {counts[folder]:4d} / {total}")

    print(f"\nMissing entirely (no working sprite): {len(missing)}")
    if missing:
        print(f"  → {missing_path}")

    # Estimate disk usage
    total_bytes = sum(
        f.stat().st_size
        for folder in args.folders
        for f in (out_dir / folder).glob("*.gif")
        if f.exists()
    )
    if total_bytes:
        print(f"\nDisk usage : {total_bytes / 1_048_576:.1f} MB")

    print(f"Full report: {report_path}")
    return 0 if not missing else 1


if __name__ == "__main__":
    sys.exit(main())
