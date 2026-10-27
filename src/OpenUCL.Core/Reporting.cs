namespace OpenUCL.Core;

/// <summary>
/// Structured output sheet: sections of aligned tables with notes. The same model
/// drives the CLI text renderer and (later) the GUI/HTML/XLSX renderers, so numbers
/// are guaranteed identical across front-ends.
/// </summary>
public sealed class ReportSheet
{
    public string Title { get; }
    public List<(string Key, string Value)> Header { get; } = new();
    public List<Section> Sections { get; } = new();

    public ReportSheet(string title) => Title = title;

    public Section AddSection(string name) { var s = new Section(name); Sections.Add(s); return s; }

    public sealed class Section
    {
        public string Name { get; }
        public string[]? Columns { get; private set; }
        public List<string[]> Rows { get; } = new();
        public List<string> Notes { get; } = new();
        public HashSet<int> HighlightedRows { get; } = new();

        public Section(string name) => Name = name;

        public Section ColumnsAre(params string[] columns) { Columns = columns; return this; }
        public Section AddRow(params string[] cells) { Rows.Add(cells); return this; }
        public Section AddRowHighlighted(params string[] cells) { Rows.Add(cells); HighlightedRows.Add(Rows.Count - 1); return this; }
        public Section AddNote(string note) { Notes.Add(note); return this; }
    }

    /// <summary>Renders the sheet as fixed-width text (invariant culture, dot decimals).</summary>
    public string RenderText(int width = 100)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(new string('=', width));
        sb.AppendLine(" " + Title);
        sb.AppendLine(new string('-', width));
        foreach (var (key, value) in Header)
            sb.AppendLine($" {key,-12}: {value}");
        sb.AppendLine(new string('-', width));

        foreach (var section in Sections)
        {
            sb.AppendLine($" {section.Name.ToUpperInvariant()}");
            if (section.Columns is not null)
            {
                int[] widths = ComputeWidths(section);
                sb.AppendLine("   " + Layout(section.Columns, widths));
                sb.AppendLine("   " + new string('-', Math.Max(20, width - 6)));
                for (int r = 0; r < section.Rows.Count; r++)
                {
                    string marker = section.HighlightedRows.Contains(r) ? " > " : "   ";
                    sb.AppendLine(marker + Layout(section.Rows[r], widths));
                }
            }
            foreach (var note in section.Notes)
                sb.AppendLine($"   note: {note}");
            sb.AppendLine(new string('-', width));
        }
        return sb.ToString();
    }

    private static int[] ComputeWidths(Section section)
    {
        int count = section.Columns!.Length;
        var widths = new int[count];
        for (int i = 0; i < count; i++) widths[i] = section.Columns[i].Length;
        foreach (var row in section.Rows)
            for (int i = 0; i < Math.Min(count, row.Length); i++)
                widths[i] = Math.Max(widths[i], row[i].Length);
        return widths;
    }

    private static string Layout(string[] cells, int[] widths)
    {
        var parts = new string[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            int w = i < widths.Length ? widths[i] : 0;
            parts[i] = i == cells.Length - 1
                ? cells[i]
                : cells[i].Length > w ? cells[i][..w] : cells[i].PadRight(w);
        }
        return string.Join(" ", parts).TrimEnd();
    }
}
