using ClosedXML.Excel;
using POTAPlanner.Models;

namespace POTAPlanner.Services;

public class ExcelService
{
    public void Export(string filename, IEnumerable<Park> parks)
    {
        using var workbook = new XLWorkbook();

        var ws = workbook.Worksheets.Add("Parks");

        // Headers
        ws.Cell(1, 1).Value = "Reference";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Grid";
        ws.Cell(1, 4).Value = "Activations";
        ws.Cell(1, 5).Value = "QSOs";
        ws.Cell(1, 6).Value = "Attempts";
        ws.Cell(1, 7).Value = "Latitude";
        ws.Cell(1, 8).Value = "Longitude";

        int row = 2;

        foreach (var park in parks)
        {
            ws.Cell(row, 1).Value = park.Reference;
            ws.Cell(row, 2).Value = park.Name;
            ws.Cell(row, 3).Value = park.Grid;
            ws.Cell(row, 4).Value = park.Activations;
            ws.Cell(row, 5).Value = park.QSOs;
            ws.Cell(row, 6).Value = park.Attempts;
            ws.Cell(row, 7).Value = park.Latitude;
            ws.Cell(row, 8).Value = park.Longitude;

            row++;
        }

        // Format header
        var header = ws.Range(1, 1, 1, 8);

        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

        // Freeze header row
        ws.SheetView.FreezeRows(1);

        // Create Excel table
        if (row > 2)
        {
            var table = ws.Range(1, 1, row - 1, 8).CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;
        }

        ws.Columns().AdjustToContents();

        workbook.SaveAs(filename);
    }
}