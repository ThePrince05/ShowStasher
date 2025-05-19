using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.Helpers
{
    public class JikanAnimeSearchResponse
    {
        [JsonProperty("data")]
        public List<JikanAnimeSearchResult>? Data { get; set; }
    }
}
