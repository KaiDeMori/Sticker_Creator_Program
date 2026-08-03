#!/usr/bin/env bash
set -euo pipefail

# Downloads and extracts signal-cli into ./signal-cli.
# One archive covers all platforms.

repository_owner="AsamK"
repository_name="signal-cli"
target_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/signal-cli"

echo "Clearing ${target_directory}..."
rm -rf "${target_directory}"
mkdir -p "${target_directory}"

echo "Resolving latest signal-cli release..."
release_JSON=$(curl -fsSL "https://api.github.com/repos/${repository_owner}/${repository_name}/releases/latest")
release_tag=$(echo "${release_JSON}" | grep -m 1 '"tag_name"' | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/')

if [[ -z "${release_tag}" ]]; then
    echo "Could not determine the latest release tag." >&2
    exit 1
fi

release_version="${release_tag#v}"
archive_name="signal-cli-${release_version}.tar.gz"
download_URL="https://github.com/${repository_owner}/${repository_name}/releases/download/${release_tag}/${archive_name}"
archive_path="${target_directory}/${archive_name}"

echo "Latest release: ${release_tag}"
echo "Downloading ${archive_name}..."
curl -fL --progress-bar -o "${archive_path}" "${download_URL}"

echo "Extracting into ${target_directory}..."
tar -xzf "${archive_path}" -C "${target_directory}" --strip-components=1
rm "${archive_path}"

# The extracted man/ only has pre-built (gzipped, non-searchable) man pages.
# The readable AsciiDoc sources they're built from live in the repo instead.
adoc_directory="${target_directory}/man/adoc"
mkdir -p "${adoc_directory}"

echo "Downloading man page sources (AsciiDoc) for ${release_tag}..."
for adoc_file in signal-cli.1.adoc signal-cli-dbus.5.adoc signal-cli-jsonrpc.5.adoc; do
    curl -fsSL -o "${adoc_directory}/${adoc_file}" \
        "https://raw.githubusercontent.com/${repository_owner}/${repository_name}/${release_tag}/man/${adoc_file}"
done

echo "Done. bin/, lib/, man/ (plus man/adoc/ sources) are now directly in ${target_directory}"
