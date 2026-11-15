# Introduction and Scope

> **OpenUCL reference version**: 0.15.0 · **chapter state**: complete · **last updated**: 2026-11-15

## What OpenUCL is

OpenUCL is an open-source, cross-platform command-line tool that reimplements the
statistical methods of the U.S. EPA ProUCL 5.2 software for environmental
applications: descriptive statistics, goodness of fit, UCL/EPC estimation,
background threshold values (percentiles, UPLs, UTLs, USLs), hypothesis tests,
outlier tests and trend tests — for data sets with and without nondetect
observations.

It is a **clean-room reimplementation**: methods are implemented from the public
ProUCL 5.2 Technical Guide equations only. The ProUCL executable is never
inspected or decompiled; every number OpenUCL prints is traceable either to a TG
equation or to live ProUCL output that the validation suite locks in golden
tests.

## What OpenUCL computes

The scope mirrors the chapters of this guide:

- descriptive statistics, batched over every series of a file;
- goodness of fit: Shapiro-Wilk, Lilliefors; gamma Anderson-Darling and K-S with
  the Singh, Singh & Iaci critical values;
- UCLs/EPCs: parametric, distribution-adjusted and bootstrap methods, with and
  without nondetects (KM, LROS, GROS), with a motivated recommendation;
- background threshold values: percentiles, UPLs (also for future k observations
  and for the mean of k), UTLs with the exact noncentral-t tolerance factor,
  gamma WH/HW limits, nonparametric limits with achieved coverage, USLs;
- one-sample and two-sample hypothesis tests (including Gehan and Tarone-Ware
  with nondetects), one-way ANOVA and Kruskal-Wallis;
- outlier tests (Dixon, Rosner) and trend tests (Mann-Kendall, Theil-Sen, OLS).

## Data layouts

Two input layouts are supported (see [the packed format
documentation](../format-packed.md)):

- **long layout** — the classic ProUCL spreadsheet: one observation per row,
  with optional group and nondetect flag/limit columns; CSV, XLSX and legacy XLS;
- **packed layout** — OpenUCL's native batch layout: one series per row with all
  values packed in a single cell, designed for files with thousands of series.

Nondetects are expressed either as a flag column plus a detection-limit column
(long layout) or inline as `<DL` (both layouts).

## Reproducibility

- Bootstrap methods are **seeded and deterministic** (default seed 381, default
  2000 runs); the seed and the run count are printed on the output sheets.
- Quantities that the TG obtains from tables (tolerance factors, Dixon critical
  values, gamma chi-square quantiles) are **computed exactly** in OpenUCL, so
  results do not depend on printed-table precision.
- Each output sheet records the input file hash (truncated SHA-256) and a UTC
  generation timestamp.

## Output style

One sheet per command invocation, plain text on stdout: a header with provenance,
one section per method group, and an explicit, motivated recommendation where the
TG provides selection guidance. Numbers are the subject; OpenUCL aims to
reproduce ProUCL's numbers, while the presentation is its own.

## Getting OpenUCL

Release binaries (self-contained, single file) are published on the project
GitHub releases for Windows, Linux and macOS (x64 and arm64; macOS bundles are
universal2). No runtime installation is required.

## Legal notice

OpenUCL is not affiliated with, nor endorsed by, the U.S. EPA. ProUCL and its
Technical Guide are works of the U.S. Government (public domain). OpenUCL is
licensed under Apache-2.0; see LICENSE and DISCLAIMER in the repository.
