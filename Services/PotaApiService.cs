using POTAPlanner.Models;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace POTAPlanner.Services;

public sealed class PotaApiService
{
    private const string CanadaParksUrl = "https://api.pota.app/program/parks/CA";
    private static readonly HttpClient Client = CreateHttpClient();

    public async Task<List<Park>> DownloadCanadaParksAsync()
    {
        using var response = await Client.GetAsync(CanadaParksUrl);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var parksElement = GetParksElement(document.RootElement);
        var parks = new List<Park>();

        foreach (var item in parksElement.EnumerateArray())
        {
            string reference = GetString(item, "reference");
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            parks.Add(new Park
            {
                Reference = reference,
                Name = GetString(item, "name"),
                Latitude = GetDouble(item, "latitude"),
                Longitude = GetDouble(item, "longitude"),
                Grid = GetString(item, "grid"),
                LocationDescription = GetString(item, "locationDesc"),
                Attempts = GetInt(item, "attempts"),
                Activations = GetInt(item, "activations"),
                QSOs = GetInt(item, "qsos"),
                MyActivations = GetInt(item, "my_activations"),
                MyHuntedQsos = GetInt(item, "my_hunted_qsos")
            });
        }

        if (parks.Count == 0)
            throw new InvalidOperationException("POTA returned no Canada parks. Use Update from CSV instead.");

        return parks;
    }

    public async Task<HashSet<string>> FindActivatedParkReferencesAsync(
        string callsign,
        IEnumerable<Park> parks,
        IProgress<string>? progress = null)
    {
        string normalizedCallsign = NormalizeCallsign(callsign);
        if (string.IsNullOrWhiteSpace(normalizedCallsign))
            throw new InvalidOperationException("Enter your callsign first.");

        var references = parks
            .Select(park => park.Reference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var activatedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < references.Count; index++)
        {
            string reference = references[index];
            progress?.Report($"Checking {index + 1:N0} of {references.Count:N0}: {reference}");

            string url = $"https://api.pota.app/park/activations/{Uri.EscapeDataString(reference)}?count=all";
            using var response = await Client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var activations = GetActivationsElement(document.RootElement);
            if (activations.EnumerateArray().Any(activation =>
                    NormalizeCallsign(GetString(activation, "activeCallsign")) == normalizedCallsign))
            {
                activatedReferences.Add(reference);
            }

            // Keep requests to POTA's public API deliberately modest.
            if (index < references.Count - 1)
                await Task.Delay(350);
        }

        return activatedReferences;
    }

    private static JsonElement GetParksElement(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        if (TryGetProperty(root, "parks", out var parks) && parks.ValueKind == JsonValueKind.Array)
            return parks;

        throw new InvalidOperationException("The POTA response did not contain a park list.");
    }

    private static JsonElement GetActivationsElement(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        if (TryGetProperty(root, "activations", out var activations)
            && activations.ValueKind == JsonValueKind.Array)
            return activations;

        throw new InvalidOperationException("The POTA response did not contain activation history for this park.");
    }

    private static string NormalizeCallsign(string callsign)
    {
        string normalized = callsign.Trim().ToUpperInvariant();
        int suffixIndex = normalized.IndexOf('/');
        return suffixIndex >= 0 ? normalized[..suffixIndex] : normalized;
    }

    private static string GetString(JsonElement item, string propertyName)
    {
        if (!TryGetProperty(item, propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return string.Empty;

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static int GetInt(JsonElement item, string propertyName)
    {
        if (!TryGetProperty(item, propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            return number;

        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private static double GetDouble(JsonElement item, string propertyName)
    {
        if (!TryGetProperty(item, propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            return number;

        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("POTAPlanner/1.4 (personal trip planning application)");
        return client;
    }
}
