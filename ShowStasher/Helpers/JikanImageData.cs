using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.Helpers
{
    public class JikanImageData
    {
        [JsonProperty("jpg")]
        public JikanImageDetail? Jpg { get; set; }
    }
}
