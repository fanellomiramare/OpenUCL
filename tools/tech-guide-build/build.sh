#!/usr/bin/env bash
# Build the OpenUCL Technical Guide PDF (pandoc + xelatex).
#
# Usage:
#   tools/tech-guide-build/build.sh [output-dir]
#
# Output default: /tmp/openucl-tech-guide/OpenUCL-Technical-Guide.pdf
# (outside the repository, so the working tree stays clean).
#
# Requires: pandoc (https://pandoc.org) and xelatex (TinyTeX, MiKTeX, TeX Live).
# The chapter list below is kept in sync with docs/technical-guide/README.md.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="$ROOT/docs/technical-guide"
OUT="${1:-/tmp/openucl-tech-guide}"
mkdir -p "$OUT"

command -v pandoc >/dev/null 2>&1 || {
  echo "error: pandoc not found — https://pandoc.org/installing.html" >&2
  exit 1
}
command -v xelatex >/dev/null 2>&1 || {
  echo "error: xelatex not found — a LaTeX distribution (TinyTeX, MiKTeX, TeX Live) is required" >&2
  exit 1
}

CHAPTERS=(
  00-style.md
  01-introduction.md
  02-goodness-of-fit.md
  03-ucl-epc.md
  04-nondetect-estimation.md
  05-btv.md
  06-btv-nondetects.md
  07-hypothesis-one-sample.md
  08-hypothesis-two-samples.md
  09-outliers.md
  10-trend.md
  11-historical-lognormal.md
  12-usl.md
  13-usl-nondetects.md
  14-reproducibility.md
  15-references.md
)

for f in "${CHAPTERS[@]}"; do
  [ -f "$SRC/$f" ] || { echo "error: missing chapter $f" >&2; exit 1; }
done

# gfm + tex_math_dollars: GFM tables and $$...$$ math (style chapter, 00-style.md).
pandoc "${CHAPTERS[@]/#/$SRC/}" \
  -f gfm+tex_math_dollars \
  --pdf-engine=xelatex \
  --number-sections \
  --toc \
  -o "$OUT/OpenUCL-Technical-Guide.pdf"

echo "written: $OUT/OpenUCL-Technical-Guide.pdf"
