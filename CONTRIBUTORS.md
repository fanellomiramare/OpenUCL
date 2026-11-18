# Contributors

## Matteo (@fanellomiramare)
Project founder, product direction and maintainer. Defined the project goals: an
open-source, cross-platform reimplementation of the ProUCL 5.2 statistical
methods, designed for large packed-series datasets and batch automation.

## ZCode (AI coding agent — GLM, by Z.ai)
Implementation assistant working under Matteo's direction: statistical engine
(descriptive statistics, Shapiro-Wilk, Lilliefors, gamma GOF), data engine
(packed and long layouts, CSV/XLSX/XLS readers), command-line interface,
CI/CD pipelines, and the test suite.

## Claude (AI coding assistant — Anthropic)
Project assistant under Matteo's direction since November 2026: audit of the
code base, corrections to the statistical engine (UCL recommendation logic
transcribed from the Technical Guide decision flowcharts, adjusted gamma
significance levels, nondetect UCLs on KM estimates, batch robustness,
bootstrap generator) and the related tests and documentation.

---

**A note on AI-assisted development.** OpenUCL is developed with the help of
AI coding agents under human direction. Every contribution is committed to the
public repository where it can be reviewed, tested and audited by anyone. All
statistical algorithms are implemented from published, citable sources (the
public-domain ProUCL Technical Guide and peer-reviewed literature), and each
method carries references to the equations it implements.
