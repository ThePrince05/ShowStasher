using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public class JikanRelationResponse
    {
        [JsonPropertyName("data")]
        public List<JikanRelationData> Data { get; set; } = new();
    }

    public class JikanRelationData
    {
        [JsonPropertyName("relation")]
        public string RelationType { get; set; }

        [JsonPropertyName("entry")]
        public List<JikanRelationEntry> Entry { get; set; } = new();
    }

    public class JikanRelationEntry
    {
        [JsonPropertyName("mal_id")]
        public int MalId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

}
