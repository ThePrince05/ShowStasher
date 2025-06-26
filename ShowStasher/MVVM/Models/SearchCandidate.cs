using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public class SearchCandidate
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public int? Year { get; set; }

        // The poster path as provided by TMDb (e.g. "/abc123.jpg")
        public string? PosterPath { get; set; }

        public string DisplayTitle => Year.HasValue ? $"{Title.Trim()} ({Year})" : Title.Trim();

        public string? FullPosterUrl =>
            string.IsNullOrWhiteSpace(PosterPath)
            ? null
            : $"https://image.tmdb.org/t/p/w185{PosterPath}";
    }


}
