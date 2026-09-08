#!/usr/bin/env python
"""
Development helper: write several files at once from a single stdin stream.

Usage:
    python infra/scripts/writefiles.py <<'EOF'
    @@FILE relative/path/One.cs
    ...file content...
    @@FILE relative/path/Two.cs
    ...file content...
    EOF

Files are written UTF-8 with LF endings, relative to the repository root.
This exists only to keep large scaffolding steps to a single shell invocation;
it is not part of the application.
"""
import io
import os
import sys

MARKER = "@@FILE "


def main() -> int:
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    data = sys.stdin.read()
    parts = data.split(MARKER)
    if len(parts) < 2:
        sys.stderr.write("writefiles: no @@FILE markers found on stdin\n")
        return 1

    written = 0
    for chunk in parts[1:]:
        newline = chunk.index("\n")
        rel = chunk[:newline].strip()
        content = chunk[newline + 1:]
        if content.endswith("\n\n"):
            content = content[:-1]
        path = os.path.join(root, rel)
        directory = os.path.dirname(path)
        if directory:
            os.makedirs(directory, exist_ok=True)
        io.open(path, "w", encoding="utf-8", newline="\n").write(content)
        print("wrote {0} ({1} lines)".format(rel, content.count("\n")))
        written += 1

    print("{0} file(s)".format(written))
    return 0


if __name__ == "__main__":
    sys.exit(main())
