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

        HashSet<WorksheetPart> touchedWorksheets = new();
        WorksheetContext categorySheet = ResolveWorksheet(
            workbookPart,
            definition.Category.SheetName);
        touchedWorksheets.Add(categorySheet.Part);

        // Write category range (Word commonly stores radar data horizontally).
        WriteRange(
            categorySheet.Data,
            categorySheet.Name,
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
                WorksheetContext nameSheet = ResolveWorksheet(
                    workbookPart,
                    seriesDef.NameSheetName ?? seriesDef.ValueSheetName ?? definition.Category.SheetName);
                touchedWorksheets.Add(nameSheet.Part);
                WriteCellValue(
                    nameSheet.Data,
                    nameSheet.Name,
                    seriesDef.NameCell,
                    normSeries.Name,
                    CellValues.String);
            }

            // Series values
            var strValues = normSeries.Values
                .Select(v => v?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .ToList();
            WorksheetContext valueSheet = ResolveWorksheet(
                workbookPart,
                seriesDef.ValueSheetName ?? definition.Category.SheetName);
            touchedWorksheets.Add(valueSheet.Part);
            WriteRange(
                valueSheet.Data,
                valueSheet.Name,
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

        foreach (WorksheetPart worksheetPart in touchedWorksheets)
            worksheetPart.Worksheet.Save();
        workbookPart.Workbook?.Save();
    }

    private static WorksheetContext ResolveWorksheet(
        WorkbookPart workbookPart,
        string? requestedSheetName)
    {
        List<Sheet> sheets = workbookPart.Workbook?.Sheets?.Elements<Sheet>().ToList()
            ?? new List<Sheet>();
        if (sheets.Count == 0)
            throw new InvalidOperationException("嵌入工作簿没有工作表。");

        string? normalized = NormalizeSheetName(requestedSheetName);
        Sheet? sheet = normalized is null
            ? sheets[0]
            : sheets.FirstOrDefault(candidate => string.Equals(
                candidate.Name?.Value,
                normalized,
                StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
            throw new InvalidOperationException($"嵌入工作簿中找不到工作表 \"{normalized}\"。");

        string sheetId = sheet.Id?.Value
            ?? throw new InvalidOperationException($"工作表 \"{sheet.Name?.Value}\" 缺少关系标识。");
        WorksheetPart part = workbookPart.GetPartById(sheetId) as WorksheetPart
            ?? throw new InvalidOperationException($"找不到工作表 \"{sheet.Name?.Value}\" 的部件。");
        SheetData data = part.Worksheet.GetFirstChild<SheetData>()
            ?? throw new InvalidOperationException($"工作表 \"{sheet.Name?.Value}\" 没有数据。");

        return new WorksheetContext(sheet.Name?.Value ?? "Sheet1", part, data);
    }

    private static string? NormalizeSheetName(string? sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName)) return null;
        string normalized = sheetName.Trim().Trim('\'');
        if (normalized.StartsWith("[", StringComparison.Ordinal))
        {
            int bracket = normalized.IndexOf(']');
            if (bracket >= 0 && bracket < normalized.Length - 1)
                normalized = normalized[(bracket + 1)..];
        }
        return normalized;
    }

    private static void WriteRange(
        SheetData sheetData,
        string sheetName,
        string? startCell,
        string? endCell,
        IReadOnlyList<string> values)
    {
        if (string.IsNullOrWhiteSpace(startCell)) return;

        (string startColumn, int startRow) = ParseCellReference(startCell);
        (string endColumn, int endRow) = endCell is not null
            ? ParseCellReference(endCell)
            : (startColumn, startRow + Math.Max(0, values.Count - 1));
        int startColumnNumber = ToColumnNumber(startColumn);
        int endColumnNumber = ToColumnNumber(endColumn);
        bool horizontal = startRow == endRow && startColumnNumber != endColumnNumber;
        int oldCount = horizontal
            ? Math.Max(1, endColumnNumber - startColumnNumber + 1)
            : Math.Max(1, endRow - startRow + 1);

        // Write new values
        for (int i = 0; i < values.Count; i++)
        {
            string cellRef = horizontal
                ? $"{ToColumnName(startColumnNumber + i)}{startRow}"
                : $"{startColumn}{startRow + i}";
            string value = values[i];
            WriteCellValue(sheetData, sheetName, cellRef, value,
                decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    ? CellValues.Number : CellValues.String);
        }

        // Clear old cells beyond new data
        if (values.Count < oldCount)
        {
            for (int i = values.Count; i < oldCount; i++)
            {
                string cellRef = horizontal
                    ? $"{ToColumnName(startColumnNumber + i)}{startRow}"
                    : $"{startColumn}{startRow + i}";
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
        int colCmp = ToColumnNumber(ca).CompareTo(ToColumnNumber(cb));
        if (colCmp != 0) return colCmp;
        return ra.CompareTo(rb);
    }

    private static int ToColumnNumber(string column)
    {
        int result = 0;
        foreach (char character in column.ToUpperInvariant())
            result = checked(result * 26 + character - 'A' + 1);
        return result;
    }

    private static string ToColumnName(int number)
    {
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
        string result = string.Empty;
        while (number > 0)
        {
            number--;
            result = (char)('A' + number % 26) + result;
            number /= 26;
        }
        return result;
    }

    private sealed record WorksheetContext(
        string Name,
        WorksheetPart Part,
        SheetData Data);
}
