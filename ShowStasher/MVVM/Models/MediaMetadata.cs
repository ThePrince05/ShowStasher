using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    // Models/MediaMetadata.cs
    public class MediaMetadata
    {
        public string Title { get; set; }
        public string Type { get; set; } // Movie, Series, Anime
        public int? Year { get; set; }
        public string Synopsis { get; set; }
        public string Rating { get; set; }
        public string PG { get; set; }
        public string PosterUrl { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public string EpisodeTitle { get; set; }
    }

}
