"""
FF Pixel Remaster offline entity-label extractor, FF1 edition (also still runs FF2-FF5).

Copied from the shared FF2-FF5 tool (FF5 mod, tools/extract_entities.py) and extended with a
faithful mirror of FF1's own Utils/EntityTranslator.cs lookup. Reads every map_*.bundle under
the game's Addressables tree, walks the Tiled-format entity data (entity_default TextAssets
+ the entity[] assets listed in each map's `package` manifest), and collects the raw Japanese
developer labels that the mod speaks at runtime (EntityTranslator.Translate on
PropertyEntity.Name, which is the Tiled object's `name`).

  * GAME selects the install path and, more importantly, the Python mirror of THAT mod's
    EntityTranslator lookup. Each mod strips prefixes/suffixes differently, so "is this
    label already covered?" can only be answered per game.
  * `missing` reports only the labels the mod would actually fail to translate. For FF1 the
    reported key is the name the mod itself logs as untranslated (prefix and enumeration
    stripped, parentheses kept), so ①/②/1./12: variants collapse into one entry.
  * `gamedict` builds a Japanese -> {lang: text} dictionary from the game's own message
    tables (story_cha = speaker names, system = names of characters/places/items). A
    label that exactly matches a game string is listed with the official localisation;
    partial matches are listed as glossary hints for whoever translates the rest.

Usage:
  python extract_entities.py selftest          # check the FF1 lookup mirror against known cases
  python extract_entities.py sample [N]        # tally + Japanese labels per object type, N bundles (default 3; 0 = all)
  python extract_entities.py full <outfile>    # every label, merged into <outfile>
  python extract_entities.py missing <outfile> # labels the current translation.json misses
  python extract_entities.py gamedict <outfile># official JP -> {lang} dictionary

Environment:
  FFPR_GAME     overrides GAME below (FF1 / FF2 / FF3 / FF4 / FF5)
  FFPR_AA_PATH  overrides the Addressables folder
"""
import glob, os, json, base64, sys, re
from collections import Counter, defaultdict

GAME = os.environ.get("FFPR_GAME", "FF1")

STEAM = r"D:\Games\SteamLibrary\steamapps\common"
INSTALL = {
    "FF1": (r"FINAL FANTASY PR", r"FINAL FANTASY_Data"),
    "FF2": (r"FINAL FANTASY II PR", r"FINAL FANTASY II_Data"),
    "FF3": (r"FINAL FANTASY III PR", r"FINAL FANTASY III_Data"),
    "FF4": (r"FINAL FANTASY IV PR", r"FINAL FANTASY IV_Data"),
    "FF5": (r"FINAL FANTASY V PR", r"FINAL FANTASY V_Data"),
}
AA = os.environ.get(
    "FFPR_AA_PATH",
    os.path.join(STEAM, INSTALL[GAME][0], INSTALL[GAME][1], r"StreamingAssets\aa\StandaloneWindows64"),
)

# The mod repo this script lives in (tools/..).
MOD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# MapConstants.ObjectType - identical numbering in FF1-FF5 (FF3 adds 27, FF5 adds 27-28)
OBJ_TYPE_NAME = {
    0: "PointIn", 1: "TileAnimation", 2: "Event", 3: "GotoMap", 4: "ToLayer",
    5: "Entity", 6: "NPC", 7: "TreasureBox", 8: "OpenTrigger", 9: "SavePoint",
    10: "AnimEntity", 11: "EffectEntity", 12: "ScreenEffect", 13: "MoveArea",
    14: "Polyline", 15: "ChangeOffset", 16: "RelayInteraction", 17: "ShopNPC",
    18: "MinimapIcon", 19: "CollisionEntity", 20: "TelepoPoint",
    21: "TransportationEventAction", 22: "IgnoreRoute", 23: "NonEncountArea",
    24: "TimeSwitchingGimmickArea", 25: "SlidingFloorGimmickArea",
    26: "DamageFloorGimmickArea", 27: "MapRange", 28: "RandomEvent",
}

# FF2-FF5: never named to the player by any of those mods: non-interactive geometry/effects,
# plus TreasureBox and SavePoint (mod-constant names). GotoMap is resolved from the Map/Area
# master at runtime in every mod, so its dev label is never spoken.
EXCLUDE_TYPES_SHARED = {0, 1, 3, 7, 8, 9, 11, 12, 13, 14, 15, 19, 22, 23, 24, 25, 26, 27}

# FF1: derived from FF1's detector chain (Field/EntityScanner.cs + Field/EntityDetectors/*)
# and the FieldEntity class hierarchy in ff1/dump.cs. Labels that reach
# EntityTranslator.Translate(PropertyEntity.Name): EventTriggerEntity (Event 2),
# SwitchLayerEventEntity (ToLayer 4), FieldMapObjectDefault (Entity 5, AnimEntity 10; skipped
# at runtime when CanAction is false, which cannot be seen offline, so they are kept),
# FieldNonPlayer (NPC 6, ShopNPC 17), PropertyTelepoPoint (TelepoPoint 20), and 21/28 whose
# class the dump does not pin down. Excluded, because FF1 never speaks the label:
#   GotoMap 3        named "Exit to <map>" from the destination map id
#   TreasureBox 7    named from the chest contents
#   OpenTrigger 8    FieldOpenTriggerEntity: plain FieldEntity, and VisualEffectFilter drops
#                    the "OpenTrigger" prefab name
#   SavePoint 9      SavePointDetector names it "Save Point" (prefab "SavePoint")
#   RelayInteraction 16, MinimapIcon 18
#                    plain FieldEntity subclasses (not IInteractiveEntity, not event
#                    triggers), so no detector claims them
#   PointIn 0, TileAnimation 1, EffectEntity 11, ScreenEffect 12, MoveArea 13, Polyline 14,
#   ChangeOffset 15, CollisionEntity 19, 22-27
#                    spawn points, effects, collision and area geometry (VisualEffectFilter
#                    drops the PointIn / TileAnimRegion / FieldEffect* / FieldScreenEffect
#                    prefab names; the rest have no detector)
EXCLUDE_TYPES_FF1 = EXCLUDE_TYPES_SHARED | {16, 18}

EXCLUDE_TYPES = EXCLUDE_TYPES_FF1 if GAME == "FF1" else EXCLUDE_TYPES_SHARED

# LanguageCodeMap minus 'ja' (identity).
LANGS = ["en", "fr", "it", "de", "es", "ko", "zht", "zhc", "ru", "th", "pt"]
ALL_LANGS = ["ja"] + LANGS


def contains_japanese(text):
    """Mirror of EntityTranslator.ContainsJapanese (hiragana, katakana, CJK)."""
    for c in text:
        o = ord(c)
        if (0x3040 <= o <= 0x309F) or (0x30A0 <= o <= 0x30FF) or (0x4E00 <= o <= 0x9FFF):
            return True
    return False


def normalize_halfwidth(s):
    out = []
    for c in s:
        o = ord(c)
        if 0xFF01 <= o <= 0xFF5E:
            out.append(chr(o - 0xFEE0))
        elif o == 0x3000:
            out.append(" ")
        else:
            out.append(c)
    return "".join(out)


def is_placeholder_shared(name):
    """FF2-FF5: labels no mod ever speaks (superset of the per-mod placeholder filters)."""
    if not name or name == "Unknown":
        return True
    norm = normalize_halfwidth(name)
    if norm.startswith("Default ") or norm.startswith("Default_"):
        return True
    if name.startswith("汎用"):
        return True
    if "セーブポイント" in name:
        return True
    return False


# FF1 VisualEffectFilter: GameObject-name words that drop an entity before any detector runs
# (except for event triggers). The GameObject name is not in the bundle data (it is the
# prefab name, e.g. "PointIn", or the label), so the label stands in for it: this only drops
# labels that themselves contain one of the words. PlayerFilter drops "player" for every type.
_FF1_EFFECT_WORDS = ("residentchara", "resident", "fieldeffect", "scrolldummy", "scroll",
                     "tileanim", "pointin", "opentrigger")


def is_placeholder_ff1(name, ot=None):
    """Mirror of the name checks FF1 applies before a label reaches EntityTranslator:
    EntityDetectionHelpers.GetEntityNameFromProperty (blank, event/eventtrigger/pointin,
    mes_/sys_/field_ message ids resolved by the game itself), PlayerFilter and
    VisualEffectFilter. FF1 has NO 汎用/Default/セーブポイント filter: those labels are spoken."""
    if not name or not name.strip():
        return True
    low = name.lower()
    if low in ("event", "eventtrigger", "event trigger", "pointin"):
        return True
    if low.startswith(("mes_", "sys_", "field_")):
        return True
    if "player" in low:
        return True
    if "resident" in low:  # residentchara / resident: skipped even for event triggers
        return True
    if ot != 2:  # the effect words below are ignored for EventTriggerEntity
        if any(w in low for w in _FF1_EFFECT_WORDS) or ("effect" in low and "object" not in low):
            return True
    return False


def is_placeholder(name, ot=None):
    return is_placeholder_ff1(name, ot) if GAME == "FF1" else is_placeholder_shared(name)


# ─────────────────────────────────────────────────────────────────────────────
#  Per-game mirrors of Utils/EntityTranslator.cs Translate().
#  Each returns the ordered list of keys the mod tries (first hit wins).
# ─────────────────────────────────────────────────────────────────────────────
CIRCLED = "①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳"
_PREFIX_SIMPLE = re.compile(r"^((?:SC)?\d+:)", re.IGNORECASE)          # FF3 / FF4 / FF5
_PREFIX_FF2 = re.compile(r"^((?:SC\s*E?\s*)?\d+:)", re.IGNORECASE)      # FF2
_TRAIL_DIGITS = re.compile(r"([0-9０-９]+)$")                            # FF2


def _strip(rx, s):
    m = rx.match(s)
    return (m.group(1), s[len(m.group(1)):]) if m else (None, s)


def _strip_digits(s):
    m = _TRAIL_DIGITS.search(s)
    return (m.group(1), s[: len(s) - len(m.group(1))]) if m else (None, s)


# ── FF1 ──────────────────────────────────────────────────────────────────────
# Regexes copied from FF1 Utils/EntityTranslator.cs. Python's \d on str patterns matches
# every Unicode decimal digit and `$` also matches before a final "\n", exactly as .NET's
# default (non-ECMAScript, non-Multiline) Regex does, so the patterns carry over verbatim.
_FF1_PREFIX = re.compile(r"^((?:SC)?\d+[.:])", re.IGNORECASE)   # EntityPrefixRegex
_FF1_PAREN_SUFFIX = re.compile(r"[(\uff08][^)\uff09]*[)\uff09]$")  # ParenSuffixRegex
_FF1_LEAD_ENUM = re.compile(r"^[\u2460-\u2473]+")               # LeadingEnumPrefixRegex
_FF1_LEAD_DIGITS = re.compile(r"^\d+(?![\d.:])")                # LeadingDigitPrefixRegex
_FF1_TRAIL_ENUM = re.compile(r"[\u2460-\u2473]+$")              # TrailingEnumSuffixRegex
_FF1_TRAIL_DIGITS = re.compile(r"\d+$")                         # TrailingDigitSuffixRegex


def _ff1_circled_to_number(s):
    return " ".join(str(ord(c) - 0x2460 + 1) for c in s if 0x2460 <= ord(c) <= 0x2473)


def ff1_translate(name, lookup):
    """Step-for-step mirror of FF1 EntityTranslator.Translate (non-Japanese game language).

    lookup(key) -> str | None stands in for TryLookup (current language, then English).
    Returns (spoken_text, tried_keys, hit_key, tracking_name, tier). hit_key is None on a
    miss, tracking_name is what the mod records as untranslated for the current map, and
    tier names the step that hit ("pre-strip", "exact", "parens", "prefix", "paren-suffix")."""
    tried = []

    def attempt(key):
        tried.append(key)
        return lookup(key)

    if not name:
        return name, tried, None, None, None

    # Trailing enumeration suffix ("①") or, failing that, trailing plain digits ("コウモリ1").
    enum_suffix, core = "", name
    m = _FF1_TRAIL_ENUM.search(name)
    if m:
        enum_suffix = " " + m.group(0)
        core = name[: m.start()]
    if not enum_suffix:
        m = _FF1_TRAIL_DIGITS.search(core)
        if m and m.start() > 0:
            enum_suffix = " " + m.group(0)
            core = core[: m.start()]

    # Exact lookup before the leading-prefix strip ("2Fへワープ").
    r = attempt(core)
    if r is not None:
        return r + enum_suffix, tried, core, None, "pre-strip"

    # Leading circled digits ("⑭村人（男性）") or plain digits ("15ルフェイン人").
    lead_suffix = ""
    m = _FF1_LEAD_ENUM.match(core)
    if m:
        lead_suffix = " " + _ff1_circled_to_number(m.group(0))
        core = core[m.end():]
    if not lead_suffix:
        m = _FF1_LEAD_DIGITS.match(core)
        if m and m.end() < len(core):
            lead_suffix = " " + m.group(0)
            core = core[m.end():]

    # 1. exact
    r = attempt(core)
    if r is not None:
        return r + lead_suffix + enum_suffix, tried, core, None, "exact"

    # 1b. full-width parens -> half-width
    normalized = core.replace("\uff08", "(").replace("\uff09", ")")
    if normalized != core:
        r = attempt(normalized)
        if r is not None:
            return r + lead_suffix + enum_suffix, tried, normalized, None, "parens"

    # 2. numeric / SC prefix ("6:", "12.", "SC01:"), kept in the output
    m = _FF1_PREFIX.match(core)
    prefix, base = (m.group(1), core[len(m.group(1)):]) if m else (None, core)
    if prefix is not None:
        r = attempt(base)
        if r is not None:
            return prefix + " " + r + lead_suffix + enum_suffix, tried, base, None, "prefix"

    # 3. parenthesised suffix ("兵士(e_v_0002専用)" -> "兵士")
    for_suffix = base if prefix is not None else core
    stripped = _FF1_PAREN_SUFFIX.sub("", for_suffix).strip()
    if stripped != for_suffix and stripped:
        r = attempt(stripped)
        if r is not None:
            text = (prefix + " " + r) if prefix is not None else r
            return text + lead_suffix + enum_suffix, tried, stripped, None, "paren-suffix"

    tracking = base if prefix is not None else core
    return name, tried, None, tracking, None


def candidates_ff1(name):
    return ff1_translate(name, lambda k: None)[1]


def candidates_ff2(name):
    keys = [name]
    prefix, after = _strip(_PREFIX_FF2, name)
    if prefix:
        keys.append(after)
    suffix, base = _strip_digits(name)
    if suffix:
        keys.append(base)
    if prefix:
        s2, b2 = _strip_digits(after)
        if s2:
            keys.append(b2)
    if name[:1] in CIRCLED:
        rest = name[1:]
        keys.append(rest)
        s3, b3 = _strip_digits(rest)
        if s3:
            keys.append(b3)
    if len(name) > 1 and name[0] == "真":
        rest = name[1:]
        keys.append(rest)
        s4, b4 = _strip_digits(rest)
        if s4:
            keys.append(b4)
    return keys


def candidates_ff3(name):
    keys = [name]
    prefix, after = _strip(_PREFIX_SIMPLE, name)
    if prefix:
        keys.append(after)
    return keys


def candidates_ff4_ff5(name):
    keys = [name]
    prefix, after = _strip(_PREFIX_SIMPLE, name)
    if prefix:
        keys.append(after)
    if name and name[-1] in CIRCLED:
        keys.append(name[:-1])
    if prefix and after and after[-1] in CIRCLED:
        keys.append(after[:-1])
    return keys


CANDIDATES = {"FF1": candidates_ff1, "FF2": candidates_ff2, "FF3": candidates_ff3,
              "FF4": candidates_ff4_ff5, "FF5": candidates_ff4_ff5}[GAME]


def proposed_key(name):
    """The key a new entry should use.
    FF1: the mod's own tracking name (prefix/enumeration stripped, parentheses kept as
    written), which the lookup always tries. Other games: the most reduced form tried.
    Never reduce to a string with no Japanese left (e.g. a bare prefix)."""
    if GAME == "FF1":
        tracking = ff1_translate(name, lambda k: None)[3]
        if tracking and contains_japanese(tracking):
            return tracking
        return name
    keys = [k for k in CANDIDATES(name) if k and contains_japanese(k)]
    return keys[-1] if keys else name


# ─────────────────────────────────────────────────────────────────────────────
#  Bundle walking (unchanged from the FF5 original; FF1 uses the same layout)
# ─────────────────────────────────────────────────────────────────────────────
def get_text(obj):
    data = obj.read()
    s = getattr(data, "m_Script", b"")
    if isinstance(s, str):
        return s.encode("utf-8", "surrogateescape").decode("utf-8", "replace")
    return bytes(s).decode("utf-8", "replace")


def object_type_of(tiled_obj):
    for p in tiled_obj.get("properties", []):
        if p.get("name") == "object_type":
            try:
                return int(p.get("value"))
            except (TypeError, ValueError):
                return None
    return None


def walk_entity_doc(doc, raw_counter, kept, bundle_tag, per_type=None):
    if not isinstance(doc, dict):
        return
    for layer in doc.get("layers", []):
        objs = layer.get("objects")
        if not isinstance(objs, list):
            continue
        for o in objs:
            if not isinstance(o, dict):
                continue
            name = o.get("name") or ""
            ot = object_type_of(o)
            raw_counter[ot] += 1
            if per_type is not None and name and contains_japanese(name):
                per_type[ot][name] += 1
            if ot is None or ot in EXCLUDE_TYPES:
                continue
            if not name or not contains_japanese(name):
                continue
            if is_placeholder(name, ot):
                continue
            kept.append((name, ot, bundle_tag))


def iter_entity_docs(bundle_path):
    import UnityPy
    env = UnityPy.load(bundle_path)
    text_by_name = defaultdict(list)
    package_txt = None
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        data = obj.read()
        nm = getattr(data, "m_Name", "")
        txt = get_text(obj)
        text_by_name[nm].append(txt)
        if nm == "package":
            package_txt = txt

    for txt in text_by_name.get("entity_default", []):
        try:
            yield json.loads(txt)
        except Exception:
            pass

    if package_txt:
        try:
            pkg = json.loads(package_txt)
        except Exception:
            pkg = None
        if pkg:
            seen = set()
            for m in pkg.get("map", []) or []:
                for elem in (m.get("entity") or []):
                    inline = elem.get("inline")
                    if inline:
                        try:
                            yield json.loads(base64.b64decode(inline).decode("utf-8"))
                        except Exception:
                            pass
                    else:
                        leaf = (elem.get("asset") or "").split("/")[-1]
                        if leaf in seen:  # same leaf in two sub-maps: already yielded all copies
                            continue
                        seen.add(leaf)
                        for txt in text_by_name.get(leaf, []):
                            try:
                                yield json.loads(txt)
                            except Exception:
                                pass


def bundle_tag(path):
    base = os.path.basename(path)
    return base.split("_assets_all_")[0]


def sweep(bundles, per_type=None):
    raw_counter = Counter()
    kept = []
    errors = []
    for i, b in enumerate(bundles, 1):
        try:
            for doc in iter_entity_docs(b):
                walk_entity_doc(doc, raw_counter, kept, bundle_tag(b), per_type)
        except Exception as e:
            errors.append((os.path.basename(b), str(e)))
        if i % 40 == 0:
            print(f"  ...{i}/{len(bundles)} bundles")
    return raw_counter, kept, errors


def all_map_bundles():
    return sorted(glob.glob(os.path.join(AA, "map_*_assets_all_*.bundle")))


# ─────────────────────────────────────────────────────────────────────────────
#  Game message tables -> official localisation
# ─────────────────────────────────────────────────────────────────────────────
_MARKUP = re.compile(r"<[^>]*>")


def clean_msg(s):
    return _MARKUP.sub("", s).replace("\\n", " ").strip()


def load_message_tables():
    """{table: {lang: {msg_id: text}}} for every localised table in the message bundle."""
    import UnityPy
    b = glob.glob(os.path.join(AA, "message_assets_all_*.bundle"))
    if not b:
        return {}
    env = UnityPy.load(b[0])
    tables = defaultdict(dict)
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        nm = obj.read().m_Name
        m = re.match(r"^(.*)_(ja|en|fr|it|de|es|ko|zht|zhc|ru|th|pt)$", nm)
        if not m:
            continue
        table, lang = m.group(1), m.group(2)
        rows = {}
        for line in get_text(obj).splitlines():
            if "\t" not in line:
                continue
            k, v = line.split("\t", 1)
            rows[k] = v
        tables[table][lang] = rows
    return tables


def build_gamedict(tables):
    """Japanese text -> {lang: official text}. Only whole strings; conflicting
    localisations for the same Japanese text keep the most common one."""
    votes = defaultdict(lambda: defaultdict(Counter))
    for table in ("story_cha", "system"):
        langs = tables.get(table, {})
        ja = langs.get("ja", {})
        for mid, jtext in ja.items():
            jt = clean_msg(jtext)
            if not jt or not contains_japanese(jt) or len(jt) > 40:
                continue
            for lang in LANGS:
                t = clean_msg(langs.get(lang, {}).get(mid, ""))
                if t:
                    votes[jt][lang][t] += 1
    out = {}
    for jt, per_lang in votes.items():
        entry = {}
        for lang in LANGS:
            if per_lang.get(lang):
                entry[lang] = per_lang[lang].most_common(1)[0][0]
        if entry.get("en"):
            out[jt] = entry
    return out


# ─────────────────────────────────────────────────────────────────────────────
#  Commands
# ─────────────────────────────────────────────────────────────────────────────
def load_translation():
    p = os.path.join(MOD, "translation.json")
    with open(p, encoding="utf-8-sig") as f:
        return json.load(f)


def has_entry(d, key):
    """FF1 Initialize() keeps an entry with any non-empty value; TryLookup then needs the
    current language or English. English is the fallback every language reaches."""
    v = d.get(key)
    return bool(v) and bool(v.get("en"))


def cmd_selftest():
    """The FF1 mirror against hand-traced EntityTranslator.Translate results."""
    tr = {"村人(男性)": "Male Villager", "コウモリ": "Bat", "ルフェイン人": "Lufenian",
          "2Fへワープ": "Warp to 2F", "兵士": "Soldier", "ウネ": "Unne",
          "村人（おじいさん）": None}
    look = lambda k: tr.get(k)
    cases = [
        ("村人(男性)①", "Male Villager ①"),        # trailing circled digit reappended
        ("⑭村人（男性）", "Male Villager 14"),       # leading circled -> number, parens normalised
        ("12:村人(男性)", "12: Male Villager"),      # numeric prefix kept, not eaten by lead digits
        ("11.コウモリ", "11. Bat"),                  # dotted prefix
        ("コウモリ3", "Bat 3"),                      # trailing digits
        ("15ルフェイン人", "Lufenian 15"),           # leading digits
        ("2Fへワープ", "Warp to 2F"),                # exact before leading-digit strip
        ("兵士(e_v_0002専用)", "Soldier"),           # paren suffix
        ("SC01:兵士（何か）", "SC01: Soldier"),      # SC prefix + full-width paren suffix
        ("②ウネ", "Unne 2"),
        ("PC1.ビックリマーク", "PC1.ビックリマーク"),  # "PC" is not a recognised prefix
        ("7", "7"),                                  # pure digits: untouched
    ]
    bad = 0
    for raw, want in cases:
        got, tried, hit, tracking, tier = ff1_translate(raw, look)
        ok = got == want
        bad += not ok
        print(f"{'ok ' if ok else 'BAD'} {raw!r} -> {got!r} (want {want!r}) tried={tried} tracking={tracking!r}")
    print("selftest:", "PASS" if not bad else f"{bad} FAILED")
    return bad


def cmd_sample(n):
    bundles = all_map_bundles()
    real = [b for b in bundles if "nighteffect" not in os.path.basename(b)]
    picks = real if n == 0 else (real[:n] if real else bundles[:n])
    per_type = defaultdict(Counter)
    raw_counter, kept, errors = sweep(picks, per_type)
    print(f"{GAME}: {len(picks)} bundles from {AA}")
    print("\n=== raw object_type histogram ===")
    for ot, cnt in sorted(raw_counter.items(), key=lambda kv: -kv[1]):
        excl = "  [EXCLUDED]" if ot in EXCLUDE_TYPES else ""
        print(f"  {OBJ_TYPE_NAME.get(ot, f'?{ot}'):<26} ({ot}): {cnt}{excl}")
    print("\n=== Japanese labels per object_type (first 12) ===")
    for ot in sorted(per_type, key=lambda x: (x is None, x)):
        names = per_type[ot]
        excl = " [EXCLUDED]" if ot in EXCLUDE_TYPES else ""
        print(f"  {OBJ_TYPE_NAME.get(ot, f'?{ot}')} ({ot}){excl}: {len(names)} unique: "
              + ", ".join(repr(k) for k in list(names)[:12]))
    print(f"\n=== kept: {len(kept)} raw, {len({k[0] for k in kept})} unique ===")
    if errors:
        print(f"{len(errors)} bundle errors, first: {errors[:3]}")


def cmd_full(outfile):
    raw_counter, kept, errors = sweep(all_map_bundles())
    names = sorted({k[0] for k in kept})
    existing = {}
    if os.path.exists(outfile):
        with open(outfile, encoding="utf-8") as f:
            existing = json.load(f)
    merged = dict(existing)
    for n in names:
        merged.setdefault(n, {l: "" for l in LANGS})
    with open(outfile, "w", encoding="utf-8") as f:
        json.dump(merged, f, ensure_ascii=False, indent=2, sort_keys=True)
    print(f"{len(names)} unique labels; wrote {outfile} ({len(merged)} keys)")
    if errors:
        print(f"{len(errors)} bundle errors, first: {errors[:3]}")


def cmd_missing(outfile):
    bundles = all_map_bundles()
    print(f"{GAME}: sweeping {len(bundles)} map bundles under {AA}")
    raw_counter, kept, errors = sweep(bundles)
    tr = load_translation()

    labels = defaultdict(lambda: {"types": set(), "maps": set()})
    for name, ot, tag in kept:
        labels[name]["types"].add(OBJ_TYPE_NAME.get(ot, str(ot)))
        labels[name]["maps"].add(tag)

    hit, miss = 0, {}
    via_paren = {}   # FF1: covered only by the paren-suffix strip (meaning may be lost)
    for name, info in labels.items():
        if GAME == "FF1":
            spoken, tried, hit_key, _, tier = ff1_translate(
                name, lambda k: tr[k]["en"] if has_entry(tr, k) else None)
            if hit_key is not None:
                hit += 1
                if tier == "paren-suffix":
                    via_paren[name] = {"key": hit_key, "en": spoken,
                                       "maps": sorted(info["maps"])}
                continue
        elif any(has_entry(tr, k) for k in CANDIDATES(name)):
            hit += 1
            continue
        key = proposed_key(name)
        slot = miss.setdefault(key, {"raw": set(), "types": set(), "maps": set()})
        slot["raw"].add(name)
        slot["types"] |= info["types"]
        slot["maps"] |= info["maps"]

    tables = load_message_tables()
    gd = build_gamedict(tables)
    terms = sorted((t for t in gd if len(t) >= 2), key=len, reverse=True)

    out = {}
    official = 0
    for key in sorted(miss):
        slot = miss[key]
        rec = {
            "raw": sorted(slot["raw"]),
            "types": sorted(slot["types"]),
            "maps": sorted(slot["maps"]),
        }
        if key in gd:
            rec["official"] = gd[key]
            official += 1
        else:
            hints, covered = {}, key
            for t in terms:
                if t in covered:
                    hints[t] = gd[t]
                    covered = covered.replace(t, "\0")
            if hints:
                rec["glossary"] = hints
        out[key] = rec

    with open(outfile, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    if GAME == "FF1":
        side = os.path.splitext(outfile)[0] + ".paren_fallback.json"
        with open(side, "w", encoding="utf-8") as f:
            json.dump(dict(sorted(via_paren.items())), f, ensure_ascii=False, indent=2)
        print(f"{len(via_paren)} labels covered only by the paren-suffix fallback -> {side}")

    print(f"unique raw labels: {len(labels)}  covered by translation.json: {hit}")
    print(f"missing keys: {len(out)}  (exact official match: {official}, "
          f"with glossary hints: {sum(1 for r in out.values() if 'glossary' in r)})")
    by_type = Counter(t for r in out.values() for t in r["types"])
    print("missing by type:", dict(by_type))
    if errors:
        print(f"{len(errors)} bundle errors, first: {errors[:3]}")
    print(f"wrote {outfile}")


def cmd_gamedict(outfile):
    gd = build_gamedict(load_message_tables())
    with open(outfile, "w", encoding="utf-8") as f:
        json.dump(gd, f, ensure_ascii=False, indent=2, sort_keys=True)
    print(f"{len(gd)} official strings -> {outfile}")


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "selftest":
        sys.exit(1 if cmd_selftest() else 0)
    elif cmd == "sample":
        cmd_sample(int(sys.argv[2]) if len(sys.argv) > 2 else 3)
    elif cmd in ("full", "missing", "gamedict") and len(sys.argv) > 2:
        {"full": cmd_full, "missing": cmd_missing, "gamedict": cmd_gamedict}[cmd](sys.argv[2])
    else:
        print(__doc__)
        sys.exit(1)
