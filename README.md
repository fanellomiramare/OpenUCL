<div align="center">

# OpenUCL

**Open-source, cross-platform environmental statistics** — a clean-room reimplementation
of the methods made popular by the U.S. EPA **ProUCL 5.2** software, designed for
**large "packed" data sets**, batch processing and full automation.

[![CI](https://github.com/fanellomiramare/OpenUCL/actions/workflows/ci.yml/badge.svg)](https://github.com/fanellomiramare/OpenUCL/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/fanellomiramare/OpenUCL)](https://github.com/fanellomiramare/OpenUCL/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/fanellomiramare/OpenUCL/total)](https://github.com/fanellomiramare/OpenUCL/releases)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

**Download** — [Windows](#download) · [macOS](#download) · [Linux](#download) · [All releases](https://github.com/fanellomiramare/OpenUCL/releases)

*Not affiliated with or endorsed by the U.S. EPA. "ProUCL" is a U.S. EPA product,
referenced only to document methodological interoperability.*

</div>

---

## Download

Every release ships **self-contained, single-file executables** (no runtime to install)
built automatically by CI for all three operating systems.
👉 [Latest release](https://github.com/fanellomiramare/OpenUCL/releases/latest)

> [!IMPORTANT]
> **OpenUCL today works through the CLI only.**
> The GUI download is a graphical shell under development
> (roadmap Phase 4 — reporting & desktop GUI): it launches,
> but implements no analysis features yet.

| OS | CLI — the working interface (batch) | GUI — shell in development | Notes |
|---|---|---|---|
| **Windows** | [cli-win-x64.zip](https://github.com/fanellomiramare/OpenUCL/releases/latest/download/cli-win-x64.zip) | [gui-win-x64.zip](https://github.com/fanellomiramare/OpenUCL/releases/latest/download/gui-win-x64.zip) | Windows 10+ (x64). Unzip and run — nothing to install. |
| **macOS** | [cli-osx-universal.zip](https://github.com/fanellomiramare/OpenUCL/releases/latest/download/cli-osx-universal.zip) | [gui-osx-universal.zip](https://github.com/fanellomiramare/OpenUCL/releases/latest/download/gui-osx-universal.zip) | Native on Intel **and** Apple Silicon (universal binary). First run: right-click → *Open*, or install with the command below — no Gatekeeper warning. |
| **Linux** | [cli-linux-x64.zip](https://github.com/fanellomiramare/OpenUCL/releases/latest/download/cli-linux-x64.zip) · [arm64](https://github.com/fanellomiramare/OpenUCL/releases/latest/download/cli-linux-arm64.zip) | [gui-linux-x64.zip](https://github.com/fanellomiramare/OpenUCL/releases/latest/download/gui-linux-x64.zip) · [arm64](https://github.com/fanellomiramare/OpenUCL/releases/latest/download/gui-linux-arm64.zip) | glibc-based distributions. Unzip, `chmod +x`, run. |

> [!NOTE]
> **macOS, no Gatekeeper friction (free):** browser downloads are quarantined;
> downloads made with `curl` are not.
> ```sh
> curl -fLO https://github.com/fanellomiramare/OpenUCL/releases/latest/download/cli-osx-universal.zip
> unzip cli-osx-universal.zip && chmod +x openucl
> ./openucl --help
> ```

Quick start (works identically on all three systems):

```sh
openucl summary wells.csv                 # descriptive statistics, batch over all series
openucl gof data.xlsx --value-col TCE     # goodness of fit (Shapiro-Wilk, Lilliefors, gamma)
openucl ucl data.xlsx --nd-flag-col C     # UCL/EPC suite with recommendation sheet
openucl btv data.csv --value-sep '|'      # background values: UPL, UTL, USL
openucl ndstats wells.csv                 # Kaplan-Meier, ROS, DL/2 with nondetects
```

Older releases and changelogs: [Releases](https://github.com/fanellomiramare/OpenUCL/releases).

---

## Why OpenUCL

| | ProUCL 5.2 | OpenUCL |
|---|---|---|
| Platform | Windows only | Windows, macOS, Linux |
| Data layout | one series per column | one series per **row** (packed values) + classic layout |
| Scale | in-memory Excel sheet | streaming, batch over thousands of series |
| Automation | none | full CLI, exit codes, scriptable |
| Reproducibility | fixed output sheets | self-documenting sheets: command, options, seed, input hash |
| License | free, closed source | open source (Apache-2.0) |
| Distribution | Windows installer | ready-to-run executables for all 3 OS |

### Beyond ProUCL: exact critical values

Where ProUCL relies on tabulated or approximated critical values, OpenUCL
computes them exactly. Within the range covered by ProUCL's tables the two
agree to display precision; outside that range, OpenUCL remains exact.

| Method | ProUCL 5.2 | OpenUCL |
|---|---|---|
| Land's H-UCL | H interpolated from Land's (1975) tables; 90% and 95% levels only | exact H for any sample size and confidence level (conditional-t polar integration) |
| UTL tolerance factor K | tabulated values for n ≤ 30, Natrella approximation for larger n | exact noncentral-t K for any n, confidence level and coverage |
| Dixon outlier test | tabulated critical values (TG Appendix A) | exact critical values by integration of order-statistic ratios (n ≤ 25, any α) |

## What OpenUCL computes today

Validated against the worked examples of the public ProUCL 5.2 Technical Guide
(golden tests run on the original sample data sets):

- **Descriptive statistics** — batch over every series of a file (packed or long layout)
- **Goodness of fit** — Shapiro-Wilk (Royston AS R94), Lilliefors K-S; gamma
  Anderson-Darling and K-S with the Singh, Singh & Iaci critical values
- **UCLs/EPCs** — Student's t, modified-t, jackknife, CLT, adjusted CLT, Chebyshev,
  approximate and adjusted gamma, bootstrap (standard, percentile, BCA, bootstrap-t),
  with and without nondetects (KM, LROS, GROS), with ProUCL 5.2 recommendation logic
- **Background values (BTVs)** — upper percentiles, UPL1/UPLk/UPLk-mean, UTL 95-95
  (exact noncentral-t tolerance factor), nonparametric limits with achieved confidence
- **Nondetect estimation** — Kaplan-Meier (validated against the Technical Guide's
  Oahu example), robust ROS (LROS/GROS) with Helsel-Cohn plotting positions, DL/2
- **Outlier tests** — Dixon (n ≤ 25) and Rosner (n ≥ 25)
- **One-sample hypothesis tests** — Student's t, exact sign test, exact
  Wilcoxon signed-rank
- **Two-sample hypothesis tests** — Student's t (pooled), Satterthwaite,
  Wilcoxon-Mann-Whitney, F test, Gehan, Tarone-Ware (with nondetects)
- **ANOVA & trend** — one-way ANOVA, Kruskal-Wallis, Mann-Kendall, Theil-Sen, OLS
- **Historical methods** — MVUE of the lognormal mean (Finney/Bradu-Mundlak),
  Chebyshev (MVUE), Hall's skewness-adjusted bootstrap, Land's exact H-UCL
  (1975 conditional-t polar integration; ProUCL computes the same statistic by
  table interpolation, which deviates from the exact values for sample sizes
  beyond the table range)

## Roadmap to 1.0-beta

| Phase | Component | Subtasks | Status |
|---|---|---|---|
| 1 | Data engine | packed format, ProUCL-compatible long layout, CSV/XLSX/XLS | ✅ v0.2–v0.7 |
| 1 | Descriptive stats & GOF | normal/lognormal/gamma, Shapiro-Wilk, Lilliefors, A-D | ✅ v0.2 |
| 2 | UCLs/EPCs | t, modified-t, CLT, gamma, bootstrap, KM, ROS, Land, Hall | ✅ v0.5–v0.12 |
| 2 | Background values | percentiles, UPL1/UPLk, UTL 95-95, USL, KM-based with NDs | ✅ v0.7–v0.10 |
| 2 | Parametric USLs | d2max critical values, USL with nondetects | ⬜ planned |
| 3 | Outlier tests | Dixon (exact critical values), Rosner | ✅ v0.8 |
| 3 | Hypothesis tests | one- & two-sample, with nondetects, ANOVA/Kruskal-Wallis, trend | ✅ v0.8–v0.10 |
| 3 | Historical methods | MVUE, Chebyshev (MVUE), Hall's bootstrap, Land's exact H-UCL | ✅ v0.11–v0.12 |
| 4 | Batch output & XLSX reports | per-series sheets, summary tables, bundle mode | ⬜ planned |
| 4 | Desktop GUI | analysis modules for the downloadable shell | ⬜ planned |
| 5 | Graphs | Q-Q plots, box plots, histograms, time series | ⬜ planned |
| 6 | Validation campaign → 1.0.0-beta | 28 ProUCL reference data sets, side-by-side comparison | ⬜ planned |

Phases 1–3 — the statistical engine — are complete and shipping.
The upcoming work is reporting (Phase 4), graphs (Phase 5) and the
validation campaign that leads to 1.0.0-beta (Phase 6).

Every phase ships runnable CLI executables — see the
[Releases](https://github.com/fanellomiramare/OpenUCL/releases) page for the
version-by-version changelog.

## Documentation

- [Packed series format](docs/format-packed.md) — one series per row, nondetects as `<DL`
- [ProUCL Technical Guide 5.2](https://www.epa.gov/land-research/proucl-software) —
  the methodological reference (public domain); each implementation cites the
  equations it reproduces

## Building from source

Requirements: [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0).

```sh
git clone https://github.com/fanellomiramare/OpenUCL.git
cd OpenUCL
dotnet build
dotnet test
```

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) first —
in particular the **clean-room policy**: OpenUCL must never contain code derived
from the ProUCL binaries. The people and agents behind the project are listed in
[CONTRIBUTORS.md](CONTRIBUTORS.md).

## License and disclaimer

[Apache-2.0](LICENSE) · [Full disclaimer](DISCLAIMER.md).

OpenUCL is an independent open-source project, **not** affiliated with, endorsed by
or sponsored by the U.S. EPA. All methods are implemented from publicly available
documentation (the ProUCL Technical Guide and User Guide, works of the U.S. Federal
Government) and peer-reviewed literature, with citations in the code. Statistical
results can drive regulatory decisions: always validate critical results and consult
a qualified statistician when in doubt.
