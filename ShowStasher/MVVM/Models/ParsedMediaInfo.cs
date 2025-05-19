using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public class ParsedMediaInfo
    {
        public string Title { get; set; }
        public string Type { get; set; } // Movie, Series, Anime
        public int? Season { get; set; }
        public int? Episode { get; set; }
    }

}
