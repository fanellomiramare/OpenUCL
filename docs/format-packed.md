# OpenUCL packed series format

OpenUCL reads two data layouts:

1. **Long (classic) layout** — ProUCL-compatible: one variable per column, one observation
   per row, plus optional group variable and nondetect flag/limit columns.
2. **Packed series layout** (the OpenUCL native layout, designed for large batch jobs) —
   **one series per row**:

| column | meaning | example |
|---|---|---|
| 1 | point / series identifier | `MW-07` |
| 2 | parameter / analyte | `TCE` |
| 3 | all values of the series, packed in a single cell | `1.2;0.8;<0.5;2.1;3.3` |
| 4+ | optional extra metadata (ignored or used as extra grouping) | |

The same file can contain thousands of series; OpenUCL streams them one at a time, so file
size is effectively bounded only by disk space (and each single series must fit in RAM).

## Value list rules

- Values inside a cell are separated by a **value separator**. Default: `;`
  (recommended inside Excel cells). In plain text files `|` is recommended as the default,
  because `;` commonly doubles as the CSV column separator in Italian/European locales.
  The separator is always configurable and auto-detected when possible.
- **Nondetects** are written with a leading `<` followed by the detection/reporting limit:
  `<0.68`. This is equivalent to ProUCL's separate "ND flag" + "detection limit" columns.
- **Missing values**: empty slots (`;;`), `NA`, or `NaN` are skipped and reported in the
  output: the per-series rows expose the columns `N` (observations kept) and `ND`
  (nondetects), and the footer reads "nondetects included in N; stats on detected
  values only".
- Decimal separator: `.` by default; `,` accepted when the value separator is not `,`.

## Example (semicolon-separated CSV, packed)

The values cell contains the column separator, so it **must be quoted** (any CSV writer does
this automatically):

```csv
point;parameter;values
MW-01;TCE;"1.2;0.8;<0.5;2.1;3.3"
MW-02;TCE;"<0.5;0.9;1.4"
MW-01;PCE;"4.0;4.5;5.1;4.2"
```

In hand-written files an unquoted cell with `;` inside is read as a **single value** (the
first one only). To avoid the quotes entirely, prefer `|` as value separator:

```csv
point;parameter;values
MW-01;TCE;1.2|0.8|<0.5|2.1|3.3
```

## Batch semantics

Any OpenUCL command applied to a packed file processes **every series** and prints the
per-series results to stdout (`summary` prints one row per series; `ucl`/`btv`/`test1`
print the full ProUCL-style output sheet for each series).

For `ucl`, `btv`, `gof`, `ndstats`, `test1`, `test2`, `outlier`, `trend` and `anova`,
`--out <dir>` (short form `-o <dir>`) switches to **batch output**: per-series sheets
are written into `<dir>/<command>/` (sanitized `point__parameter.txt` names, automatic
`(2)`, `(3)`... disambiguation; `anova` writes a single file-level sheet) plus a
summary in two formats, `<input>_<command>_summary.csv` and
`<input>_<command>_summary.xlsx`, in `<dir>`. Both carry the run-info header (command,
arguments, UTC timestamp, series count) and one row per series or comparison (common
statistics columns plus the command-specific block, and a trailing `warnings`
column); in the XLSX the run info lives on a second sheet and numeric cells are real
numbers. With `--bundle` the sheets are appended into one combined
`<input>_<command>_bundle.txt` file instead of one file per series. Without `--out`
the per-series sheets keep going to stdout.

A small packed example is available in the repository:
[`examples/demo-packed.csv`](../examples/demo-packed.csv). The repository also carries
[`examples/x-example-packed.csv`](../examples/x-example-packed.csv) — the series X of the
Technical Guide Example 2-2, kept as a ready-made input for reproducing the manual
anchoring checks (Land's H and the exact tolerance factor).

The same commands accept a group-by clause on long-format files to obtain the identical
batch behaviour (`--group-col <group column> --value-col <value column>`). `test2`
also accepts one long-format file with exactly two `--value-col` columns to compare
column A against column B directly.

## A complete batch example

The repository ships a ready-made batch input, [`examples/demo-packed.csv`](../examples/demo-packed.csv):
6 series (3 wells × 2 parameters), one of them with nondetects. Running the BTV module
in batch mode:

```
openucl btv examples/demo-packed.csv -o demo-out
```

produces this layout (stdout stays quiet, one progress line at the end):

```
demo-out/
├── btv/
│   ├── MW-01__PCE.txt            ← full ProUCL-style sheet, one per series
│   ├── MW-01__TCE.txt
│   ├── MW-02__PCE.txt
│   ├── MW-02__TCE.txt
│   ├── MW-03__PCE.txt
│   └── MW-03__TCE.txt
├── demo-packed_btv_summary.csv   ← run-info header + one row per series
└── demo-packed_btv_summary.xlsx  ← same summary as Excel (2 sheets)
```

The summary CSV carries the run-info header, the column header and one row per series:

```
# command: btv
# arguments: btv examples/demo-packed.csv -o demo-out
# generated: 2026-11-16 09:45 UTC
# series: 6
point,parameter,n,nd,nd_pct,min,max,median,mean,sd,km_mean,km_sd,upl1_normal,utl_normal,usl_normal,utl_nonparam_cc,warnings
MW-01,TCE,8,1,12.5,0.5,3.3,1.9,1.74286,0.97785,1.5875,0.941326,3.77419,5.06703,3.63806,0.301663,
MW-02,TCE,7,1,14.29,0.7,2.2,1.25,1.35,0.568331,1.25714,0.531459,2.58697,3.45719,2.38557,0.264908,
MW-03,TCE,8,0,0,1.7,6.2,3.45,3.7625,1.50422,,,6.78524,8.55689,6.81855,0.33658,
```

Notes on reading it: `km_mean`/`km_sd` are filled only for series with nondetects
(MW-03 has none); the trailing `warnings` column counts per-series warnings and shows
the first message (empty here). The same summary is also written as
`demo-packed_btv_summary.xlsx`, with the table on a "summary" sheet (numeric cells as
real numbers, ready for filters and pivots) and the run details on a "run info" sheet.
Adding `--bundle` replaces the six per-series files with one
`demo-packed_ucl_bundle.txt` (example with the `ucl` module):

```
openucl ucl examples/demo-packed.csv -o demo-out --bundle
demo-out/ucl/demo-packed_ucl_bundle.txt   ← all 6 sheets, in sequence
```

Regenerating with a different command writes into its own subfolder
(`demo-out/ucl/`, `demo-out/btv/`, ...), so outputs never overwrite each other.
`--out`/`--bundle` work the same way for `ucl`, `btv`, `gof`, `ndstats`, `test1`,
`test2`, `outlier`, `trend` and `anova`.
