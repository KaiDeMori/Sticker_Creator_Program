#!/usr/bin/env bash
# Builds the two shippable packages (win, linux) into _publish/. Both are x64 —
# the only architecture supported, so it's not called out in any output name.
# Self-contained, single-file, no trimming. Run from Git Bash on Windows.
set -euo pipefail

workspace_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_path="$workspace_root/Sticker_Creator_Program/Sticker_Creator_Program.csproj"
signal_cli_install_directory="$workspace_root/signal-cli"
publish_root="$workspace_root/_publish"
executable_name="Sticker_Creator_Program"

# platform label -> .NET runtime identifier. The RID itself must stay exact
# (it's what dotnet publish -r requires); only our own output names drop "x64".
platforms=(win linux)
rid_for_win="win-x64"
rid_for_linux="linux-x64"

for tool in dotnet tar zip; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "publish.sh: required tool '$tool' not found on PATH." >&2
    exit 1
  fi
done

if [ ! -d "$signal_cli_install_directory" ]; then
  echo "publish.sh: signal-cli install not found at $signal_cli_install_directory" >&2
  exit 1
fi

rm -rf "$publish_root"
mkdir -p "$publish_root"

project_directory="$(dirname "$project_path")"
rm -rf "$project_directory/bin" "$project_directory/obj"

for platform in "${platforms[@]}"; do
  rid_var="rid_for_${platform}"
  rid="${!rid_var}"

  echo "== Publishing $platform ($rid) =="
  dotnet publish "$project_path" -c Release -r "$rid" --self-contained true -o "$publish_root/$platform"
  cp -r "$signal_cli_install_directory" "$publish_root/$platform/signal-cli"
done

echo "== Packaging win (zip, store/no compression) =="
(
  cd "$publish_root/win"
  zip -0 -r -q "../${executable_name}_win.zip" .
)

echo "== Packaging linux (tar, uncompressed, forced executable bit) =="
(
  cd "$publish_root/linux"
  tar --mode='+x' -cf "../${executable_name}_linux.tar" "$executable_name"
  tar --mode='a+rX,u+w' -rf "../${executable_name}_linux.tar" --exclude="$executable_name" .
)

echo "== Removing staging directories =="
for platform in "${platforms[@]}"; do
  rm -rf "$publish_root/$platform"
done

echo "== Done =="
ls -lh "$publish_root/${executable_name}_win.zip" "$publish_root/${executable_name}_linux.tar"
