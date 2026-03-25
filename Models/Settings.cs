using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Models
{
    public class Settings
    {
        public int Id { get; set; }

        // Abandonment
        public int AbandonedAfterDays { get; set; } = 7;

        // Productivity
        public int ProductivityThreshold { get; set; } = 100;
        public decimal BaseIncentive { get; set; } = 0;
        public decimal PerUnitIncentive { get; set; } = 0;

        // Note templates
        public string VisitTemplate { get; set; } = string.Empty;
        public string ContactTemplate { get; set; } = string.Empty;
        public string DocumentationTemplate { get; set; } = string.Empty;

        // Weekday exclusions
        public bool ExcludeMonday { get; set; } = false;
        public bool ExcludeTuesday { get; set; } = false;
        public bool ExcludeWednesday { get; set; } = false;
        public bool ExcludeThursday { get; set; } = false;
        public bool ExcludeFriday { get; set; } = false;

        // Federal holidays
        public bool ExcludeNewYearsDay { get; set; } = true;
        public bool ExcludeMLKDay { get; set; } = false;
        public bool ExcludePresidentsDay { get; set; } = false;
        public bool ExcludeMemorialDay { get; set; } = true;
        public bool ExcludeJuneteenth { get; set; } = false;
        public bool ExcludeIndependenceDay { get; set; } = true;
        public bool ExcludeLaborDay { get; set; } = true;
        public bool ExcludeIndigenousPeoplesDay { get; set; } = false;
        public bool ExcludeVeteransDay { get; set; } = false;
        public bool ExcludeThanksgiving { get; set; } = true; 
        public bool ExcludeDayAfterThanksgiving { get; set; } = true;
        public bool ExcludeChristmas { get; set; } = true;
    }
}
