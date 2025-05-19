using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.Helpers
{
    public class JikanAnimeSearchResult
    {
        [JsonProperty("mal_id")]
        public int MalId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; } = "";

        [JsonProperty("synopsis")]
        public string? Synopsis { get; set; }

        [JsonProperty("rating")]
        public string? Rating { get; set; }

        [JsonProperty("images")]
        public JikanImageData? Images { get; set; }

        [JsonIgnore]
        public string? ImageUrl => Images?.Jpg?.ImageUrl;
    }
}
