using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModHashGui;

public class ScanResult
{
    public string Name = "";
    public string FullPath = "";
    public string Hash = "";
    public string Meta = "";
    public string Note = "";
    public string Project = "";
    public string Status = "Unverified";
}

public class ScanReport
{
    public List<ScanResult> Items = new();
    public bool Online = true;
    public string NetworkError = "";
}

public static class ModScanner
{
    public static async Task<ScanReport> ScanAsync(string folder, bool recursive, IProgress<(int Done, int Total, string Name)> progress, CancellationToken token)
    {
        var report = new ScanReport();

        var files = await Task.Run(() =>
        {
            var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
            return Directory.EnumerateFiles(folder, "*", options).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }, token);

        await Task.Run(() =>
        {
            var done = 0;

            foreach (var path in files)
            {
                token.ThrowIfCancellationRequested();
                done++;
                progress.Report((done, files.Count, Path.GetFileName(path)));

                try
                {
                    var isJar = path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                    var isZip = HasZipHeader(path);

                    if (!isJar && !isZip)
                    {
                        continue;
                    }

                    var result = new ScanResult
                    {
                        Name = Path.GetRelativePath(folder, path),
                        FullPath = path,
                        Hash = HashFile(path),
                        Meta = isZip ? ReadMeta(path) : ""
                    };

                    if (isJar && !isZip)
                    {
                        result.Note = "has .jar extension but is not an archive";
                    }
                    else if (!isJar && isZip)
                    {
                        result.Note = "archive with a non-jar extension";
                    }

                    report.Items.Add(result);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    report.Items.Add(new ScanResult
                    {
                        Name = Path.GetRelativePath(folder, path),
                        FullPath = path,
                        Note = "could not be read: " + ex.Message
                    });
                }
            }
        }, token);

        if (report.Items.Count == 0)
        {
            return report;
        }

        progress.Report((files.Count, files.Count, "Contacting Modrinth"));

        var found = new Dictionary<string, (string ProjectId, string Version)>();
        var titles = new Dictionary<string, string>();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ModHashVerifier/1.1 (pc-check tool)");

        try
        {
            var hashes = report.Items.Where(i => i.Hash != "").Select(i => i.Hash).Distinct().ToArray();
            var body = JsonSerializer.Serialize(new { hashes, algorithm = "sha1" });

            using var response = await http.PostAsync(
                "https://api.modrinth.com/v2/version_files",
                new StringContent(body, Encoding.UTF8, "application/json"),
                token);

            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var projectId = property.Value.TryGetProperty("project_id", out var p) ? p.GetString() ?? "" : "";
                var version = property.Value.TryGetProperty("version_number", out var v) ? v.GetString() ?? "" : "";
                found[property.Name.ToLowerInvariant()] = (projectId, version);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            report.Online = false;
            report.NetworkError = ex.Message;
        }

        if (report.Online && found.Count > 0)
        {
            try
            {
                var ids = found.Values.Select(f => f.ProjectId).Where(i => i != "").Distinct().ToArray();
                var url = "https://api.modrinth.com/v2/projects?ids=" + Uri.EscapeDataString(JsonSerializer.Serialize(ids));
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url, token));

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
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        foreach (var item in report.Items)
        {
            if (item.Hash != "" && report.Online && found.TryGetValue(item.Hash, out var match))
            {
                var title = titles.TryGetValue(match.ProjectId, out var t) ? t : match.ProjectId;
                item.Project = (title + " " + match.Version).Trim();
                item.Status = item.Note != "" ? "Flagged" : "Verified";
            }
            else if (item.Note != "")
            {
                item.Status = "Flagged";
            }
            else if (!report.Online)
            {
                item.Status = "No network";
            }
            else
            {
                item.Status = "Unverified";
            }
        }

        report.Items = report.Items.OrderBy(i => Rank(i.Status)).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return report;
    }

    static int Rank(string status)
    {
        return status switch
        {
            "Flagged" => 0,
            "Unverified" => 1,
            "No network" => 2,
            _ => 3
        };
    }

    public static string BuildReport(string folder, ScanReport report)
    {
        var lines = new List<string>
        {
            "Mod hash report",
            "Folder: " + folder,
            "Run (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            "Files checked: " + report.Items.Count,
            ""
        };

        if (!report.Online)
        {
            lines.Add("Could not reach Modrinth: " + report.NetworkError);
            lines.Add("No file could be verified. This may be a network problem or a blocked connection.");
            lines.Add("");
        }

        foreach (var item in report.Items)
        {
            lines.Add("[" + item.Status.ToUpperInvariant() + "] " + item.Name);

            if (item.Project != "")
            {
                lines.Add("    Modrinth: " + item.Project);
            }

            if (item.Meta != "")
            {
                lines.Add("    Metadata: " + item.Meta);
            }

            if (item.Note != "")
            {
                lines.Add("    Note: " + item.Note);
            }

            if (item.Hash != "")
            {
                lines.Add("    SHA-1: " + item.Hash);
            }
        }

        lines.Add("");
        lines.Add("Verified: " + report.Items.Count(i => i.Status == "Verified") + "  Unverified: " + report.Items.Count(i => i.Status == "Unverified") + "  Flagged: " + report.Items.Count(i => i.Status == "Flagged"));
        lines.Add("UNVERIFIED means the hash was not found on Modrinth. It does not mean the file is a cheat.");
        lines.Add("Review unverified files in a decompiler before drawing any conclusion.");
        lines.Add("A clean result only means no artifact meeting current criteria was found in the evidence collected.");

        return string.Join(Environment.NewLine, lines);
    }

    public static List<string> FindModFolders()
    {
        var result = new List<string>();

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            AddIfExists(result, Path.Combine(appData, ".minecraft", "mods"));
            AddInstances(result, Path.Combine(appData, "PrismLauncher", "instances"), new[] { @".minecraft\mods", @"minecraft\mods" });
            AddInstances(result, Path.Combine(appData, "ModrinthApp", "profiles"), new[] { "mods" });
            AddInstances(result, Path.Combine(appData, "ATLauncher", "instances"), new[] { "mods" });
            AddInstances(result, Path.Combine(docs, "curseforge", "minecraft", "Instances"), new[] { "mods" });
            AddInstances(result, Path.Combine(appData, "gdlauncher_carbon", "data", "instances"), new[] { "mods" });
            AddInstances(result, Path.Combine(appData, ".technic", "modpacks"), new[] { "mods" });
        }
        catch
        {
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static void AddIfExists(List<string> list, string path)
    {
        if (Directory.Exists(path))
        {
            list.Add(path);
        }
    }

    static void AddInstances(List<string> list, string root, string[] subfolders)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(root))
        {
            foreach (var sub in subfolders)
            {
                AddIfExists(list, Path.Combine(directory, sub));
            }
        }
    }

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
}
