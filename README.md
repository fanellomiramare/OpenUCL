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

**Phase 0 — project scaffold.** Development has started; nothing statistically useful yet.

Roadmap:

1. Repository, CI, license, validation harness against ProUCL 5.2 outputs
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
the ProUCL binaries.

## License

[Apache-2.0](LICENSE). The ProUCL User Guide and Technical Guide are works of the
U.S. Federal Government (public domain) and are used here as the methodological reference.
