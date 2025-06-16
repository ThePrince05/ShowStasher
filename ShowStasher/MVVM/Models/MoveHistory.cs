using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public class MoveHistory
    {
        public int Id { get; set; }
        public string OriginalFileName { get; set; }
        public string NewFileName { get; set; }
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public DateTime MovedAt { get; set; } // Optional – keep if you want time tracking
    }

}
