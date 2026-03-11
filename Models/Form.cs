using System;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters;
using System.Text;
using static Proofer.Enums;

namespace Proofer.Models
{
    public class Form
    {
        public int Id { get; set; }
        public FormType Type { get; set; }
        public DateTime DueDate { get; set; }
        public bool IsCompliant { get; set;  }

        public Person Person { get; set; } = null!; 
        public int PersonId { get; set; }
    }
}
