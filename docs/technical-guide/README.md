# OpenUCL Technical Guide

Technical documentation for the OpenUCL statistical methods: what each method
computes, the equations it follows (with references to the public ProUCL 5.2
Technical Guide, "TG"), how it is exposed on the OpenUCL command line, and the
validation anchors that lock its numbers.

The chapter order mirrors the structure of the TG, so a reader can move from
one to the other chapter by chapter. Style, template and sourcing rules are in
[00-style.md](00-style.md).

## Chapters

| # | file | title | state |
|---|---|---|---|
| 0 | [00-style.md](00-style.md) | Style and method template | complete |
| 1 | [01-introduction.md](01-introduction.md) | Introduction and scope | complete |
| 2 | [02-goodness-of-fit.md](02-goodness-of-fit.md) | Goodness of fit | planned |
| 3 | [03-ucl-epc.md](03-ucl-epc.md) | UCLs and EPCs | planned |
| 4 | [04-nondetect-estimation.md](04-nondetect-estimation.md) | Estimation and UCLs with nondetects | planned |
| 5 | [05-btv.md](05-btv.md) | Background threshold values | planned |
| 6 | [06-btv-nondetects.md](06-btv-nondetects.md) | Background threshold values with nondetects | planned |
| 7 | [07-hypothesis-one-sample.md](07-hypothesis-one-sample.md) | One-sample hypothesis tests | planned |
| 8 | [08-hypothesis-two-samples.md](08-hypothesis-two-samples.md) | Two-sample hypothesis tests and ANOVA | planned |
| 9 | [09-outliers.md](09-outliers.md) | Outlier tests | planned |
| 10 | [10-trend.md](10-trend.md) | Trend tests | planned |
| 11 | [11-historical-lognormal.md](11-historical-lognormal.md) | Historical lognormal methods | planned |
| 12 | [12-usl.md](12-usl.md) | Upper simultaneous limits (full data sets) | complete |
| 13 | [13-usl-nondetects.md](13-usl-nondetects.md) | Upper simultaneous limits with nondetects | planned |
| 14 | [14-reproducibility.md](14-reproducibility.md) | Reproducibility | planned |
| 15 | [15-references.md](15-references.md) | References | planned |

## Building the PDF

`tools/tech-guide-build/build.sh` concatenates the chapters in manifest order
and produces the PDF with pandoc + xelatex (requires both installed):

```
tools/tech-guide-build/build.sh            # PDF in /tmp/openucl-tech-guide/
tools/tech-guide-build/build.sh out/tg     # or pass an output directory
```

The build writes outside the repository by default, so the working tree stays
clean.

## Source rules

Pure Markdown (no front-matter); GFM tables; LaTeX math in `$$...$$`; margin
notes as blockquotes. Every printed number comes from a real OpenUCL CLI run on
the ProUCL sample data sets or from the golden tests. English, sober tone.
OpenUCL is not affiliated with or endorsed by the U.S. EPA; ProUCL and its
Technical Guide are public domain. OpenUCL is Apache-2.0.
