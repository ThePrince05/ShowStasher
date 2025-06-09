using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public class JikanPagination
    {
        [JsonPropertyName("has_next_page")]
        public bool HasNextPage { get; set; }
    }

}
