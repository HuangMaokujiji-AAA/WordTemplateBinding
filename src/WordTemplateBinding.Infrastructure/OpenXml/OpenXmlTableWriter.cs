using System.Collections;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>使用模板首个数据行的样式重复填充 Word 表格。</summary>
internal static class OpenXmlTableWriter
{
    public static void Write(
        MainDocumentPart mainPart,
        TableTemplateItem tableItem,
        object? value,
        TableBindingMapping mapping)
    {
        Body body = mainPart.Document?.Body
            ?? throw new ReportRenderingException("模板缺少主文档正文。");
        List<Table> tables = body.Descendants<Table>()
            .Where(table => !table.Ancestors<Table>().Any())
            .ToList();
        if (tableItem.Locator.TableIndex < 0 ||
            tableItem.Locator.TableIndex >= tables.Count)
        {
            throw new LocatorNotFoundException(tableItem.LocatorId);
        }

        Table table = tables[tableItem.Locator.TableIndex];
        List<TableRow> templateRows = table.Elements<TableRow>().ToList();
        int headerRowCount = Math.Max(1, mapping.HeaderRowCount);
        if (templateRows.Count <= headerRowCount)
        {
            throw new ReportRenderingException(
                $"表格 {tableItem.Title} 缺少可用于复制样式的数据行。");
        }

        TableRow prototype = (TableRow)templateRows[headerRowCount].CloneNode(true);
        foreach (TableRow row in templateRows.Skip(headerRowCount))
        {
            row.Remove();
        }

        IReadOnlyList<JsonElement> rows = EnumerateRows(value)
            .Where(row => MatchesFilter(row, mapping.FilterField, mapping.FilterValue))
            .ToList()
            .AsReadOnly();
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            TableRow output = (TableRow)prototype.CloneNode(true);
            List<TableCell> cells = output.Elements<TableCell>().ToList();
            HashSet<int> mappedColumns = mapping.Columns
                .Select(column => column.ColumnIndex)
                .ToHashSet();
            for (int columnIndex = 0; columnIndex < cells.Count; columnIndex++)
            {
                if (!mappedColumns.Contains(columnIndex))
                {
                    // Never repeat sample values from the prototype in columns
                    // the user intentionally left unmapped.
                    SetCellText(cells[columnIndex], string.Empty);
                }
            }
            foreach (TableColumnBinding column in mapping.Columns)
            {
                if (column.ColumnIndex < 0 || column.ColumnIndex >= cells.Count)
                {
                    continue;
                }

                string text = string.Equals(
                        column.SourceField,
                        "rowNumber",
                        StringComparison.Ordinal)
                    ? (rowIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : ReadValue(rows[rowIndex], column.SourceField) ??
                      column.FallbackValue ?? string.Empty;
                SetCellText(cells[column.ColumnIndex], text);
            }

            table.Append(output);
        }
    }

    private static IReadOnlyList<JsonElement> EnumerateRows(object? value)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("表格数据必须是 JSON 数组。");
            }

            return element.EnumerateArray().Select(item => item.Clone()).ToList().AsReadOnly();
        }

        if (value is IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>()
                .Select(item => JsonSerializer.SerializeToElement(item))
                .ToList()
                .AsReadOnly();
        }

        throw new FormatException("表格数据必须是数组或集合。");
    }

    private static bool MatchesFilter(
        JsonElement row,
        string? filterField,
        string? filterValue)
    {
        if (string.IsNullOrWhiteSpace(filterField) || filterValue is null)
        {
            return true;
        }

        return string.Equals(
            ReadValue(row, filterField),
            filterValue,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadValue(JsonElement row, string field)
    {
        JsonElement current = row;
        foreach (string segment in field.Split(
                     '.',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => current.GetString(),
            JsonValueKind.True => "是",
            JsonValueKind.False => "否",
            _ => current.ToString(),
        };
    }

    private static void SetCellText(TableCell cell, string value)
    {
        List<DocumentFormat.OpenXml.Wordprocessing.Text> texts =
            cell.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().ToList();
        if (texts.Count == 0)
        {
            Paragraph paragraph = cell.Elements<Paragraph>().FirstOrDefault()
                ?? cell.AppendChild(new Paragraph());
            paragraph.AppendChild(new Run())
                .AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text(value)
                {
                    Space = SpaceProcessingModeValues.Preserve,
                });
            return;
        }

        texts[0].Text = value;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        foreach (DocumentFormat.OpenXml.Wordprocessing.Text text in texts.Skip(1))
        {
            text.Text = string.Empty;
        }
    }
}
