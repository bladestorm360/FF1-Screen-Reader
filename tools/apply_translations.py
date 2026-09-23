"""
Merge reviewed map-label translations into FF1's translation.json (the embedded resource that
Utils/EntityTranslator.cs loads), keeping the file's exact formatting: 2-space indent, CRLF
line endings, raw UTF-8, insertion order. Existing keys stay where they are; new keys are
appended at the end.

A batch is a JSON object {japanese_key: {lang: text, ...}}:
  * existing key: only the languages given (non-empty) are overwritten
  * new key:      must carry all 11 languages; 'ja' is never stored (the key is the Japanese)

Usage:
  python apply_translations.py apply <batch.json> [--dry]
  python apply_translations.py check-official <gamedict.json>
        list existing keys that exactly match an official game string but differ from it
        (review each: shop words and speaker labels are often the wrong context)
  python apply_translations.py status

Make the gamedict with:  python extract_entities.py gamedict <out.json>
"""
import json, os, re, sys

MOD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(MOD, "translation.json")
LANGS = ["en", "fr", "it", "de", "es", "ko", "zht", "zhc", "ru", "th", "pt"]

# EntityTranslator strips these from the end of a label BEFORE the first lookup, so a key
# ending in one of them can never be hit.
_UNREACHABLE_TAIL = re.compile(r"(\d+|[①-⑳]+)$")


def load():
    raw = open(PATH, "rb").read()
    d = json.loads(raw.decode("utf-8"))
    if serialize(d) != raw:
        sys.exit("translation.json does not round-trip byte-for-byte; refusing to rewrite it")
    return d


def serialize(d):
    text = json.dumps(d, ensure_ascii=False, indent=2).replace("\n", "\r\n") + "\r\n"
    return text.encode("utf-8")


# ── Mirror of ModTextTranslator.ParseNestedJson, the parser the mod actually uses ──
def _closing_quote(s, i):
    while i < len(s):
        if s[i] == "\\":
            i += 2
            continue
        if s[i] == '"':
            return i
        i += 1
    return -1


def _matching_brace(s, i):
    depth, in_str, i = 1, False, i + 1
    while i < len(s):
        c = s[i]
        if c == "\\" and in_str:
            i += 2
            continue
        if c == '"':
            in_str = not in_str
        elif not in_str:
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    return i
        i += 1
    return -1


_ESC = {'"': '"', "\\": "\\", "n": "\n", "r": "\r", "t": "\t", "/": "/"}


def _unescape(s):
    out, i = [], 0
    while i < len(s):
        if s[i] == "\\" and i + 1 < len(s) and s[i + 1] in _ESC:
            out.append(_ESC[s[i + 1]])
            i += 2
        else:
            out.append(s[i])
            i += 1
    return "".join(out)


def parse_like_mod(text):
    text = text.strip()
    inner = text[1:-1]
    result, pos = {}, 0
    while pos < len(inner):
        ks = inner.find('"', pos)
        if ks < 0:
            break
        ke = _closing_quote(inner, ks + 1)
        if ke < 0:
            break
        key = _unescape(inner[ks + 1:ke])
        bs = inner.find("{", ke)
        if bs < 0:
            break
        be = _matching_brace(inner, bs)
        if be < 0:
            break
        body, sub, p = inner[bs + 1:be], {}, 0
        while p < len(body):
            a = body.find('"', p)
            if a < 0:
                break
            b = _closing_quote(body, a + 1)
            if b < 0:
                break
            lk = _unescape(body[a + 1:b])
            c = body.find(":", b)
            v1 = body.find('"', c)
            v2 = _closing_quote(body, v1 + 1)
            if c < 0 or v1 < 0 or v2 < 0:
                break
            sub[lk] = _unescape(body[v1 + 1:v2])
            p = v2 + 1
        result[key] = sub
        pos = be + 1
    return result


def verify(d):
    """The mod's own parser must read back exactly what json wrote."""
    text = serialize(d).decode("utf-8")
    if re.search(r"\\[bfu]", text):
        sys.exit("output contains a \\b, \\f or \\u escape, which the mod's JSON parser cannot read")
    if parse_like_mod(text) != d:
        sys.exit("the mod's parser would read this file differently from json; not writing")


def cmd_apply(batch_path, dry):
    d = load()
    with open(batch_path, encoding="utf-8") as f:
        batch = json.load(f)
    added, changed, problems = [], [], []
    for key, langs in batch.items():
        extra = set(langs) - set(LANGS)
        if extra:
            problems.append(f"{key!r}: unknown languages {sorted(extra)}")
        if _UNREACHABLE_TAIL.search(key):
            problems.append(f"{key!r}: ends in digits/circled digits, which the lookup strips first")
        for s in [key] + list(langs.values()):
            if any(ord(ch) < 0x20 and ch != "\n" for ch in s):
                problems.append(f"{key!r}: control character other than \\n")
        if key in d:
            for l in LANGS:
                v = langs.get(l, "")
                if v and d[key].get(l) != v:
                    changed.append((key, l, d[key].get(l), v))
                    d[key][l] = v
        else:
            missing = [l for l in LANGS if not langs.get(l)]
            if missing:
                problems.append(f"{key!r}: new key missing {missing}")
                continue
            d[key] = {l: langs[l] for l in LANGS}
            added.append(key)
    if problems:
        print("PROBLEMS (nothing written):")
        for p in problems:
            print("  " + p)
        sys.exit(1)
    verify(d)
    print(f"{len(added)} new keys, {len(changed)} changed values ({len(d)} keys total)")
    for key, l, old, new in changed:
        print(f"  ~ {key!r} [{l}] {old!r} -> {new!r}")
    for key in added:
        print(f"  + {key!r} = {d[key]['en']!r}")
    if dry:
        print("dry run: translation.json not written")
        return
    with open(PATH, "wb") as f:
        f.write(serialize(d))
    print(f"wrote {PATH}")


def cmd_check_official(gd_path):
    d = load()
    with open(gd_path, encoding="utf-8") as f:
        gd = json.load(f)
    n = 0
    for key, v in d.items():
        off = gd.get(key)
        if not off:
            continue
        diffs = {l: (v.get(l), off.get(l)) for l in LANGS if off.get(l) and v.get(l) != off.get(l)}
        if diffs:
            n += 1
            print(key)
            for l, (cur, o) in diffs.items():
                print(f"  {l:4} current={cur!r}  official={o!r}")
    print(f"{n} keys differ from an exact official string")


def cmd_status():
    d = load()
    incomplete = [k for k, v in d.items() if any(not v.get(l) for l in LANGS)]
    print(f"{len(d)} keys; {len(incomplete)} missing a language: {incomplete[:20]}")
    verify(d)
    print("the mod's parser reads the file identically")


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    cmd = args[0] if args else "status"
    if cmd == "apply" and len(args) > 1:
        cmd_apply(args[1], "--dry" in sys.argv)
    elif cmd == "check-official" and len(args) > 1:
        cmd_check_official(args[1])
    elif cmd == "status":
        cmd_status()
    else:
        print(__doc__)
        sys.exit(1)
