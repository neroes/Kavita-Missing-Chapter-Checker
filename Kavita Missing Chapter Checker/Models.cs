using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Kavita_Missing_Chapter_Checker
{
    public class Series
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class Volume
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }
        [JsonPropertyName("chapters")]
        public List<Chapter> Chapters { get; set; }
    }

    public class Chapter
    {
        [JsonPropertyName("number")]
        [JsonConverter(typeof(JsonDecimalConverter))]
        public decimal Number { get; set; }

        [JsonPropertyName("files")]
        public FileInfo[] Files { get; set; }
        [JsonPropertyName("isSpecial")]
        public bool IsSpecial { get; set; }
    }

    public class FileInfo
    {
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; }

        [JsonPropertyName("pages")]
        public int pages { get; set; }
    }
}
