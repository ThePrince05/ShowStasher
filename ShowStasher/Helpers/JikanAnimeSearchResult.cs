using Newtonsoft.Json;
using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShowStasher.Helpers
{
    public class JikanAnimeSearchResponse
    {
        [JsonProperty("data")]
        public List<JikanAnimeData> Data { get; set; } = new();
    }

}
