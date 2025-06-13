using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.Services
{
    public interface IMetadataSelectionService
    {

        Task<int?> PromptUserToSelectMovieAsync(string originalTitle, IReadOnlyList<SearchCandidate> candidates);

        Task<int?> PromptUserToSelectSeriesAsync(string originalTitle, IReadOnlyList<SearchCandidate> candidates);
    }

}
