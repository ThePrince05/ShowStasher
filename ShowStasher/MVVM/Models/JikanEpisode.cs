using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public class JikanEpisode
    {
        [JsonPropertyName("mal_id")]
        public int EpisodeId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }
    }

}
