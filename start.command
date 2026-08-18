#!/bin/zsh
# macOS launcher for MC Mod Migrator
cd "$(dirname "$0")"

if ! command -v node >/dev/null 2>&1; then
  osascript -e 'display alert "需要 Node.js 20 或更高版本" message "请先从 nodejs.org 安装 Node.js，然后重新打开此文件。"'
  exit 1
fi

node server.js &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null' EXIT INT TERM

for i in {1..20}; do
  if curl --silent --fail http://127.0.0.1:3728/ >/dev/null 2>&1; then
    open http://127.0.0.1:3728/
    break
  fi
  sleep 0.2
done

wait "$server_pid"
