using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Charts;

/// <summary>
/// 将 NormalizedChartData 写入嵌入的 Excel 工作簿，并更新单元格范围。
/// </summary>
internal static class EmbeddedChartWorkbookWriter
{
    internal static void Write(
        ChartPart chartPart,
        NormalizedChartData data,
        ChartDataDefinition definition)
    {
        EmbeddedPackagePart? embeddedPart = null;
        foreach (IdPartPair partPair in chartPart.Parts)
        {
            if (partPair.OpenXmlPart is EmbeddedPackagePart ep)
            {
                embeddedPart = ep;
                break;
            }
        }

        if (embeddedPart is null)
            throw new InvalidOperationException("图表没有嵌入工作簿，无法写入。");

        // Read the embedded workbook stream
        using Stream workbookStream = embeddedPart.GetStream(FileMode.Open, FileAccess.ReadWrite);
        using SpreadsheetDocument spreadsheet = SpreadsheetDocument.Open(workbookStream, true);

        WorkbookPart workbookPart = spreadsheet.WorkbookPart
            ?? throw new InvalidOperationException("嵌入工作簿缺少 WorkbookPart。");

        // Use the first worksheet or find by sheet name from formula
        Sheet? sheet = workbookPart.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault()
            ?? throw new InvalidOperationException("嵌入工作簿没有工作表。");

        string sheetId = sheet.Id?.Value ?? string.Empty;
        WorksheetPart? worksheetPart = workbookPart.GetPartById(sheetId) as WorksheetPart
            ?? throw new InvalidOperationException("找不到工作表部件。");

        SheetData sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>()
            ?? throw new InvalidOperationException("工作表没有数据。");

        string sheetName = sheet.Name?.Value ?? "Sheet1";

        // Write category column
        WriteColumn(
            sheetData,
            sheetName,
            definition.Category.StartCell,
            definition.Category.EndCell,
            data.Categories.Select(c => c ?? string.Empty).ToList());

        // Write each series
        foreach (var seriesDef in definition.Series)
        {
            var normSeries = data.Series.FirstOrDefault(s => s.SeriesIndex == seriesDef.SeriesIndex);
            if (normSeries is null) continue;

            // Series name
            if (seriesDef.NameCell is not null)
            {
                WriteCellValue(sheetData, sheetName, seriesDef.NameCell, normSeries.Name, CellValues.String);
            }

            // Series values
            var strValues = normSeries.Values
                .Select(v => v?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .ToList();
            WriteColumn(
                sheetData,
                sheetName,
                seriesDef.ValueStartCell,
                seriesDef.ValueEndCell,
                strValues);
        }

        // Update calculation chain if present
        var calcChain = workbookPart.CalculationChainPart?.CalculationChain;
        if (calcChain is not null)
        {
            calcChain.RemoveAllChildren();
        }

        worksheetPart.Worksheet.Save();
        workbookPart.Workbook?.Save();
    }

    private static void WriteColumn(
        SheetData sheetData,
        string sheetName,
        string? startCell,
        string? endCell,
        IReadOnlyList<string> values)
    {
        if (string.IsNullOrWhiteSpace(startCell)) return;

        (string col, int startRow) = ParseCellReference(startCell);
        int originalEndRow = startRow + (endCell is not null
            ? ParseCellReference(endCell).row - startRow
            : values.Count - 1);
        int oldRowCount = Math.Max(0, originalEndRow - startRow + 1);

        // Write new values
        for (int i = 0; i < values.Count; i++)
        {
            string cellRef = $"{col}{startRow + i}";
            string value = values[i];
            WriteCellValue(sheetData, sheetName, cellRef, value,
                decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    ? CellValues.Number : CellValues.String);
        }

        // Clear old cells beyond new data
        if (values.Count < oldRowCount)
        {
            for (int i = values.Count; i < oldRowCount; i++)
            {
                string cellRef = $"{col}{startRow + i}";
                WriteCellValue(sheetData, sheetName, cellRef, string.Empty, CellValues.String);
            }
        }
    }

    private static void WriteCellValue(
        SheetData sheetData,
        string sheetName,
        string cellReference,
        string value,
        CellValues cellType)
    {
        // Find existing row or create one
        (string col, int rowNum) = ParseCellReference(cellReference);

        Row? row = sheetData.Elements<Row>()
            .FirstOrDefault(r => r.RowIndex?.Value == (uint)rowNum);
        if (row is null)
        {
            row = new Row { RowIndex = (uint)rowNum };
            // Insert at correct position
            Row? previousRow = sheetData.Elements<Row>()
                .Where(r => r.RowIndex?.Value < (uint)rowNum)
                .MaxBy(r => r.RowIndex?.Value ?? 0);
            if (previousRow is not null)
                sheetData.InsertAfter(row, previousRow);
            else
                sheetData.InsertAt(row, 0);
        }

        // Find or create cell
        Cell? cell = row.Elements<Cell>()
            .FirstOrDefault(c => c.CellReference?.Value == cellReference);
        if (cell is null)
        {
            cell = new Cell { CellReference = cellReference };
            // Insert at correct position in row
            Cell? previousCell = row.Elements<Cell>()
                .Where(c => CompareCellRefs(c.CellReference?.Value, cellReference) < 0)
                .MaxBy(c => c.CellReference?.Value);
            if (previousCell is not null)
                row.InsertAfter(cell, previousCell);
            else
                row.InsertAt(cell, 0);
        }

        cell.DataType = cellType;
        cell.CellValue = new CellValue(value);
    }

    private static (string col, int row) ParseCellReference(string cellRef)
    {
        cellRef = cellRef.Replace("$", "").ToUpperInvariant();
        int i = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
        string col = cellRef[..i];
        int row = int.Parse(cellRef[i..], CultureInfo.InvariantCulture);
        return (col, row);
    }

    private static int CompareCellRefs(string? a, string? b)
    {
        if (a is null) return -1;
        if (b is null) return 1;
        var (ca, ra) = ParseCellReference(a);
        var (cb, rb) = ParseCellReference(b);
        int colCmp = string.Compare(ca, cb, StringComparison.Ordinal);
        if (colCmp != 0) return colCmp;
        return ra.CompareTo(rb);
    }
}
