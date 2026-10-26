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
  output ("n" vs "total").
- Decimal separator: `.` by default; `,` accepted when the value separator is not `,`.

## Example (semicolon-separated CSV, packed)

```csv
point;parameter;values
MW-01;TCE;1.2;0.8;<0.5;2.1;3.3
MW-02;TCE;<0.5;0.9;1.4
MW-01;PCE;4.0;4.5;5.1;4.2
```

Here the CSV column separator is `;` **and** the value separator is `;`; the third field is
quoted automatically by any CSV writer when it contains the column separator. To avoid any
ambiguity in hand-written files, prefer `|` as value separator:

```csv
point;parameter;values
MW-01;TCE;1.2|0.8|<0.5|2.1|3.3
```

## Batch semantics

Any OpenUCL command applied to a packed file processes **every series** and produces:

- a **summary table** (one row per series × statistic/method), exportable to CSV/XLSX;
- optionally, per-series detailed reports (ProUCL-style output sheets).

The same commands accept a group-by clause on long-format files to obtain the identical
batch behaviour (`--group point --variable parameter`).
