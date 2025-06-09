using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public class JikanImageData
    {
        [JsonPropertyName("jpg")]
        public JikanImageFormats? Jpg { get; set; }
    }

    public class JikanImageFormats
    {
        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }
    }

}
