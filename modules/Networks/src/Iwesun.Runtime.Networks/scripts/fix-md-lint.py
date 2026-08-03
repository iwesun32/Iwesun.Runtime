#!/usr/bin/env python3
"""
fix-md-lint.py — Auto-fix common markdownlint issues in Chinese design docs.

Usage:
    python fix-md-lint.py <path> [<path> ...]
    python fix-md-lint.py docs/                     # fix all docs in a dir
    python fix-md-lint.py docs/README.md            # fix single file

Fixes (in order):
  MD022  blanks around headings
  MD024  duplicate headings (adds parent section prefix)
  MD026  trailing punctuation in heading
  MD028  blank line inside blockquote
  MD031  blanks around fenced code blocks
  MD032  blanks around lists
  MD033  inline HTML like <T> / <TValue> / <TKey>
  MD037  spaces inside emphasis markers (──  arrows)
  MD038  spaces inside code span elements
  MD040  fenced code blocks without language specifier → 'text'
  MD046  code block style (split heading+fence on same line)
  MD060  table column alignment → wrap with markdownlint-disable/enable

All fixes are idempotent — run repeatedly, no-op on already-fixed files.
"""

import re
import sys
from pathlib import Path


# ── helpers ──────────────────────────────────────────────────────────

def read_file(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write_file(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8")


def glob_md(root: Path) -> list[Path]:
    if root.is_file():
        return [root] if root.suffix == ".md" else []
    return sorted(root.rglob("*.md"))


def collapse(text: str) -> str:
    """Collapse 3+ consecutive blank lines to exactly 2."""
    while "\n\n\n" in text:
        text = text.replace("\n\n\n", "\n\n")
    return text


# ── fix: MD040 + MD046 — fenced code language + heading/fence splits ─

def fix_fences(text: str) -> str:
    """
    1. `## 标题```csharp` → split into heading + fence on separate lines
    2. ````csharp### 标题` → split into fence + heading on separate lines
    3. Bare ``` → ```text
    """
    lines = text.split("\n")
    out = []
    in_fence = False
    fence_mark = None

    for line in lines:
        stripped = line.rstrip()

        # heading + fence on same line: "## 标题```csharp"
        hm = re.match(r"^(#{1,6}\s+.+?)(`{3,})(\S*)\s*$", stripped)
        if hm and not in_fence:
            out.append(hm.group(1).strip())
            lang = hm.group(3) or "text"
            out.append(f"{hm.group(2)}{lang}")
            in_fence = True
            fence_mark = hm.group(2)
            continue

        # fence + heading on same line: "```### 标题" or "```csharp### 标题"
        fm = re.match(r"^(`{3,})(\S*)\s*(#.+)$", stripped)
        if fm:
            fence = fm.group(1)
            lang = fm.group(2) or "text"
            heading = fm.group(3).strip()
            if in_fence:
                in_fence = False
                fence_mark = None
                out.append(fence)
                out.append(heading)
            else:
                out.append(f"{fence}{lang}")
                in_fence = True
                fence_mark = fence
            continue

        # normal fence
        nf = re.match(r"^(?P<mark>`{3,})(?:(\S+))?\s*$", stripped)
        if nf:
            mark = nf.group("mark")
            lang = nf.group(2) or ""
            if not in_fence:
                in_fence = True
                fence_mark = mark
                out.append(f"{mark}{lang or 'text'}")
            else:
                if mark.startswith(fence_mark[0]) and len(mark) >= len(fence_mark):
                    in_fence = False
                    fence_mark = None
                out.append(line)
        else:
            out.append(line)

    return "\n".join(out)


# ── fix: MD022 + MD031 — blanks around headings and fences ──────────

def fix_blanks(text: str) -> str:
    lines = text.split("\n")
    out = []
    i = 0
    in_fence = False

    while i < len(lines):
        line = lines[i]
        stripped = line.rstrip()

        nf = re.match(r"^(`{3,})(\S*)\s*$", stripped)

        if nf and not in_fence:
            if out and out[-1].strip() and not out[-1].strip().startswith("```"):
                out.append("")
            out.append(line)
            in_fence = True
            i += 1
            continue

        if nf and in_fence:
            out.append(line)
            in_fence = False
            i += 1
            if i < len(lines) and lines[i].strip():
                out.append("")
            continue

        if re.match(r"^#{1,6}\s+", stripped):
            if out and out[-1].strip() and not out[-1].strip().startswith("```"):
                out.append("")
            out.append(line)
            i += 1
            # blank after heading if next line is not blank and not heading
            if i < len(lines) and lines[i].strip() and not lines[i].strip().startswith("#"):
                next_blank = i < len(lines) and not lines[i].strip()
                if not next_blank:
                    out.append("")
            continue

        out.append(line)
        i += 1

    return collapse("\n".join(out))


# ── fix: MD032 — blanks around lists ────────────────────────────────

def fix_md032(text: str) -> str:
    lines = text.split("\n")
    out = []
    for i, line in enumerate(lines):
        stripped = line.strip()
        is_list = bool(re.match(r"^(\s*)([-*]|\d+\.)\s", line))
        is_heading = bool(re.match(r"^#{1,6}\s", stripped))

        if is_list:
            if out and out[-1].strip():
                prev = out[-1].strip()
                prev_is_list = bool(re.match(r"^(\s*)([-*]|\d+\.)\s", out[-1]))
                prev_is_fence = prev.startswith("```")
                if not prev_is_list and not is_heading and not prev_is_fence and "：" not in prev:
                    out.append("")
            out.append(line)
        else:
            if out:
                prev = out[-1].strip()
                prev_is_list = bool(re.match(r"^(\s*)([-*]|\d+\.)\s", out[-1]))
                if prev_is_list and (is_heading or stripped.startswith("```")):
                    out.append("")
            out.append(line)

    return collapse("\n".join(out))


# ── fix: MD028 — blank line inside blockquote ───────────────────────

def fix_md028(text: str) -> str:
    lines = text.split("\n")
    out = []
    in_bq = False
    for i, line in enumerate(lines):
        is_bq = line.startswith(">")
        is_empty = not line.strip()
        if is_bq:
            in_bq = True
            out.append(line)
        elif is_empty and in_bq:
            nxt = next((lines[j] for j in range(i + 1, len(lines)) if lines[j].strip()), None)
            out.append(">" if nxt and nxt.startswith(">") else line)
            if not (nxt and nxt.startswith(">")):
                in_bq = False
        else:
            if in_bq and line.strip():
                pass  # still in BQ — next non-bq line ends it
            in_bq = False
            out.append(line)
    return "\n".join(out)


# ── fix: MD033 — inline HTML <T> <TValue> <TKey> ───────────────────

def fix_md033(text: str) -> str:
    lines = text.split("\n")
    out = []
    for line in lines:
        if re.match(r"^#|^\s*[-*]|^\s*\d+\.", line):
            line = re.sub(r"(?<![`])\b<([Tt]\w*)>\b", r"`<\1>`", line)
        out.append(line)
    return "\n".join(out)


# ── fix: MD037 — spaces inside emphasis (──, →, ├, └, │, ▲, ▼) ─────

def fix_md037(text: str) -> str:
    lines = text.split("\n")
    out = []
    for line in lines:
        for ch in ["─", "→", "├", "└", "│", "▲", "▼"]:
            line = re.sub(f"_( ?{re.escape(ch)}+ ?)_", lambda m: m.group(1), line)
        out.append(line)
    return "\n".join(out)


# ── fix: MD038 — spaces inside code spans ───────────────────────────

def fix_md038(text: str) -> str:
    return re.sub(r"`(\s+)([^`]+?)(\s+)`", r"`\2`", text)


# ── fix: MD024 — duplicate headings ─────────────────────────────────

def fix_md024(text: str) -> str:
    lines = text.split("\n")
    counts: dict[str, int] = {}
    positions: dict[str, list[int]] = {}
    for i, line in enumerate(lines):
        m = re.match(r"^(#{3,6})\s+(.+?)(?:\s*\{#.*\})?\s*$", line.strip())
        if m and len(m.group(1)) >= 3:
            ht = m.group(2).strip().rstrip("#").strip()
            counts[ht] = counts.get(ht, 0) + 1
            positions.setdefault(ht, []).append(i)

    dups = {k for k, v in counts.items() if v > 1}
    if not dups:
        return text

    for ht, pos_list in positions.items():
        if ht not in dups:
            continue
        for idx in pos_list[1:]:
            parent = None
            for j in range(idx - 1, -1, -1):
                pm = re.match(r"^(#{1,6})\s+(.+)$", lines[j].strip())
                if pm:
                    pl = len(pm.group(1))
                    pt = pm.group(2).strip().rstrip("#").strip()
                    cur_lvl = len(re.match(r"^(#+)", lines[idx]).group(1))
                    if pl < cur_lvl:
                        parent = pt
                        break
            if parent:
                hm = re.match(r"^(#+)\s+", lines[idx])
                if hm:
                    lines[idx] = f"{hm.group(1)} {parent}：{ht}"
    return "\n".join(lines)


# ── fix: MD026 — trailing punctuation in heading ────────────────────

def fix_md026(text: str) -> str:
    lines = text.split("\n")
    out = []
    for line in lines:
        m = re.match(r"^(#{1,6}\s+.+?)([。：；，])\s*$", line.rstrip())
        out.append(m.group(1) if m else line)
    return "\n".join(out)


# ── fix: MD060 — table alignment wrappers ────────────────────────────

RE_TABLE = re.compile(r"^\|.+\|.*\|")

def fix_md060(text: str) -> str:
    """Wrap table sections with markdownlint-disable MD060 (idempotent)."""
    if "markdownlint-disable MD060" in text:
        return text  # already wrapped
    lines = text.split("\n")
    out = []
    disabled = False
    for line in lines:
        s = line.strip()
        is_table = bool(RE_TABLE.match(s))
        is_sep = bool(re.match(r"^\|[\s\-:]+\|", s)) and "-" in s
        if is_table and not disabled:
            out.append("<!-- markdownlint-disable MD060 -->")
            disabled = True
            out.append(line)
        elif disabled and is_table:
            out.append(line)
        elif disabled and s and not is_table:
            out.append("<!-- markdownlint-enable MD060 -->")
            disabled = False
            out.append(line)
        else:
            out.append(line)
    if disabled:
        out.append("<!-- markdownlint-enable MD060 -->")
    return "\n".join(out)


# ── main pipeline ───────────────────────────────────────────────────

ALL_FIXES = [
    ("MD040/MD046", "Fences: language + heading split", fix_fences),
    ("MD022/MD031", "Blanks around headings & fences", fix_blanks),
    ("MD033", "Inline HTML angle brackets", fix_md033),
    ("MD026", "Trailing punctuation in headings", fix_md026),
    ("MD028", "Blank line in blockquote", fix_md028),
    ("MD037", "Spaces in emphasis arrows", fix_md037),
    ("MD038", "Spaces in code spans", fix_md038),
    ("MD032", "Blanks around lists", fix_md032),
    ("MD024", "Duplicate headings", fix_md024),
    ("MD060", "Table alignment wrappers", fix_md060),
    ("cleanup", "Collapse extra blank lines", collapse),
]


def fix_file(path: Path, verbose: bool = True) -> bool:
    original = read_file(path)
    modified = original
    for code, name, func in ALL_FIXES:
        modified = func(modified)
    if modified != original:
        write_file(path, modified)
        if verbose:
            print(f"  ✓ {path.name}")
        return True
    return False


def main():
    args = sys.argv[1:]
    if not args:
        print("Usage: python fix-md-lint.py <file.md|directory> [<file.md|directory> ...]")
        sys.exit(1)
    files: list[Path] = []
    for arg in args:
        p = Path(arg)
        if p.exists():
            files.extend(glob_md(p))
        else:
            print(f"  ✗ not found: {arg}")
    if not files:
        print("No markdown files found.")
        return
    changed = 0
    for f in files:
        if fix_file(f):
            changed += 1
    print(f"\nDone. {changed}/{len(files)} files modified.")


if __name__ == "__main__":
    main()
