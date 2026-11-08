#!/usr/bin/env python3
# Claude Code UserPromptSubmit hook: appends every prompt sent to Claude, word for word, to one markdown file per session,
# in a subfolder named after the person (their git user.name, or their computer login if git has none).
# Usage (set in .claude/settings.json): python3 log_prompt.py <log folder>
# It never prints anything (a UserPromptSubmit hook's output would be added to the prompt) and never blocks a prompt.
# The same script is used in IMP-Front, IMP-Back, the IMP docs folder, and Luke's top/scripts. Keep the copies identical.
import datetime
import getpass
import json
import os
import re
import subprocess
import sys


def person(cwd):
    try:
        name = subprocess.run(['git', 'config', 'user.name'], cwd=cwd, capture_output=True, text=True, timeout=3).stdout.strip()
    except Exception:
        name = ''
    return name or getpass.getuser()


def fence(text):
    # A code fence longer than any run of backticks in the prompt, so the prompt can't close it early.
    longest = max((len(run) for run in re.findall(r'`+', text)), default=0)
    return '`' * max(3, longest + 1)


def main():
    event = json.load(sys.stdin)
    folder = sys.argv[1]
    session = event.get('session_id', 'unknown')
    cwd = event.get('cwd', os.getcwd())
    prompt = event.get('prompt', '')
    now = datetime.datetime.now().astimezone()
    who = person(cwd)
    slug = re.sub(r'[^a-z0-9]+', '-', who.lower()).strip('-') or 'unknown'
    folder = os.path.join(folder, slug)
    os.makedirs(folder, exist_ok=True)

    existing = [f for f in os.listdir(folder) if f.endswith(f'_{session[:8]}.md')]
    if existing:
        path = os.path.join(folder, existing[0])
    else:
        path = os.path.join(folder, f'{now:%Y-%m-%d_%H%M%S}_{session[:8]}.md')
        with open(path, 'w', encoding='utf-8') as f:
            f.write(f'# Claude session {now:%Y-%m-%d %H:%M}\n\n'
                    f'- Person: {who}\n- Session ID: {session}\n- Started in: {cwd}\n\n'
                    'Every prompt sent to Claude in this session, word for word, written by the logging hook.\n')

    mark = fence(prompt)
    with open(path, 'a', encoding='utf-8') as f:
        f.write(f'\n## {now:%Y-%m-%d %H:%M:%S %z}\n\n{mark}text\n{prompt}\n{mark}\n')


if __name__ == '__main__':
    try:
        main()
    except Exception:
        pass
    sys.exit(0)
