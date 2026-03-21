using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using InterplanetaryManeuver.App.Models;

namespace InterplanetaryManeuver.App.Services;

public sealed class HorizonsEphemerisService
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://ssd.jpl.nasa.gov/")
    };

    private readonly string _cacheDir;

    public HorizonsEphemerisService()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InterplanetaryManeuver",
            "Ephemerides");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<EphemerisState> GetStateAsync(string bodyId, string bodyName, DateTime epochUtc, CancellationToken cancellationToken)
    {
        string stamp = epochUtc.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
        string cachePath = Path.Combine(_cacheDir, $"{bodyId}_{stamp}.json");

        if (File.Exists(cachePath))
        {
            await using var cacheStream = File.OpenRead(cachePath);
            var cached = await JsonSerializer.DeserializeAsync<EphemerisState>(cacheStream, cancellationToken: cancellationToken);
            if (cached is not null)
                return cached;
        }

        string start = epochUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string stop = epochUtc.AddMinutes(1).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string url =
            "api/horizons.api" +
            "?format=json" +
            $"&COMMAND='{Uri.EscapeDataString(bodyId)}'" +
            "&OBJ_DATA='NO'" +
            "&MAKE_EPHEM='YES'" +
            "&EPHEM_TYPE='VECTORS'" +
            "&CENTER='500@0'" +
            $"&START_TIME='{Uri.EscapeDataString(start)}'" +
            $"&STOP_TIME='{Uri.EscapeDataString(stop)}'" +
            "&STEP_SIZE='1%20m'" +
            "&REF_SYSTEM='ICRF'" +
            "&REF_PLANE='FRAME'" +
            "&OUT_UNITS='KM-S'" +
            "&VEC_TABLE='2'" +
            "&CSV_FORMAT='YES'";

        try
        {
            using var response = await HttpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            string raw = document.RootElement.GetProperty("result").GetString()
                ?? throw new InvalidOperationException("Horizons API returned an empty result.");

            EphemerisState state = ParseResponse(raw, bodyId, bodyName, epochUtc);

            await using var outStream = File.Create(cachePath);
            await JsonSerializer.SerializeAsync(outStream, state, cancellationToken: cancellationToken);

            return state;
        }
        catch when (File.Exists(cachePath))
        {
            await using var cacheStream = File.OpenRead(cachePath);
            var cached = await JsonSerializer.DeserializeAsync<EphemerisState>(cacheStream, cancellationToken: cancellationToken);
            if (cached is not null)
                return cached;

            throw;
        }
    }

    private static EphemerisState ParseResponse(string raw, string bodyId, string bodyName, DateTime epochUtc)
    {
        int start = raw.IndexOf("$$SOE", StringComparison.Ordinal);
        int end = raw.IndexOf("$$EOE", StringComparison.Ordinal);
        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException("Horizons API response does not contain vector data.");

        string payload = raw[(start + 5)..end];
        string? line = payload
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static x => x.Trim())
            .FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x));

        if (line is null)
            throw new InvalidOperationException("Horizons API response does not contain vector rows.");

        string[] parts = line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 8)
            throw new InvalidOperationException("Horizons API vector row has an unexpected format.");

        double xKm = double.Parse(parts[2], CultureInfo.InvariantCulture);
        double yKm = double.Parse(parts[3], CultureInfo.InvariantCulture);
        double zKm = double.Parse(parts[4], CultureInfo.InvariantCulture);
        double vxKmS = double.Parse(parts[5], CultureInfo.InvariantCulture);
        double vyKmS = double.Parse(parts[6], CultureInfo.InvariantCulture);
        double vzKmS = double.Parse(parts[7], CultureInfo.InvariantCulture);

        return new EphemerisState
        {
            Id = bodyId,
            Name = bodyName,
            EpochUtc = epochUtc,
            X = xKm * 1000.0,
            Y = yKm * 1000.0,
            Z = zKm * 1000.0,
            Vx = vxKmS * 1000.0,
            Vy = vyKmS * 1000.0,
            Vz = vzKmS * 1000.0,
        };
    }
}
