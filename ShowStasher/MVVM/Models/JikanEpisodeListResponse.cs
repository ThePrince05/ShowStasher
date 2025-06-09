using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public class JikanEpisodeListResponse
    {
        public List<JikanEpisode> Data { get; set; } = new();
        public JikanPagination Pagination { get; set; }
    }

}
