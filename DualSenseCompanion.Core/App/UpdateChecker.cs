using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace DualSenseCompanion.App;

public sealed record UpdateInfo(Version Version, string Url);

//Asks GitHub for the latest release. Sends nothing about the user; only a generic User-Agent, which GitHub requires.
public static class UpdateChecker
{
    public const string RepositoryUrl = "https://github.com/dchosendave/DualSenseCompanion";
    private const string LatestReleaseApi = "https://api.github.com/repos/dchosendave/DualSenseCompanion/releases/latest";

    public static Version CurrentVersion { get; } = Normalize((Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version ?? new Version(0, 0, 0));

    //Null when up to date, offline, or GitHub can't be reached
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DualSenseCompanion", CurrentVersion.ToString()));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.GetAsync(LatestReleaseApi);

            if (!response.IsSuccessStatusCode)
            {
                //404 means no release has been published yet
                Console.WriteLine($"Update check: GitHub answered {(int)response.StatusCode}.");
                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
            string url = json.RootElement.TryGetProperty("html_url", out var html) ? html.GetString() ?? RepositoryUrl : RepositoryUrl;

            if (!TryParseVersion(tag, out var latest))
            {
                Console.WriteLine($"Update check: couldn't read version from tag '{tag}'.");
                return null;
            }

            Console.WriteLine($"Update check: latest release {latest}, running {CurrentVersion}.");
            return IsNewer(latest, CurrentVersion) ? new UpdateInfo(latest, url) : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update check failed: {ex.Message}");
            return null;
        }
    }

    //Accepts "v1.2.3", "1.2.3" and "v1.2"
    public static bool TryParseVersion(string tag, out Version version)
    {
        string text = tag.Trim().TrimStart('v', 'V');
        int dash = text.IndexOf('-');

        if (dash >= 0)
        {
            text = text[..dash];
        }

        if (Version.TryParse(text, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    //Compare major.minor.patch only; missing parts count as 0
    private static Version Normalize(Version v) => new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
