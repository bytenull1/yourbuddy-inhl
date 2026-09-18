#!/usr/bin/env python3
"""Trim a BepInEx log down to something worth pasting into a session.

A raw capture is mostly overhead: every line carries `[Info   :YourBuddy Mod] `
(22 chars of nothing) and the interesting lines repeat with only the coordinates
changing. This strips the boilerplate and collapses repetition, WITHOUT throwing away
the numbers -- coordinates and node ids are usually the actual evidence, so a run
is folded to its first and last occurrences, never to a single summary.

--stats is usually the first thing to look at: it answers "what is this log mostly
made of" in a few dozen lines. --fold then gives you a readable body at ~1/4 size.

Usage
    python tools/trimlog.py LogOutput.log                 # strip + exact dedupe
    python tools/trimlog.py LogOutput.log --fold          # also fold near-identical runs
    python tools/trimlog.py LogOutput.log --stats         # shape histogram, no body
    python tools/trimlog.py LogOutput.log --tag nav,ai    # only these subsystems
    python tools/trimlog.py LogOutput.log --all           # keep non-YourBuddy lines too
    ... | python tools/trimlog.py -                       # or read stdin

Write the result next to the repo and hand the agent that file, not the raw log.
"""

import argparse
import re
import sys

# BepInEx line prefix: "[Info   :YourBuddy Mod] ", "[Warning:   BepInEx] ", ...
BEPINEX = re.compile(r'^\[(?P<level>\w+)\s*:\s*(?P<source>[^\]]+)\]\s?')
# Our own subsystem tag, e.g. "[nav] " (and the legacy long form).
TAG = re.compile(r'^\[(?P<tag>[A-Za-z]+)\]\s?')
# Anything that varies run to run: numbers (incl. comma decimals) and #ids.
VARIABLE = re.compile(r'#\d+|-?\d+[.,]\d+|-?\d+')

LEVEL_MARK = {'Warning': '! ', 'Error': 'E ', 'Fatal': 'E '}


def shape(text):
    """Line with every number replaced, so near-identical lines compare equal."""
    return VARIABLE.sub('N', text)


def parse(line):
    """-> (keep, level, tag, text) with the BepInEx prefix removed."""
    line = line.rstrip('\n').rstrip()
    m = BEPINEX.match(line)
    if not m:
        return False, '', '', line
    level = m.group('level')
    source = m.group('source').strip()
    text = line[m.end():]
    tag = ''
    t = TAG.match(text)
    if t:
        tag = t.group('tag')
        text = text[t.end():]
    return source.startswith('YourBuddy'), level, tag, text


def emit(out, level, tag, text):
    out.append('%s%s%s' % (LEVEL_MARK.get(level, ''), '[%s] ' % tag if tag else '', text))


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('path', help='log file, or - for stdin')
    ap.add_argument('--tag', help='comma-separated subsystem tags to keep (nav, ai, probe, mgr, editor, ...)')
    ap.add_argument('--fold', action='store_true',
                    help='keep only the first and last few occurrences of each repeated line shape')
    ap.add_argument('--keep', type=int, default=2, metavar='N',
                    help='with --fold, how many of each shape to keep at each end (default 2)')
    ap.add_argument('--stats', action='store_true',
                    help='print a shape histogram instead of the log body')
    ap.add_argument('--all', action='store_true', help='keep lines from other sources too')
    args = ap.parse_args()

    stream = sys.stdin if args.path == '-' else open(args.path, 'r', encoding='utf-8', errors='replace')
    wanted = {t.strip() for t in args.tag.split(',')} if args.tag else None

    rows = []
    for raw in stream:
        if not raw.strip():
            continue
        mine, level, tag, text = parse(raw)
        if not mine and not args.all:
            # Lines the mod did not write, and free-text notes the user added to a
            # capture by hand -- those are evidence, so keep them.
            if BEPINEX.match(raw.strip()):
                continue
            level, tag, text = '', '', raw.rstrip('\n').rstrip()
        if wanted is not None and tag and tag not in wanted:
            continue
        rows.append((level, tag, text))
    if stream is not sys.stdin:
        stream.close()

    if args.stats:
        counts = {}
        for level, tag, text in rows:
            key = ('[%s] ' % tag if tag else '') + shape(text)
            counts[key] = counts.get(key, 0) + 1
        for key, n in sorted(counts.items(), key=lambda kv: -kv[1]):
            print('%6d  %s' % (n, key))
        print('\n%d lines, %d distinct shapes' % (len(rows), len(counts)))
        return

    out = []
    n_in = len(rows)

    if args.fold:
        # Sample by SHAPE rather than by run length. Real captures interleave - a
        # gate-frame note or a stuck warning lands in the middle of a replan cycle -
        # so exact repeating-block detection almost never fires on them, while the
        # same handful of shapes still accounts for most of the file.
        total = {}
        for _level, tag, text in rows:
            key = (tag, shape(text))
            total[key] = total.get(key, 0) + 1
        seen = {}
        dropped = 0
        for level, tag, text in rows:
            key = (tag, shape(text))
            n = seen[key] = seen.get(key, 0) + 1
            # First N and last N of each shape: the drift between them is often the
            # finding (a position that never changes means a livelock).
            if n <= args.keep or n > total[key] - args.keep:
                if dropped:
                    out.append('    ... %d lines of shapes already shown ...' % dropped)
                    dropped = 0
                emit(out, level, tag, text)
            else:
                dropped += 1
        if dropped:
            out.append('    ... %d lines of shapes already shown ...' % dropped)
    else:
        i = 0
        while i < len(rows):
            reps = 1
            while i + reps < len(rows) and rows[i + reps][1:] == rows[i][1:]:
                reps += 1
            emit(out, *rows[i])
            if reps > 1:
                out.append('    ... x%d identical ...' % (reps - 1))
            i += reps

    sys.stdout.write('\n'.join(out) + '\n')
    sys.stderr.write('trimlog: %d lines -> %d\n' % (n_in, len(out)))


if __name__ == '__main__':
    main()
