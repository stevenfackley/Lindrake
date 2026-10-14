#!/bin/sh
# ============================================================
# Lindrake.Web — render the Blazor client's runtime config
# Run by the nginx image's /docker-entrypoint.sh before nginx starts.
#
# API_BASE_URL  absolute http(s) URL of the API as the browser sees it
#               (e.g. http://localhost:5000). Empty or unset: the client
#               calls api/... on its own origin (reverse proxy in front).
# ============================================================
set -eu

target="${LINDRAKE_APPSETTINGS_PATH:-/usr/share/nginx/html/app/appsettings.json}"
url="${API_BASE_URL:-}"

case "$url" in
    "" | http://* | https://*) ;;
    *)
        echo "$0: API_BASE_URL must be empty or an absolute http(s) URL, got '$url'" >&2
        exit 1
        ;;
esac

# JSON string escaping for the two characters that can matter in a URL.
escaped=$(printf '%s' "$url" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g')

printf '{\n  "ApiBaseUrl": "%s"\n}\n' "$escaped" > "$target"

# Publish pre-compresses the build-time file; drop the stale copies so no
# server configuration can serve them instead of the rendered one.
rm -f "$target.br" "$target.gz"

echo "$0: ApiBaseUrl='${url}' -> $target"
