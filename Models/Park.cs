namespace POTAPlanner.Models;

public class Park
{
    public string Reference { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string Grid { get; set; } = string.Empty;

    public string LocationDescription { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public int Activations { get; set; }

    public int QSOs { get; set; }

    public int MyActivations { get; set; }

    public int MyHuntedQsos { get; set; }

    public string GoogleMapsUrl =>
        $"https://www.google.com/maps?q={Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public override string ToString()
    {
        return $"{Reference} - {Name}";
    }
}