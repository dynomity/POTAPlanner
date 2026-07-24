using ClosedXML.Excel;
using POTAPlanner.Models;
using SkiaSharp;
using System.IO;

namespace POTAPlanner.Services;

public class ExcelService
{
    public void Export(string filename, IEnumerable<Park> parks)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Parks");

        ws.Cell(1, 1).Value = "Reference";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Grid";
        ws.Cell(1, 4).Value = "Activations";
        ws.Cell(1, 5).Value = "QSOs";
        ws.Cell(1, 6).Value = "Attempts";
        ws.Cell(1, 7).Value = "Latitude";
        ws.Cell(1, 8).Value = "Longitude";
        ws.Cell(1, 9).Value = "Google Maps";

        var row = 2;
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
            AddGoogleMapsLink(ws.Cell(row, 9), park);
            row++;
        }

        StyleHeader(ws.Range(1, 1, 1, 9));
        ws.SheetView.FreezeRows(1);

        if (row > 2)
        {
            var table = ws.Range(1, 1, row - 1, 9).CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;
        }

        ws.Columns().AdjustToContents();
        ConfigurePrint(ws, row - 1, 9);
        workbook.SaveAs(filename);
    }

    public void ExportRoute(
        string filename,
        IEnumerable<RouteStop> stops,
        string startLocation,
        string destinationLocation,
        RoutePlan route)
    {
        var routeStops = stops
            .OrderBy(stop => stop.RoutePositionKm)
            .ThenBy(stop => stop.DistanceFromRouteKm)
            .ToList();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Route Plan");

        ws.Cell(1, 1).Value = "POTA Planner Route Plan";
        ws.Range(1, 1, 1, 11).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;

        ws.Cell(3, 1).Value = "Start";
        ws.Cell(3, 2).Value = startLocation;
        ws.Cell(4, 1).Value = "Destination";
        ws.Cell(4, 2).Value = destinationLocation;
        ws.Cell(5, 1).Value = "Planned parks";
        ws.Cell(5, 2).Value = routeStops.Count;
        ws.Cell(6, 1).Value = "Route distance";
        ws.Cell(6, 2).Value = route.DistanceKm;
        ws.Cell(6, 2).Style.NumberFormat.Format = "0.0 \"km\"";
        ws.Cell(7, 1).Value = "Drive time";
        ws.Cell(7, 2).Value = route.DurationMinutes / 60d;
        ws.Cell(7, 2).Style.NumberFormat.Format = "0.0 \"hours\"";
        ws.Range(3, 1, 7, 1).Style.Font.Bold = true;

        const int headerRow = 9;
        var headers = new[]
        {
            "Stop", "Reference", "Name", "Grid", "Activations", "From Start",
            "To Destination", "From Route", "Latitude", "Longitude", "Google Maps"
        };
        for (var column = 0; column < headers.Length; column++)
            ws.Cell(headerRow, column + 1).Value = headers[column];

        var row = headerRow + 1;
        var stopNumber = 1;
        foreach (var stop in routeStops)
        {
            var park = stop.Park;
            ws.Cell(row, 1).Value = stopNumber++;
            ws.Cell(row, 2).Value = park.Reference;
            ws.Cell(row, 3).Value = park.Name;
            ws.Cell(row, 4).Value = park.Grid;
            ws.Cell(row, 5).Value = park.Activations;
            ws.Cell(row, 6).Value = stop.RoutePositionKm;
            ws.Cell(row, 6).Style.NumberFormat.Format = "0.0 \"km\"";
            ws.Cell(row, 7).Value = Math.Max(0, stop.RouteDistanceKm - stop.RoutePositionKm);
            ws.Cell(row, 7).Style.NumberFormat.Format = "0.0 \"km\"";
            ws.Cell(row, 8).Value = stop.DistanceFromRouteKm;
            ws.Cell(row, 8).Style.NumberFormat.Format = "0.0 \"km\"";
            ws.Cell(row, 9).Value = park.Latitude;
            ws.Cell(row, 10).Value = park.Longitude;
            AddGoogleMapsLink(ws.Cell(row, 11), park);
            row++;
        }

        StyleHeader(ws.Range(headerRow, 1, headerRow, 11));
        if (routeStops.Count > 0)
        {
            var table = ws.Range(headerRow, 1, row - 1, 11).CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;
        }

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();

        var mapTitleRow = row + 2;
        ws.Cell(mapTitleRow, 1).Value = "Route map";
        ws.Range(mapTitleRow, 1, mapTitleRow, 11).Merge();
        ws.Cell(mapTitleRow, 1).Style.Font.Bold = true;
        ws.Cell(mapTitleRow, 1).Style.Font.FontSize = 13;

        using var mapImage = CreateRouteMap(route, routeStops);
        ws.AddPicture(mapImage)
            .MoveTo(ws.Cell(mapTitleRow + 1, 1))
            .WithSize(1050, 500);

        ConfigurePrint(ws, mapTitleRow + 28, 11);
        workbook.SaveAs(filename);
    }

    private static void AddGoogleMapsLink(IXLCell cell, Park park)
    {
        cell.FormulaA1 = $"HYPERLINK(\"{park.GoogleMapsUrl}\",\"Open Map\")";
        cell.Style.Font.FontColor = XLColor.Blue;
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
    }

    private static void StyleHeader(IXLRange header)
    {
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
    }

    private static void ConfigurePrint(IXLWorksheet ws, int lastRow, int lastColumn)
    {
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.CenterHorizontally = true;
        ws.PageSetup.Margins.Left = 0.25;
        ws.PageSetup.Margins.Right = 0.25;
        ws.PageSetup.Margins.Top = 0.35;
        ws.PageSetup.Margins.Bottom = 0.35;
        ws.PageSetup.PrintAreas.Add(1, 1, lastRow, lastColumn);
    }

    private static MemoryStream CreateRouteMap(RoutePlan route, IReadOnlyList<RouteStop> stops)
    {
        const int width = 1050;
        const int height = 500;
        const int margin = 42;

        var points = route.Points
            .Append(route.Start)
            .Append(route.Destination)
            .Concat(stops.Select(stop => new RoutePoint(stop.Park.Latitude, stop.Park.Longitude)))
            .ToList();

        var minLatitude = points.Min(point => point.Latitude);
        var maxLatitude = points.Max(point => point.Latitude);
        var minLongitude = points.Min(point => point.Longitude);
        var maxLongitude = points.Max(point => point.Longitude);

        var latitudePadding = Math.Max((maxLatitude - minLatitude) * 0.08, 0.08);
        var longitudePadding = Math.Max((maxLongitude - minLongitude) * 0.08, 0.08);
        minLatitude -= latitudePadding;
        maxLatitude += latitudePadding;
        minLongitude -= longitudePadding;
        maxLongitude += longitudePadding;

        using var bitmap = new SKBitmap(width, height);
        using var graphics = new SKCanvas(bitmap);
        graphics.Clear(new SKColor(247, 249, 252));

        using var gridPen = new SKPaint { Color = new SKColor(220, 226, 235), StrokeWidth = 1, IsAntialias = true, Style = SKPaintStyle.Stroke };
        for (var i = 0; i <= 4; i++)
        {
            var x = margin + i * (width - 2 * margin) / 4f;
            var y = margin + i * (height - 2 * margin) / 4f;
            graphics.DrawLine(x, margin, x, height - margin, gridPen);
            graphics.DrawLine(margin, y, width - margin, y, gridPen);
        }

        SKPoint Project(RoutePoint point) => new(
            (float)(margin + (point.Longitude - minLongitude) / (maxLongitude - minLongitude) * (width - 2 * margin)),
            (float)(height - margin - (point.Latitude - minLatitude) / (maxLatitude - minLatitude) * (height - 2 * margin)));

        var routePoints = route.Points.Select(Project).ToArray();
        if (routePoints.Length > 1)
        {
            using var routePen = new SKPaint { Color = new SKColor(38, 115, 209), StrokeWidth = 4, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
            graphics.DrawPoints(SKPointMode.Polygon, routePoints, routePen);
        }

        DrawEndpoint(graphics, Project(route.Start), new SKColor(44, 139, 67), "Start", -36);
        DrawEndpoint(graphics, Project(route.Destination), new SKColor(204, 55, 55), "Destination", 14);

        using var stopBrush = new SKPaint { Color = new SKColor(35, 105, 190), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stopTextBrush = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 10, Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), TextAlign = SKTextAlign.Center };
        for (var index = 0; index < stops.Count; index++)
        {
            var point = Project(new RoutePoint(stops[index].Park.Latitude, stops[index].Park.Longitude));
            graphics.DrawCircle(point, 10, stopBrush);
            var number = (index + 1).ToString();
            graphics.DrawText(number, point.X, point.Y + 4, stopTextBrush);
        }

        using var borderPen = new SKPaint { Color = new SKColor(160, 170, 183), StrokeWidth = 1, IsAntialias = true, Style = SKPaintStyle.Stroke };
        graphics.DrawRect(0, 0, width - 1, height - 1, borderPen);

        var stream = new MemoryStream();
        using var image = SKImage.FromBitmap(bitmap);
        using var imageData = image.Encode(SKEncodedImageFormat.Png, 100);
        imageData.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }

    private static void DrawEndpoint(SKCanvas graphics, SKPoint point, SKColor color, string label, int labelOffset)
    {
        using var brush = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textBrush = new SKPaint { Color = new SKColor(40, 40, 40), IsAntialias = true, TextSize = 12, Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) };
        graphics.DrawCircle(point, 9, brush);
        graphics.DrawText(label, point.X + 12, point.Y + labelOffset, textBrush);
    }
}
