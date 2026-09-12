#!/usr/bin/env python3
"""Offline reader for the SOLIDWORKS 2025 API help (local CHM files).

Subcommands: setup, find, show, grep, members.
Run `python swapi.py --help` for usage. Stdlib only.
"""
import argparse
import html
import os
import re
import subprocess
import sys
from html.parser import HTMLParser
from pathlib import Path

API_DIR = Path(r"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api")
SEVENZIP = Path(r"C:\Program Files\7-Zip\7z.exe")
CACHE = Path(os.environ.get("LOCALAPPDATA", Path.home())) / "G-CAM" / "swapi-help"

# CHM basename -> what it covers. Order matters: earlier books win name collisions.
BOOKS = {
    "sldworksapi": "interfaces, methods, properties (ISldWorks, IModelDoc2, IBody2, ...)",
    "swconst": "enumerations and constants (swDocumentTypes_e, swBodyType_e, ...)",
    "swpublishedapi": "add-in interfaces you implement (ISwAddin, IPropertyManagerPage2Handler9, ...)",
    "sldworksapiprogguide": "conceptual programming guide and overviews",
}


# --------------------------------------------------------------------------- setup


def setup(force=False):
    if not SEVENZIP.exists():
        sys.exit(f"7-Zip not found at {SEVENZIP}. Install it or edit SEVENZIP in this script.")
    CACHE.mkdir(parents=True, exist_ok=True)
    for book in BOOKS:
        chm = API_DIR / f"{book}.chm"
        dest = CACHE / book
        if not chm.exists():
            print(f"skip {book}: {chm} not found")
            continue
        if dest.exists() and not force:
            print(f"ok   {book}: already extracted ({len(list(dest.rglob('*.htm*')))} topics)")
            continue
        dest.mkdir(parents=True, exist_ok=True)
        # Only HTML topics -- images and CSS triple the size and are never read.
        # -r is required: some books keep topics in subfolders (Overview\, GettingStarted\).
        subprocess.run(
            [str(SEVENZIP), "x", "-y", "-r", f"-o{dest}", str(chm), "*.htm", "*.html"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False,
        )
        print(f"done {book}: {len(list(dest.rglob('*.htm*')))} topics -> {dest}")
    print(f"\nCache: {CACHE}")


def ensure_setup():
    if not CACHE.exists() or not any(CACHE.glob("*/*.htm*")):
        sys.exit("Help not extracted yet. Run:  python swapi.py setup")


# --------------------------------------------------------------------------- topic naming

# Real filenames look like:
#   SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ICommandManager~CreateCommandGroup2.html
#   SolidWorks.Interop.swconst~SolidWorks.Interop.swconst.swDocumentTypes_e.html
# Short name is the part after the last '~', prefixed by the type: ICommandManager.CreateCommandGroup2
NS_RE = re.compile(r"^[\w.]+~[\w.]+?\.(?P<rest>[\w]+(?:~[\w]+)?)(?P<suffix>_methods|_properties|_fields|_events)?$")


def short_name(path: Path) -> str:
    stem = path.stem
    m = NS_RE.match(stem)
    if not m:
        return stem
    rest = m.group("rest").replace("~", ".")
    if m.group("suffix"):
        rest += m.group("suffix")
    return rest


def all_topics():
    """Yield (short_name, path) for every topic, first book wins on duplicates."""
    seen = {}
    for book in BOOKS:
        d = CACHE / book
        if not d.exists():
            continue
        for p in sorted(d.rglob("*.htm*")):
            n = short_name(p)
            seen.setdefault(n.lower(), (n, p))
    return seen


# Every example ships in C#, VB.NET and VBA flavours; the VB ones are dead weight here.
VB_TOPIC = re.compile(r"_(VB|VBNET|VBA)$", re.I)


def resolve(query: str, all_langs=False):
    """Find topics matching query. Exact short-name match wins outright."""
    topics = all_topics()
    q = query.lower()
    if q in topics:
        return [topics[q]]
    # Try regex/substring across short names.
    try:
        rx = re.compile(query, re.I)
    except re.error:
        rx = re.compile(re.escape(query), re.I)
    hits = [v for k, v in topics.items() if rx.search(v[0])]
    if not all_langs:
        non_vb = [h for h in hits if not VB_TOPIC.search(h[0])]
        hits = non_vb or hits  # fall back if the query genuinely wanted a VB page
    hits.sort(key=lambda kv: (len(kv[0]), kv[0]))
    return hits


# --------------------------------------------------------------------------- html -> text

DROP_TAGS = {"script", "style", "head", "title"}
# NOTE: expandcollapse is kept -- the section heading text ("Syntax", "Remarks") lives inside it.
DROP_CLASSES = {"dxpopupbubble", "savehistory", "copycode"}
BLOCK = {"div", "p", "tr", "h1", "h2", "h3", "h4", "h5", "li", "br", "table", "pre"}


class TopicParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.out = []
        self.skip_depth = 0
        self.skip_tag = None
        self.cell = False

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        cls = (a.get("class") or "").lower()
        if self.skip_depth:
            if tag == self.skip_tag:
                self.skip_depth += 1
            return
        if tag in DROP_TAGS or any(c in cls for c in DROP_CLASSES):
            self.skip_depth, self.skip_tag = 1, tag
            return
        if tag in ("h1", "h2", "h3", "h4"):
            self.out.append("\n\n## ")
        elif tag in ("td", "th"):
            if self.cell:
                self.out.append(" | ")
            self.cell = True
        elif tag in BLOCK:
            self.out.append("\n")

    def handle_endtag(self, tag):
        if self.skip_depth:
            if tag == self.skip_tag:
                self.skip_depth -= 1
            return
        if tag == "tr":
            self.cell = False
            self.out.append("\n")
        elif tag in BLOCK:
            self.out.append("\n")

    def handle_data(self, data):
        if self.skip_depth:
            return
        self.out.append(data)

    def text(self):
        t = html.unescape("".join(self.out)).replace("﻿", "")
        t = re.sub(r"[ \t\xa0]+", " ", t)
        t = re.sub(r" *\n *", "\n", t)
        t = re.sub(r"\n{3,}", "\n\n", t)
        return t.strip()


# Chrome that appears on nearly every page and carries no information.
NOISE = re.compile(
    r"^(Collapse All|Expand All|Show All|Hide All|Language Filter:|"
    r"\| ?Send comments on this topic\.?|Send comments on this topic\.?|"
    r"SOLIDWORKS API Help|Glossary Item Box|"
    r"NOTE: See Differences Between Unmanaged C\+\+ and C\+\+/CLI Code\.?|"
    r"[>:]\s|"                    # breadcrumb trail lines
    r"[\w.]+ Namespace$)",
    re.I,
)

# Whole sections that are dead weight for a C# add-in.
SKIP_SECTION = re.compile(r"^## (Visual Basic for Applications|VBA)\b", re.I)
# Anchor links at the top of the page; the same words also appear as real headings below.
NAV_LINKS = {"see also", "example", "examples"}

# The syntax table repeats every signature once per language. Keep C# only.
LANG_MARKER = re.compile(r"^(Visual Basic( \((Declaration|Usage)\))?|C#|C\+\+/CLI|VB\.NET)\s*\|?\s*$", re.I)
CSHARP = re.compile(r"^C#\s*\|?\s*$", re.I)


def render(path: Path, keep_all_langs=False) -> str:
    p = TopicParser()
    p.feed(path.read_text(encoding="utf-8-sig", errors="replace"))

    lines, lang_ok = [], True
    for raw in p.text().splitlines():
        s = raw.strip()
        if not s or NOISE.match(s):
            continue
        if not keep_all_langs:
            if LANG_MARKER.match(s):
                # Entering a language-specific block: keep it only if it is C#.
                lang_ok = bool(CSHARP.match(s))
                continue
            if s.startswith("##"):
                lang_ok = True  # a new section ends the syntax table
            if not lang_ok:
                continue
        lines.append(s)

    # Attach each heading marker to its text, and drop headings that ended up empty.
    merged = []
    for s in lines:
        if merged and merged[-1] == "##":
            merged[-1] = f"## {s}"
        else:
            merged.append(s)
    merged = [s for s in merged if s != "##"]

    # Drop skipped sections, and the bare nav links above the first heading.
    kept, skipping = [], False
    for s in merged:
        if s.startswith("##"):
            skipping = bool(SKIP_SECTION.match(s))
        if skipping:
            continue
        if s.lower() in NAV_LINKS:  # a real heading keeps its "## " prefix
            continue
        kept.append(s)

    # Collapse consecutive duplicates -- the help repeats headings and titles.
    out = []
    for s in kept:
        if not out or out[-1] != s:
            out.append(s)
    return "\n".join(out)


# --------------------------------------------------------------------------- commands


def cmd_find(args):
    ensure_setup()
    hits = resolve(args.query, args.all_langs)
    if not hits:
        sys.exit(f"No topic matching {args.query!r}")
    for name, path in hits[: args.limit]:
        print(f"{name}  [{path.parent.name}]")
    if len(hits) > args.limit:
        print(f"... {len(hits) - args.limit} more")


def cmd_show(args):
    ensure_setup()
    hits = resolve(args.query, args.all_langs)
    if not hits:
        sys.exit(f"No topic matching {args.query!r}")
    if len(hits) > 1 and hits[0][0].lower() != args.query.lower():
        print(f"{len(hits)} matches; showing first. Others:")
        for name, _ in hits[1:11]:
            print(f"  {name}")
        print()
    name, path = hits[0]
    print(f"=== {name}  [{path.parent.name}] ===")
    print(render(path, keep_all_langs=args.all_langs))


def cmd_members(args):
    ensure_setup()
    topics = all_topics()
    prefix = args.type.lower() + "."
    names = sorted(v[0] for k, v in topics.items() if k.startswith(prefix))
    if not names:
        sys.exit(f"No members found for {args.type!r}. Try: python swapi.py find {args.type}")
    for n in names:
        print(n)


def cmd_grep(args):
    ensure_setup()
    rx = re.compile(args.pattern, re.I)
    shown = 0
    for name, path in sorted(all_topics().values()):
        if args.type and not name.lower().startswith(args.type.lower()):
            continue
        if not args.all_langs and VB_TOPIC.search(name):
            continue
        try:
            body = render(path)
        except Exception:
            continue
        for ln in body.splitlines():
            if rx.search(ln):
                print(f"{name}: {ln[:200]}")
                shown += 1
                break
        if shown >= args.limit:
            print(f"... stopped at {args.limit} matches")
            return


def main():
    # The help text is UTF-8; the Windows console defaults to cp1252 and would crash on it.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("setup", help="extract the CHM help into the local cache (run once)")
    s.add_argument("--force", action="store_true", help="re-extract even if cached")
    s.set_defaults(func=lambda a: setup(a.force))

    langs_help = "include VB/VBA/C++ pages and signatures (default: C# only)"

    s = sub.add_parser("find", help="list topics whose name matches a substring/regex")
    s.add_argument("query")
    s.add_argument("--limit", type=int, default=40)
    s.add_argument("--all-langs", action="store_true", dest="all_langs", help=langs_help)
    s.set_defaults(func=cmd_find)

    s = sub.add_parser("show", help="print one topic as clean text")
    s.add_argument("query", help="e.g. ICommandManager.CreateCommandGroup2")
    s.add_argument("--all-langs", action="store_true", dest="all_langs", help=langs_help)
    s.set_defaults(func=cmd_show)

    s = sub.add_parser("members", help="list all members of an interface or enum")
    s.add_argument("type", help="e.g. ICommandManager")
    s.set_defaults(func=cmd_members)

    s = sub.add_parser("grep", help="full-text search across topic bodies")
    s.add_argument("pattern")
    s.add_argument("--type", help="restrict to one interface/enum prefix")
    s.add_argument("--limit", type=int, default=30)
    s.add_argument("--all-langs", action="store_true", dest="all_langs", help=langs_help)
    s.set_defaults(func=cmd_grep)

    args = ap.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
