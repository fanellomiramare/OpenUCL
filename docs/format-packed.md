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
summary CSV `<input>_<command>_summary.csv` in `<dir>` with a run-info comment header
(command, arguments, UTC timestamp, series count) and one row per series or comparison
(common statistics columns plus the command-specific block, and a trailing `warnings`
column). With `--bundle` the sheets are appended into one combined
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
