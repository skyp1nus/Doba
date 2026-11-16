#!/usr/bin/env bash
# Stop hook: sends the architect back to work while docs/PROGRESS.md has open items.
# Exit 0 = allow the stop. Exit 2 = block it; stderr is shown to Claude as the reason.
set -u
cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0

progress="docs/PROGRESS.md"

# Not kicked off yet, or the architect declared a hard blocker: let it stop.
[ -f "$progress" ] || exit 0
[ -f "docs/BLOCKED.md" ] && exit 0

open=$(grep -cE '^[[:space:]]*[-*] \[ \]' "$progress" 2>/dev/null)
open=${open:-0}
[ "$open" -eq 0 ] && exit 0

# Loop guard: if PROGRESS.md has not changed across several stop attempts in a row,
# the session is spinning rather than working, so let it stop.
state="${TMPDIR:-/tmp}/doba-keep-going.state"
hash=$(sha1sum "$progress" | cut -d' ' -f1)
prev=""; count=0
[ -f "$state" ] && read -r prev count < "$state"
if [ "$hash" = "$prev" ]; then count=$((count + 1)); else count=1; fi
printf '%s %s\n' "$hash" "$count" > "$state"
[ "$count" -gt 5 ] && exit 0

cat >&2 <<EOF
docs/PROGRESS.md still has $open open item(s), so the work is not finished.
Re-read docs/KICKOFF.md (sections 8-10) and docs/PROGRESS.md, then continue with the next open item.
Stop only when every item is ticked, or after writing docs/BLOCKED.md if everything left is blocked by something outside your control.
EOF
exit 2
