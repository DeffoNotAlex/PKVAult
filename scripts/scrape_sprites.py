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
  sprites/
    ani/           *.gif   (normal, modern)
    ani-shiny/     *.gif   (shiny, modern)
    gen5ani/       *.gif   (normal, gen5 fallback)
    gen5ani-shiny/ *.gif   (shiny, gen5 fallback)

  sprites/missing.txt     — slugs with no sprite in any folder
  sprites/report.json     — full per-slug status

Usage:
  pip install requests
  python scripts/scrape_sprites.py [--out sprites] [--workers 8] [--dry-run]
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

BASE_URL = "https://play.pokemonshowdown.com/sprites"
POKEDEX_URL = "https://play.pokemonshowdown.com/data/pokedex.json"

FOLDERS = [
    "ani",
    "ani-shiny",
    "gen5ani",
    "gen5ani-shiny",
]


# ── Slug helpers ──────────────────────────────────────────────────────────────

def to_slug(name: str) -> str:
    """Mirror the ToShowdownSlug() logic in GamePage.xaml.cs."""
    return (
        name.lower()
        .replace("♀", "-f").replace("♂", "-m")
        .replace(" ", "-").replace(".", "")
        .replace("'", "").replace(":", "")
        .replace("é", "e")
    )


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


def process_slug(session: requests.Session, slug: str,
                 out_dir: Path, dry_run: bool) -> dict:
    """
    For one slug, attempt every folder and collect results.
    Returns a status dict ready for the JSON report.
    """
    result: dict[str, str] = {}
    for folder in FOLDERS:
        result[folder] = try_download(session, folder, slug, out_dir, dry_run)
    return result


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out",     default="sprites", help="Output directory (default: sprites)")
    parser.add_argument("--workers", type=int, default=8,  help="Parallel download threads (default: 8)")
    parser.add_argument("--dry-run", action="store_true",  help="Check URLs without saving files")
    parser.add_argument("--slugs",   nargs="*",            help="Only process these slugs (for testing)")
    args = parser.parse_args()

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    session = make_session(args.workers)

    if args.slugs:
        slugs = args.slugs
    else:
        slugs = fetch_slugs(session)

    total   = len(slugs)
    report  = {}   # slug → {folder: status}
    missing = []   # slugs with no working sprite at all

    print(f"Processing {total} slugs with {args.workers} workers …")
    if args.dry_run:
        print("  (dry-run: no files will be written)\n")

    start = time.monotonic()
    done  = 0

    with ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = {
            pool.submit(process_slug, session, slug, out_dir, args.dry_run): slug
            for slug in slugs
        }
        for future in as_completed(futures):
            slug   = futures[future]
            result = future.result()
            report[slug] = result
            done += 1

            # Determine overall status for this slug
            has_any = any(
                v in ("ok", "cached")
                for v in result.values()
            )
            if not has_any:
                missing.append(slug)

            # Progress every 50 or at end
            if done % 50 == 0 or done == total:
                elapsed = time.monotonic() - start
                print(f"  {done}/{total}  ({elapsed:.1f}s)  missing so far: {len(missing)}")

    # ── Write reports ─────────────────────────────────────────────────────────
    report_path  = out_dir / "report.json"
    missing_path = out_dir / "missing.txt"

    report_path.write_text(
        json.dumps(dict(sorted(report.items())), indent=2),
        encoding="utf-8"
    )

    missing_path.write_text(
        "\n".join(sorted(missing)) + ("\n" if missing else ""),
        encoding="utf-8"
    )

    # ── Summary ───────────────────────────────────────────────────────────────
    elapsed = time.monotonic() - start
    counts  = {f: 0 for f in FOLDERS}
    for slug_result in report.values():
        for folder, status in slug_result.items():
            if status in ("ok", "cached"):
                counts[folder] += 1

    print(f"\n{'─' * 50}")
    print(f"Done in {elapsed:.1f}s")
    print(f"\nSprites downloaded (or already cached):")
    for folder, n in counts.items():
        print(f"  {folder:20s}  {n:4d} / {total}")
    print(f"\nMissing entirely (no folder had a sprite): {len(missing)}")
    if missing:
        print(f"  → see {missing_path}")
    print(f"\nFull report: {report_path}")

    return 0 if not missing else 1


if __name__ == "__main__":
    sys.exit(main())
