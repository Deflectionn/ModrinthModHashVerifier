using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var noReport = args.Contains("--no-report");
var pathArgs = args.Where(a => a != "--no-report").ToArray();

if (pathArgs.Length > 0 && (pathArgs[0] == "-h" || pathArgs[0] == "--help" || pathArgs[0] == "/?"))
{
    Console.WriteLine("Usage: modhash <mods folder>");
    Console.WriteLine("Example: modhash \"C:\\Users\\Player\\AppData\\Roaming\\.minecraft\\mods\"");
    Console.WriteLine("Run without arguments to be asked for the folder.");
    Console.WriteLine("Add --no-report to skip saving a report file.");
    return 0;
}

string folder;

if (pathArgs.Length > 0)
{
    folder = string.Join(" ", pathArgs).Trim().Trim('"');
}
else
{
    Console.Write("Paste the mods folder path: ");
    folder = (Console.ReadLine() ?? "").Trim().Trim('"');
}

if (!Directory.Exists(folder))
{
    Console.WriteLine("Folder not found.");
    return 1;
}

var entries = new List<Entry>();

foreach (var path in Directory.EnumerateFiles(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
{
    try
    {
        var isJar = path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
        var isZip = HasZipHeader(path);

        if (!isJar && !isZip)
        {
            continue;
        }

        var entry = new Entry
        {
            Name = Path.GetFileName(path),
            Hash = HashFile(path),
            Meta = isZip ? ReadMeta(path) : ""
        };

        if (isJar && !isZip)
        {
            entry.Note = "has .jar extension but is not an archive";
        }
        else if (!isJar && isZip)
        {
            entry.Note = "archive with a non-jar extension";
        }

        entries.Add(entry);
    }
    catch (Exception ex)
    {
        entries.Add(new Entry { Name = Path.GetFileName(path), Hash = "", Meta = "", Note = "could not be read: " + ex.Message });
    }
}

if (entries.Count == 0)
{
    Console.WriteLine("No jar files or archives found in that folder.");
    return 0;
}

var found = new Dictionary<string, (string ProjectId, string Version)>();
var titles = new Dictionary<string, string>();
var online = true;
var networkError = "";

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("ModHashVerifier/1.0 (pc-check tool)");

try
{
    var hashes = entries.Where(e => e.Hash != "").Select(e => e.Hash).Distinct().ToArray();
    var body = JsonSerializer.Serialize(new { hashes, algorithm = "sha1" });

    using var response = await http.PostAsync(
        "https://api.modrinth.com/v2/version_files",
        new StringContent(body, Encoding.UTF8, "application/json"));

    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    foreach (var property in doc.RootElement.EnumerateObject())
    {
        var projectId = property.Value.TryGetProperty("project_id", out var p) ? p.GetString() ?? "" : "";
        var version = property.Value.TryGetProperty("version_number", out var v) ? v.GetString() ?? "" : "";
        found[property.Name.ToLowerInvariant()] = (projectId, version);
    }
}
catch (Exception ex)
{
    online = false;
    networkError = ex.Message;
}

if (online && found.Count > 0)
{
    try
    {
        var ids = found.Values.Select(f => f.ProjectId).Where(i => i != "").Distinct().ToArray();
        var url = "https://api.modrinth.com/v2/projects?ids=" + Uri.EscapeDataString(JsonSerializer.Serialize(ids));
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url));

        foreach (var project in doc.RootElement.EnumerateArray())
        {
            var id = project.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            var title = project.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            if (id != "")
            {
                titles[id] = title;
            }
        }
    }
    catch
    {
    }
}

var lines = new List<string>();
var verified = 0;
var unverified = 0;

lines.Add("Mod hash report");
lines.Add("Folder: " + folder);
lines.Add("Run (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
lines.Add("Files checked: " + entries.Count);
lines.Add("");

if (!online)
{
    lines.Add("Could not reach Modrinth: " + networkError);
    lines.Add("No file could be verified. This may be a network problem or a blocked connection.");
    lines.Add("");
}

foreach (var entry in entries)
{
    string status;

    if (!online)
    {
        status = "NO NETWORK";
    }
    else if (entry.Hash != "" && found.TryGetValue(entry.Hash, out var match))
    {
        status = "VERIFIED";
        verified++;
        var title = titles.TryGetValue(match.ProjectId, out var t) ? t : match.ProjectId;
        entry.Project = title + " " + match.Version;
    }
    else
    {
        status = "UNVERIFIED";
        unverified++;
    }

    lines.Add("[" + status + "] " + entry.Name);

    if (entry.Project != "")
    {
        lines.Add("    Modrinth: " + entry.Project);
    }

    if (entry.Meta != "")
    {
        lines.Add("    Metadata: " + entry.Meta);
    }

    if (entry.Note != "")
    {
        lines.Add("    Note: " + entry.Note);
    }

    if (entry.Hash != "")
    {
        lines.Add("    SHA-1: " + entry.Hash);
    }
}

lines.Add("");
lines.Add("Verified: " + verified + "  Unverified: " + unverified);
lines.Add("UNVERIFIED means the hash was not found on Modrinth. It does not mean the file is a cheat.");
lines.Add("Review unverified files in a decompiler before drawing any conclusion.");
lines.Add("A clean result only means no artifact meeting current criteria was found in the evidence collected.");

foreach (var line in lines)
{
    Console.WriteLine(line);
}

try
{
    if (noReport)
    {
        return 0;
    }

    var reportPath = Path.Combine(AppContext.BaseDirectory, "modhash-report-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
    File.WriteAllLines(reportPath, lines);
    Console.WriteLine();
    Console.WriteLine("Report saved: " + reportPath);
}
catch
{
}

return 0;

static bool HasZipHeader(string path)
{
    using var stream = File.OpenRead(path);
    var header = new byte[4];
    var read = stream.Read(header, 0, 4);
    return read == 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
}

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
}

static string ReadMeta(string path)
{
    try
    {
        using var zip = ZipFile.OpenRead(path);

        var fabric = zip.GetEntry("fabric.mod.json");
        if (fabric != null)
        {
            using var doc = JsonDocument.Parse(ReadText(fabric));
            var id = doc.RootElement.TryGetProperty("id", out var i) ? i.GetString() : null;
            var version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            return "fabric " + id + " " + version;
        }

        var quilt = zip.GetEntry("quilt.mod.json");
        if (quilt != null)
        {
            using var doc = JsonDocument.Parse(ReadText(quilt));
            if (doc.RootElement.TryGetProperty("quilt_loader", out var loader))
            {
                var id = loader.TryGetProperty("id", out var i) ? i.GetString() : null;
                var version = loader.TryGetProperty("version", out var v) ? v.GetString() : null;
                return "quilt " + id + " " + version;
            }
        }

        var toml = zip.GetEntry("META-INF/mods.toml") ?? zip.GetEntry("META-INF/neoforge.mods.toml");
        if (toml != null)
        {
            var text = ReadText(toml);
            var id = Regex.Match(text, "modId\\s*=\\s*\"([^\"]+)\"");
            var version = Regex.Match(text, "(?m)^\\s*version\\s*=\\s*\"([^\"]+)\"");
            return "forge " + (id.Success ? id.Groups[1].Value : "") + " " + (version.Success ? version.Groups[1].Value : "");
        }

        return "no mod metadata found";
    }
    catch
    {
        return "metadata unreadable";
    }
}

static string ReadText(ZipArchiveEntry entry)
{
    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
    return reader.ReadToEnd();
}

class Entry
{
    public string Name = "";
    public string Hash = "";
    public string Meta = "";
    public string Note = "";
    public string Project = "";
}
