# Style and Method Template

> **OpenUCL reference version**: 0.15.0 · **chapter state**: complete · **last updated**: 2026-11-15

This chapter defines the conventions every chapter of the OpenUCL Technical Guide
follows, and the template each method section uses. It keeps the guide uniform,
verifiable, and free of tool-specific markup.

## Source neutrality

- **Pure Markdown**: no front-matter, no tool-specific metadata. GFM tables,
  standard LaTeX math in `$$...$$`, margin notes as blockquotes.
- Every chapter opens with a one-line blockquote right after the title:

  > **OpenUCL reference version**: 0.15.0 · **chapter state**: planned | draft | complete · **last updated**: YYYY-MM-DD

- The **chapter state** is `planned` (placeholder), `draft` (writing), or
  `complete` (content and anchors in place).

## Sourcing rule

Every number printed in this guide comes from one of two verifiable sources:

1. a **real OpenUCL CLI run** on the ProUCL sample data sets (the run is
   reproducible from the repository; examples are pasted, never invented);
2. a **golden test** in the repository test suite (which runs on CI, with the
   sample data embedded).

Private notes, chat history, or memory are never sources.

## Method template

Each method section in the guide follows this structure:

1. **Definition and formulas** — what the method estimates, with the equations
   and their references to the public ProUCL 5.2 Technical Guide (TG), using the
   same equation numbering as the TG (e.g., "TG eq. 3-20").
2. **Algorithm** — how OpenUCL computes it, in a few lines: exact closed forms
   where possible, numerical integration where the TG uses tables.
3. **OpenUCL specifics** — the command and options that produce it, defaults,
   and reproducibility notes (bootstrap seed, printed provenance).
4. **Validation anchors** — a table pairing the OpenUCL value with the ProUCL
   value (live output or TG table), with the data set of provenance. Format:

   | quantity | data set | OpenUCL | ProUCL 5.2 | note |
   |---|---|---|---|---|
   | USL normal | silver-data.xls (n = 56) | 241.087 | 241.1 | live output |

5. **Deviations** — documented, quantified deviations from the TG or from
   ProUCL's implementation, with the measured magnitude. Where there is no
   deviation the section states so.

## Tone and language

- English throughout, sober scientific tone. No marketing wording.
- Numbers are the subject: OpenUCL aims to reproduce ProUCL's numbers; the
  presentation (output sheets, wording, recommendations) is OpenUCL's own.
- Recommendations printed by the CLI are reported verbatim when quoted.
- Legal framing: OpenUCL is not affiliated with or endorsed by the U.S. EPA;
  ProUCL and its Technical Guide are public domain. OpenUCL is Apache-2.0.
