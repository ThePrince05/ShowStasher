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

        // Adjust this path to wherever your placeholder image is located in the project
        private const string PlaceholderPosterPath = "pack://application:,,,/Assets/Images/placeholder.png";

        public string FullPosterUrl =>
            string.IsNullOrWhiteSpace(PosterPath)
            ? PlaceholderPosterPath
            : $"https://image.tmdb.org/t/p/w185{PosterPath}";
    }



}
