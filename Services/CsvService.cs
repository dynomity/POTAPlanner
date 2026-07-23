using CsvHelper;
using CsvHelper.Configuration;
using POTAPlanner.Models;
using System.Globalization;
using System.IO;

namespace POTAPlanner.Services;

public class CsvService
{
    public List<Park> LoadParks(string filename)
    {
        using var reader = new StreamReader(filename);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = args => args.Header.ToLower().Trim(),
            MissingFieldFound = null,
            HeaderValidated = null
        };

        using var csv = new CsvReader(reader, config);

        csv.Context.RegisterClassMap<ParkMap>();

        return csv.GetRecords<Park>().ToList();
    }
}

public sealed class ParkMap : ClassMap<Park>
{
    public ParkMap()
    {
        Map(m => m.Reference).Name("reference");
        Map(m => m.Name).Name("name");
        Map(m => m.Latitude).Name("latitude");
        Map(m => m.Longitude).Name("longitude");
        Map(m => m.Grid).Name("grid");
        Map(m => m.LocationDescription).Name("locationdesc");
        Map(m => m.Attempts).Name("attempts");
        Map(m => m.Activations).Name("activations");
        Map(m => m.QSOs).Name("qsos");
        Map(m => m.MyActivations).Name("my_activations");
        Map(m => m.MyHuntedQsos).Name("my_hunted_qsos");
    }
}