using Newtonsoft.Json;
using ShowStasher.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public class JikanAnimeData
    {
        [JsonPropertyName("mal_id")]
        public int MalId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("titles")]
        public List<JikanTitle> Titles { get; set; }

        [JsonPropertyName("title_english")]
        public string? TitleEnglish { get; set; }

        [JsonPropertyName("title_japanese")]
        public string? TitleJapanese { get; set; }

        [JsonPropertyName("synopsis")]
        public string? Synopsis { get; set; }

        [JsonProperty("rating")]
        public string? Rating { get; set; }

        [JsonPropertyName("images")]
        public JikanImageData? Images { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

}
