# Contributing to OpenUCL

Thank you for considering a contribution! A few rules keep this project legally clean
and scientifically trustworthy.

## Clean-room policy (important)

The ProUCL software ("ProUCL 5.2") is distributed with this notice:

> "ProUCL contains embedded licensed software. Any modification of the ProUCL source code
> may violate the embedded licensed software agreements and is expressly forbidden."

Therefore, in this repository:

- **Never decompile, disassemble, or reverse-engineer** the ProUCL binaries.
- **Never copy source code** from ProUCL or from projects that did.
- Implementations must be derived **only** from:
  - the public ProUCL **Technical Guide** and **User Guide** (works of the U.S. Federal
    Government, public domain) — https://www.epa.gov/land-research/proucl-software
  - peer-reviewed papers and standard statistical literature,
  - other open-source statistical libraries under compatible licenses.

If a contribution is based on the Technical Guide, reference the equation/section in a
comment (e.g. `// ProUCL Technical Guide 5.2, eq. 4-3`). That is the level of detail the
guide itself publishes.

## Numerical validation

Every statistical method must ship with unit tests. Where possible, expected values come
from the worked examples in the ProUCL Technical Guide / User Guide, or from independent
references (R, SciPy). Deterministic methods should reproduce ProUCL's published output
to display precision (4–6 significant digits). Bootstrap methods use reproducible seeds.

## Development workflow

1. Fork / branch from `main`.
2. `dotnet build && dotnet test` must pass on all platforms.
3. Keep `OpenUCL.Core` free of UI and filesystem dependencies: pure computation, deterministic.
4. Follow the existing code style (`Nullable` enabled, no warnings allowed on `Release`).

## Reporting bugs

Please include: platform, OpenUCL version, a minimal input file, the command used,
and the full output. For numerical discrepancies with ProUCL, attach both outputs.
