#!/usr/bin/env python3
"""Fail the build when a code comment contains Cyrillic characters.

User-facing strings stay Russian on purpose (the assignment requires it), so the
scanner strips string literals first and only inspects comment text.
"""

import pathlib
import re
import sys

CYRILLIC = re.compile("[Ѐ-ӿ]")
ROOTS = ("backend/src", "backend/tests", "frontend/src")
SUFFIXES = (".cs", ".ts", ".tsx")
SKIP_PARTS = {"bin", "obj", "node_modules", "dist"}


def comment_spans(text: str, suffix: str):
    """Yield (line_number, comment_text) pairs, skipping strings and chars."""
    i, line, n = 0, 1, len(text)
    while i < n:
        ch = text[i]
        if ch == "\n":
            line += 1
            i += 1
        elif text.startswith("//", i):
            end = text.find("\n", i)
            end = n if end < 0 else end
            yield line, text[i:end]
            i = end
        elif text.startswith("/*", i):
            end = text.find("*/", i + 2)
            end = n if end < 0 else end + 2
            chunk = text[i:end]
            yield line, chunk
            line += chunk.count("\n")
            i = end
        elif suffix == ".cs" and text.startswith('@"', i):
            i = skip_verbatim(text, i)
        elif ch in "\"'`":
            i, crossed = skip_quoted(text, i, ch)
            line += crossed
        else:
            i += 1


def skip_verbatim(text: str, i: int) -> int:
    """Skip a C# verbatim string; "" is an escaped quote inside it."""
    j = i + 2
    while j < len(text):
        if text[j] == '"':
            if text.startswith('""', j):
                j += 2
                continue
            return j + 1
        j += 1
    return len(text)


def skip_quoted(text: str, i: int, quote: str):
    """Skip a regular quoted literal, returning the new index and newlines crossed."""
    j, newlines = i + 1, 0
    while j < len(text):
        c = text[j]
        if c == "\\":
            j += 2
            continue
        if c == "\n":
            newlines += 1
            # Only backticks legitimately span lines; bail out on unterminated ones
            # so a stray quote cannot swallow the rest of the file.
            if quote != "`":
                return j, newlines
        elif c == quote:
            return j + 1, newlines
        j += 1
    return len(text), newlines


def main() -> int:
    root = pathlib.Path(__file__).resolve().parent.parent
    offenders = []
    for rel in ROOTS:
        for path in sorted((root / rel).rglob("*")):
            if path.suffix not in SUFFIXES or SKIP_PARTS & set(path.parts):
                continue
            text = path.read_text(encoding="utf-8")
            for line, comment in comment_spans(text, path.suffix):
                for offset, chunk in enumerate(comment.split("\n")):
                    if CYRILLIC.search(chunk):
                        rel_path = path.relative_to(root).as_posix()
                        offenders.append(f"{rel_path}:{line + offset}: {chunk.strip()}")

    if offenders:
        print("Cyrillic found in code comments (comments must be English):")
        for offender in offenders:
            print(f"  {offender}")
        return 1

    print("No Cyrillic in code comments.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
