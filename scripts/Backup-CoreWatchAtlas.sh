#!/usr/bin/env bash
set -euo pipefail

# Creates a consistent SQLite backup together with the Data Protection keys.
# Run as the account that owns the CoreWatch Atlas service data.
source_dir="${1:-/var/lib/corewatch-atlas}"
destination_dir="${2:-/var/backups/corewatch-atlas}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_dir="${destination_dir}/corewatch-atlas-${timestamp}"
database="${source_dir}/atlas.db"

command -v sqlite3 >/dev/null || { echo "sqlite3 is required for an online backup." >&2; exit 1; }
[[ -f "$database" ]] || { echo "Database not found: $database" >&2; exit 1; }

umask 077
mkdir -p "$backup_dir"
sqlite3 "$database" ".backup '${backup_dir}/atlas.db'"

for directory in keys updates; do
  if [[ -d "${source_dir}/${directory}" ]]; then
    cp -a "${source_dir}/${directory}" "${backup_dir}/${directory}"
  fi
done

(cd "$backup_dir" && sha256sum atlas.db $(find keys updates -type f -print 2>/dev/null | sort) > SHA256SUMS)
sqlite3 "${backup_dir}/atlas.db" "PRAGMA integrity_check;" | grep -qx "ok"
echo "$backup_dir"
