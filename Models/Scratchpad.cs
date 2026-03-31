using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Sati.Models
{
    public class Scratchpad
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime Date { get; set; }
        public string Content { get; set; } = string.Empty;

        [NotMapped]
        public string ContentPreview
        {
            get
            {
                var firstLine = Content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault() ?? string.Empty;
                return firstLine.Length > 80 ? firstLine[..80] + "…" : firstLine;
            }
        }
    }
}
