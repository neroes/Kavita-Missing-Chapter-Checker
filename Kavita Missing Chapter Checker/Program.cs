using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Kavita_Missing_Chapter_Checker
{
    internal class Program
    {
        private static StreamWriter LogWriter;

        static void Main(string[] args)
        {
            InitializeLogger("MissingChapters.log");
            string odpsUrl = PromptUser("Enter the Kavita ODPS URL:");
            if (string.IsNullOrWhiteSpace(odpsUrl))
            {
                throw new Exception("Error: ODPS URL is required.");
            }
            var baseUrl = ExtractBaseUrl(odpsUrl);
            var apiKey = ExtractApiKey(odpsUrl);

            while (true)
            {
                Console.Clear(); // Clear the console before each run

                string libraryId;
                try
                {
                    GetInfoFromUser(out libraryId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    continue; // Restart the loop if input is invalid
                }

                string kavitaToken = Authenticate(baseUrl, apiKey);

                LogMessage("Missing Chapters: \n\n--------------------------\n");

                var librarySeries = GetLibrarySeriesInfo(libraryId, kavitaToken, baseUrl);

                foreach (var series in librarySeries)
                {
                    AnalyzeSeries(series, kavitaToken, baseUrl);
                }

                Console.WriteLine("\nWould you like to check another library? (y/n)");
                string response = Console.ReadLine()?.Trim().ToLower();
                if (response != "y")
                {
                    break; // Exit the loop if the user doesn't want to continue
                }
            }
        }

        private static void GetInfoFromUser(out string libraryId)
        {

            libraryId = PromptUser("Enter the Library ID:");
            if (string.IsNullOrWhiteSpace(libraryId))
            {
                throw new Exception("Error: Library ID is required.");
            }
        }

        private static void InitializeLogger(string logFilePath)
        {
            LogWriter = new StreamWriter(logFilePath);
        }

        private static string PromptUser(string message)
        {
            Console.WriteLine(message);
            return Console.ReadLine();
        }

        private static string ExtractBaseUrl(string odpsUrl) => odpsUrl.Split("/api")[0];

        private static string ExtractApiKey(string odpsUrl) => odpsUrl.Split("/opds/")[1];

        private static string Authenticate(string baseUrl, string apiKey)
        {
            using var client = new HttpClient();
            string authUrl = $"{baseUrl}/api/Plugin/authenticate/?apiKey={apiKey}&pluginName=Kavita_List";

            var response = client.PostAsync(authUrl, null).Result;
            var responseContent = response.Content.ReadAsStringAsync().Result;

            var json = JsonSerializer.Deserialize<Dictionary<string, string>>(responseContent);
            return json["token"];
        }

        private static List<Series> GetLibrarySeriesInfo(string libraryId, string kavitaToken, string baseUrl)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {kavitaToken}");

            string url = $"{baseUrl}/api/Series/all-v2/?PageNumber=1&PageSize=0";

            var data = new
            {
                id = 0,
                name = (string)null,
                statements = new[]
                {
                    new { comparison = 0, field = 19, value = libraryId }
                },
                combination = 0,
                sortOptions = new { sortField = 1, isAscending = true },
                limitTo = 0
            };

            var content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            var response = client.PostAsync(url, content).Result;
            var responseContent = response.Content.ReadAsStringAsync().Result;

            return JsonSerializer.Deserialize<List<Series>>(responseContent);
        }

        private static List<Volume> GetSeriesVolumes(int seriesId, string kavitaToken, string baseUrl)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {kavitaToken}");

            string url = $"{baseUrl}/api/Series/volumes?seriesId={seriesId}";

            var response = client.GetAsync(url).Result;
            var responseContent = response.Content.ReadAsStringAsync().Result;

            return JsonSerializer.Deserialize<List<Volume>>(responseContent);
        }

        private static void AnalyzeSeries(Series series, string kavitaToken, string baseUrl)
        {
            var seriesReportBuilder = new StringBuilder();
            var foundIssueInSeries = false;

            int seriesId = series.Id;
            string seriesName = series.Name;

            var seriesVolumes = GetSeriesVolumes(seriesId, kavitaToken, baseUrl);
            seriesReportBuilder.AppendLine($"Series: {seriesName}");
            var sortedVolumes = seriesVolumes.OrderBy(e => e.Number).ToList();

            var volume1Chapters = seriesVolumes.FirstOrDefault(v => v.Number == 1)?.Chapters.Select(c => c.Number).ToHashSet() ?? new HashSet<decimal>();
            bool volumesStartAtOne = true;
            if (sortedVolumes.Count > 1)
            {
                volumesStartAtOne = sortedVolumes[1].Chapters.Any(e => e.Number < 2);
            }
            foreach (var volume in sortedVolumes)
            {
                // Check for overlap between Volume 1 and subsequent volumes
                if (volume.Number > 1 && volume.Chapters.Any(c => volume1Chapters.Contains(c.Number)) && !volumesStartAtOne)
                {
                    foundIssueInSeries = true;
                    seriesReportBuilder.AppendLine($"Volume {volume.Number} has overlapping chapter numbers with Volume 1.");
                }

                AnalyzeVolume(volume, seriesReportBuilder, ref foundIssueInSeries);
            }

            if (foundIssueInSeries)
            {
                LogMessage(seriesReportBuilder.ToString());
            }
        }

        private static void AnalyzeVolume(Volume volume, StringBuilder seriesReportBuilder, ref bool foundIssueInSeries)
        {
            var chapters = volume.Chapters.OrderBy(e => e.Number).ToArray();

            var missingChapters = FindMissingChapters(chapters);
            var duplicateChapters = FindDuplicateChapters(chapters);
            var fileMissMatchChapters = FindFileNameMismatches(volume, chapters);

            if (missingChapters.Any() || duplicateChapters.Any() || fileMissMatchChapters.Any())
            {
                foundIssueInSeries = true;
                seriesReportBuilder.AppendLine($"Volume: {volume.Number}");
                AppendMissingToReport(seriesReportBuilder, "Missing Chapters", missingChapters);
                AppendIssuesToReport(seriesReportBuilder, "Duplicate Chapters", duplicateChapters);
                AppendIssuesToReport(seriesReportBuilder, "File Name Mismatches", fileMissMatchChapters);
            }
        }

        private static List<string> FindMissingChapters(Chapter[] chapters)
        {
            List<string> missingChapters = [];
            for (int i = 0; i < chapters.Length - 1; i++)
            {
                if (chapters[i].Number + 1.1M < chapters[i + 1].Number)
                {
                    var firstMissingChapter = Math.Floor(chapters[i].Number + 1);
                    var lastMissingChapter = Math.Floor(chapters[i+1].Number - 1);
                    missingChapters.Add($"{firstMissingChapter}-{lastMissingChapter}");
                }
            }
            return missingChapters;

            IEnumerable<decimal> DecimalRange(decimal start, decimal end)
            {
                for (decimal value = start; value <= end; value ++)
                {
                    yield return value;
                }
            }
        }

        private static List<string> FindDuplicateChapters(Chapter[] chapters)
        {
            List<string> duplicateChapters = [];
            foreach (var chapter in chapters)
            {
                if (chapter.Files.Length > 1)
                {
                    duplicateChapters.Add($"Multiple files found for chapter {chapter.Number}: \n {string.Join("\n - ", chapter.Files.Select(e => e.FilePath))}");
                }
            }
            return duplicateChapters;
        }

        private static List<string> FindFileNameMismatches(Volume volume, Chapter[] chapters)
        {
            List<string> fileMissMatchChapters = [];
            foreach (var chapter in chapters)
            {
                if (chapter.IsSpecial) // Skip specials
                {
                    continue;
                }

                string chapterPattern = $@"Vol\. {volume.Number} Ch\. 0*{chapter.Number.ToString(CultureInfo.InvariantCulture)}";
                string altPattern = $@"Chapter 0*{chapter.Number.ToString(CultureInfo.InvariantCulture)}";

                var fileName = chapter.Files[0].FilePath.Split('/')[^1];
                if (!Regex.IsMatch(fileName, chapterPattern) &&
                    (volume.Number != 1 || !Regex.IsMatch(fileName, altPattern)))
                {
                    fileMissMatchChapters.Add($"File name mismatch for chapter {chapter.Number}: {fileName} does not match expected format for Volume {volume.Number} Chapter {chapter.Number}");
                }
            }
            return fileMissMatchChapters;
        }

        private static void AppendIssuesToReport(StringBuilder reportBuilder, string issueType, IEnumerable<string> issues)
        {
            if (issues.Any())
            {
                reportBuilder.AppendLine($"{issueType}: {string.Join("\n", issues)}");
            }
        }

        private static void AppendMissingToReport(StringBuilder reportBuilder, string issueType, IEnumerable<string> chapters)
        {
            if (chapters.Any())
            {
                reportBuilder.AppendLine($"{issueType}: {string.Join(", ", chapters)}");
            }
        }

        private static void LogMessage(string message)
        {
            Console.WriteLine(message);
            LogWriter.WriteLine(message);
        }
    }
}