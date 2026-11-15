# Upper Simultaneous Limits (Full Data Sets)

> **OpenUCL reference version**: 0.15.0 · **chapter state**: complete · **last updated**: 2026-11-15

## Definition and formulas

An upper simultaneous limit (USL) bounds the **maximum** of a set of future
comparisons against a background data set. In the multi-constituent screening
workflow of TG section 5.3, the USL of the background data set is compared with
the largest observed value of the site data: if a site maximum exceeds the USL,
the corresponding constituent is flagged. This makes the USL an alternative to
the UTL when several constituents are screened against one background set.

OpenUCL computes the USL on the same four scales as the other parametric
background limits:

$$\mathrm{USL}_{\text{normal}} = \bar{x} + d_{2\max} \cdot s \tag{TG eq. 3-20}$$

$$\mathrm{USL}_{\text{lognormal}} = \exp\!\left(\overline{\ln x} + d_{2\max} \cdot s_{\ln x}\right) \tag{TG eq. 3-21}$$

$$\mathrm{USL}_{\gamma,\mathrm{WH}} = \left(\overline{\sqrt[3]{x}} + d_{2\max} \cdot s_{\sqrt[3]{x}}\right)^{3} \qquad \mathrm{USL}_{\gamma,\mathrm{HW}} = \left(\overline{\sqrt[4]{x}} + d_{2\max} \cdot s_{\sqrt[4]{x}}\right)^{4} \tag{TG section 3.6}$$

plus the nonparametric USL, which is the sample maximum:

$$\mathrm{USL}_{\text{nonparametric}} = x_{(n)} \tag{TG section 3.6}$$

Here $d_{2\max}$ is the $(1-\alpha)$ critical value of the maximum Studentized
distance — the extreme value of the same statistic Grubbs' outlier test uses,
computed under the null hypothesis of no outlier (Singh 1993, "max-MD"
critical values). It plays the role the tolerance factor $K$ plays for the UTL.

The nonparametric USL is only meaningful on outlier-free, single-population
data sets; the TG states this caveat and OpenUCL prints it verbatim on the
sheet.

## Algorithm

- $d_{2\max}$ is computed with the **exact closed form** of the Grubbs-type
  critical value with the $\alpha/n$ multiplicity adjustment. No tables and no
  tabulated interpolation are involved: the value is an evaluated formula for
  any $n$ and any $\alpha$, on the same philosophy as the exact tolerance
  factor (see [05-btv.md](05-btv.md)).
- The lognormal USL applies the normal-form equation to the log-transformed
  data and back-transforms; the gamma USLs use the cube-root (WH) and
  fourth-root (HW) transformed scales, as for the other gamma limits.

## OpenUCL specifics

The `btv` command prints the USLs in the section `UPPER SIMULTANEOUS LIMITS`
(see the example below). Options: `--alpha` (confidence, default 0.05) and
`--coverage` (default 0.95); the future-`k` option does not affect USLs. The
section appears only when the detected data support the corresponding scale
(e.g., the gamma rows require all-positive data and the gamma fit). The sheet
records the input file hash and the generation timestamp, so the example below
is reproducible from the repository.

## Example — real run

`btv` on the ProUCL sample data set `silver-data.xls` (n = 56, all detected),
packed input; relevant sections of the sheet (OpenUCL 0.15.0):

```
 UPPER TOLERANCE LIMITS
   limit                            value   reference
   ----------------------------------------------------------------------------------------------
   tolerance factor K               2.03774 exact noncentral-t (computed, not tabled)
   UTL95-95 normal                  168.675 TG eq. 3-6

 UPPER SIMULTANEOUS LIMITS
   limit                   value   reference
   ----------------------------------------------------------------------------------------------
   d2max (for USL)         3.00073 Singh (1993) max-MD critical, closed form (Grubbs 1950)
   USL normal              241.087 TG eq. 3-20
   USL lognormal           343.341 TG eq. 3-21
   USL gamma WH            129.783 TG section 3.6 (cube-root scale)
   USL gamma HW            131.606 TG section 3.6 (fourth-root scale)
   USL nonparametric (max) 560     TG section 3.6; use only on outlier-free single populations
```

## Validation anchors

| quantity | data set | OpenUCL | ProUCL 5.2 | note |
|---|---|---|---|---|
| d2max (n = 56) | silver-data.xls | 3.00073 | 3.001 | live output |
| USL normal | silver-data.xls | 241.087 | 241.1 | live output |
| USL lognormal | silver-data.xls | 343.341 | 343.3 | live output |
| USL gamma WH | silver-data.xls | 129.783 | 129.8 | live output |
| USL gamma HW | silver-data.xls | 131.606 | 131.6 | live output |
| USL nonparametric | silver-data.xls | 560 | 560 | sample maximum |
| USL nonparametric | Lead, TG Table 3-5 (n = 24) | 109 | 109 | TG worked example |

All parametric anchors agree with live ProUCL output to display rounding; the
goldens are locked in the test suite (which runs on CI with the data embedded).

## Deviations

- **d2max**: OpenUCL evaluates the exact closed form; ProUCL uses the
  Singh (1993) critical values. Measured difference at n = 56: 3.00073 vs
  3.001 (below display precision of the ProUCL sheet, i.e. < 0.01%).
- **Tolerance factor K** (enters the neighboring UTL, printed for context on
  the same sheet): OpenUCL computes the exact noncentral-t factor; ProUCL uses
  the Natrella approximation beyond n = 30. At n = 56 the approximation sits
  0.3% below the exact value (2.032 vs 2.03774). See [05-btv.md](05-btv.md).
- No deviation for the lognormal, gamma WH/HW and nonparametric forms beyond
  the shared $d_{2\max}$ value.
