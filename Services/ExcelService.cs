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

    public void ExportRoute(
        string filename,
        IEnumerable<RouteStop> stops,
        string startLocation,
        string destinationLocation,
        double routeDistanceKm,
        double routeDurationMinutes)
    {
        var routeStops = stops.ToList();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Route Plan");

        ws.Cell(1, 1).Value = "POTA Planner Route Plan";
        ws.Range(1, 1, 1, 6).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;

        ws.Cell(3, 1).Value = "Start";
        ws.Cell(3, 2).Value = startLocation;
        ws.Cell(4, 1).Value = "Destination";
        ws.Cell(4, 2).Value = destinationLocation;
        ws.Cell(5, 1).Value = "Planned parks";
        ws.Cell(5, 2).Value = routeStops.Count;
        ws.Cell(6, 1).Value = "Route distance";
        ws.Cell(6, 2).Value = routeDistanceKm;
        ws.Cell(6, 2).Style.NumberFormat.Format = "0.0 \"km\"";
        ws.Cell(7, 1).Value = "Drive time";
        ws.Cell(7, 2).Value = routeDurationMinutes / 60d;
        ws.Cell(7, 2).Style.NumberFormat.Format = "0.0 \"hours\"";

        var summaryLabels = ws.Range(3, 1, 7, 1);
        summaryLabels.Style.Font.Bold = true;

        const int headerRow = 9;
        ws.Cell(headerRow, 1).Value = "Stop";
        ws.Cell(headerRow, 2).Value = "Reference";
        ws.Cell(headerRow, 3).Value = "Name";
        ws.Cell(headerRow, 4).Value = "Grid";
        ws.Cell(headerRow, 5).Value = "Activations";
        ws.Cell(headerRow, 6).Value = "From Route";
        ws.Cell(headerRow, 7).Value = "Latitude";
        ws.Cell(headerRow, 8).Value = "Longitude";

        int row = headerRow + 1;
        int stopNumber = 1;
        foreach (var stop in routeStops.OrderBy(stop => stop.RoutePositionKm).ThenBy(stop => stop.DistanceFromRouteKm))
        {
            var park = stop.Park;
            ws.Cell(row, 1).Value = stopNumber++;
            ws.Cell(row, 2).Value = park.Reference;
            ws.Cell(row, 3).Value = park.Name;
            ws.Cell(row, 4).Value = park.Grid;
            ws.Cell(row, 5).Value = park.Activations;
            ws.Cell(row, 6).Value = stop.DistanceFromRouteKm;
            ws.Cell(row, 6).Style.NumberFormat.Format = "0.0 \"km\"";
            ws.Cell(row, 7).Value = park.Latitude;
            ws.Cell(row, 8).Value = park.Longitude;
            row++;
        }

        var header = ws.Range(headerRow, 1, headerRow, 8);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

        if (routeStops.Count > 0)
        {
            var table = ws.Range(headerRow, 1, row - 1, 8).CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;
        }

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();
        workbook.SaveAs(filename);
    }
}
