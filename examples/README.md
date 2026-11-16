# OpenUCL examples

Small ready-made inputs for trying the OpenUCL command line. Every file is a valid
packed-format input (see [the packed format documentation](../docs/format-packed.md));
run the commands from the repository root, or download the example files from the
release archives.

| file | what it is | try it with |
|---|---|---|
| [`demo-packed.csv`](demo-packed.csv) | 6 series (3 wells × 2 parameters, one series with nondetects) — the batch example | `openucl btv demo-packed.csv -o demo-out` |
| [`x-example-packed.csv`](x-example-packed.csv) | series X of the Technical Guide Example 2-2 (n = 25) | `openucl ucl x-example-packed.csv` |

## The batch example

`demo-packed.csv` is the input used in the [batch walkthrough](../docs/format-packed.md#a-complete-batch-example):

```
openucl btv demo-packed.csv -o demo-out
```

produces one ProUCL-style sheet per series in `demo-out/btv/` plus the summary CSV
and XLSX (`demo-packed_btv_summary.csv` / `.xlsx`). Other modules on the same input:

```
openucl ucl  demo-packed.csv -o demo-out --bundle   ← all sheets in one combined file
openucl gof  demo-packed.csv -o demo-out            ← goodness of fit, summary CSV
openucl test1 demo-packed.csv --cs 2 -o demo-out    ← one-sample tests vs Cs = 2
```

## The anchoring example

`x-example-packed.csv` reproduces the Technical Guide's Example 2-2 UCL table: with
it, `openucl ucl x-example-packed.csv` prints Student's t 61.66, gamma adjusted
74.27 and the rest of the published values — the same numbers the golden tests lock
in CI. It is kept in the repository as a ready-made input for manual verification.
