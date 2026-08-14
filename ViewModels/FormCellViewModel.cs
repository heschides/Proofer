using Sati.Helpers;
using Sati.Models;

namespace Sati.ViewModels
{

    public class FormCellViewModel
    {
        public Form? Form { get; }
        public FormCellStatus Status { get; }

        public FormCellViewModel(Person person, FormType type, DateTime today)
        {
            Form = person.GetCurrentCycleForm(type);
            if (Form?.OpenedDate != null)
                System.Diagnostics.Debug.WriteLine($"{person.FullName} {type}: OpenedDate={Form.OpenedDate}, IsOpen={IsOpen}");
            Status = FormCellStatusCalculator.Compute(Form, today);
        }

        public DateTime? DueDate => Form?.DueDate;
        public DateTime? CompletedDate => Form?.CompletedDate;
        public DateTime? OpenedDate => Form?.OpenedDate;


        public bool IsOpen => Form is { OpenedDate: not null, IsCompliant: false };

        public string CellText
        {
            get
            {
                if (Form is null)
                    return string.Empty;

                var lines = new List<string>();
                if (Status == FormCellStatus.Overdue)
                    lines.Add("OVERDUE");

                lines.Add($"Due: {Form.DueDate:M/d/yy}");

                if (Form.IsCompliant && Form.CompletedDate.HasValue)
                    lines.Add($"Completed: {Form.CompletedDate.Value:M/d/yy}");
                else if (IsOpen)
                    lines.Add($"Opened: {Form.OpenedDate!.Value:M/d/yy}");

                return string.Join(Environment.NewLine, lines);
            }
        }
    }
}