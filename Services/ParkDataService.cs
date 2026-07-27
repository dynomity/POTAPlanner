using System;
using System.IO;

namespace POTAPlanner.Services;

public sealed class ParkDataService
{
    private const string DataFileName = "Canada (CA).csv";

    public string GetPreferredDataPath() =>
        File.Exists(LocalDataPath) ? LocalDataPath : BundledDataPath;

    public string GetDataSourceDescription() =>
        File.Exists(LocalDataPath) ? "local updated Canada dataset" : "bundled Canada dataset";

    public void UpdateFrom(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("The selected CSV file could not be found.", sourceFilePath);

        Directory.CreateDirectory(AppDataDirectory);
        File.Copy(sourceFilePath, LocalDataPath, overwrite: true);
    }

    public void UpdateFromParks(IEnumerable<Models.Park> parks, CsvService csvService)
    {
        Directory.CreateDirectory(AppDataDirectory);
        csvService.SaveParks(LocalDataPath, parks);
    }

    private static string BundledDataPath => Path.Combine(AppContext.BaseDirectory, "Data", DataFileName);

    private static string LocalDataPath => Path.Combine(AppDataDirectory, DataFileName);

    private static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "POTAPlanner");
}
