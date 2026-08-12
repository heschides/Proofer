using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Sati.Models
{
    public class Scratchpad
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime Date { get; set; }
        public string Content { get; set; } = string.Empty;
        public ObservableCollection<ScratchpadComment> Comments { get; set; } = [];

        [NotMapped]
        public string DisplayContent => string.IsNullOrWhiteSpace(Content)
            ? "No text was entered on this day."
            : Content;

        [NotMapped]
        public bool HasComments => Comments.Count > 0;

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
