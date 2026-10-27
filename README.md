# OpenUCL

**OpenUCL** is a free, open-source, cross-platform (Windows / macOS / Linux) reimplementation
of the statistical methods made popular by the U.S. EPA **ProUCL 5.2** software, designed from
the ground up for **large datasets** ("packed" series files), **batch processing**, and
**automation** via a command-line interface — in addition to a classic desktop GUI.

> **Disclaimer:** OpenUCL is an independent open-source project. It is **not** produced by,
> endorsed by, or affiliated with the U.S. Environmental Protection Agency. "ProUCL" is a
> product of the U.S. EPA and is mentioned here only to document interoperability of
> statistical methodology. OpenUCL is a clean-room reimplementation based solely on the
> publicly available ProUCL Technical Guide and peer-reviewed literature.

## Why OpenUCL

| | ProUCL 5.2 | OpenUCL |
|---|---|---|
| Platform | Windows only | Windows, macOS, Linux |
| Data layout | one series per column | one series per **row** (packed values) + classic layout |
| Scale | in-memory Excel sheet | streaming, batch over thousands of series |
| Automation | none | full CLI, exit codes, scriptable |
| License | free, closed source | open source (Apache-2.0) |
| Distribution | Windows installer | ready-to-run executables for all 3 OS |

## Status

**Phase 1 in progress.** Working today:

- **Packed series layout** (one series per row, values in a single cell) and the classic
  ProUCL **long layout** (CSV/TSV and `.xlsx`, with nondetect flag and detection-limit
  columns, optional group column)
- Batch **descriptive statistics** per series (thousands of series per file, streamed)
- **Goodness-of-fit**: Shapiro-Wilk (Royston 1995, AS R94, n ≤ 5000) and Lilliefors
  K-S, for normality and lognormality, with the ProUCL-style combined conclusion;
  **gamma EDF tests** (Anderson-Darling A² and K-S D) with the Singh, Singh and Iaci
  critical-value tables from the ProUCL 5.2 Technical Guide Appendix A, plus gamma
  MLE fitting (k̂, θ̂) with the bias-corrected k*, θ* used by ProUCL
- **UCL/EPC suite** for full data sets: Student's t, modified-t, jackknife, CLT,
  adjusted CLT (Chen), Chebyshev (mean, sd), approximate and adjusted gamma UCLs
  (chi-square + table 2-2 beta interpolation), seeded bootstrap (standard, percentile,
  BCA, bootstrap-t) — with a recommendation sheet following the ProUCL 5.2 decision
  logic; **validated against the Technical Guide's worked Example 2-2** (gamma MLE,
  chi-square values, gamma UCLs 71.77/74.27, t-UCL 61.66, CLT 60.98, Chebyshev 88.85)
  — CLI: `openucl ucl` (self-documenting report sheets with reproducibility metadata)
- **Nondetect (left-censored) estimation**: Kaplan-Meier mean/SE/sd (validated against
  the Technical Guide's Oahu example), robust ROS for lognormal (LROS) and gamma (GROS)
  distributions with Helsel-Cohn plotting positions for multiple detection limits,
  and DL/2 substitution — CLI: `openucl ndstats`
- **Legacy `.xls` reading** (ExcelDataReader): the sample datasets shipped with
  ProUCL open directly
- CLI: `openucl summary`, `openucl gof` (`--lognormal`, `--gamma`), `openucl ndstats`,
  `openucl ucl`
- Avalonia GUI shell (menus laid out, modules coming online phase by phase)

Coming next: UCLs/EPCs, hypothesis tests, graphs.

Roadmap:

1. ~~Repository, CI, license~~ ✅ — plus validation harness against ProUCL 5.2 outputs (in progress)
2. Data engine (packed + long format + XLSX/CSV) and descriptive statistics, GOF tests
3. UCLs/EPCs (all ProUCL methods, with and without nondetects) + UTLs/UPLs/USLs
4. Outlier, hypothesis, ANOVA and trend tests
5. Avalonia desktop GUI
6. Graphs, packaging, 1.0 release

See [docs/format-packed.md](docs/format-packed.md) for the packed series file format.

## Building from source

Requirements: [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0).

```sh
git clone https://github.com/fanellomiramare/OpenUCL.git
cd OpenUCL
dotnet build
dotnet test
```

Run the CLI:

```sh
dotnet run --project src/OpenUCL.Cli -- summary <data-file>
```

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) first —
in particular the **clean-room policy**: OpenUCL must never contain code derived from
the ProUCL binaries. The people and agents behind the project are listed in
[CONTRIBUTORS.md](CONTRIBUTORS.md).

## License

[Apache-2.0](LICENSE). The ProUCL User Guide and Technical Guide are works of the
U.S. Federal Government (public domain) and are used here as the methodological reference.
