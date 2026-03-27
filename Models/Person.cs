using Sati.Models;
using System;
using System.Collections.Generic;
using System.Security.Permissions;
using System.Text;
using static Sati.Enums;

namespace Sati
{
    public class Person
    {

        //simple properties
        public int Id { get; private set; }
        public int UserId { get; private set;  }
        public string? FirstName { get;  set; }
        public string? LastName { get; set; } 
        public DateTime BirthDate { get;  set; }
        public DateTime EffectiveDate { get;  set; }
        public string? Bio { get;  set; }
        public WaiverType Waiver { get;  set; } = WaiverType.None;
        public string FullName => $"{FirstName} {LastName}".Trim();

        //collections
        public List<Form> Forms { get; set; } = new List<Form>();
        public List<Note> Notes { get; set; } = new List<Note>();

        //constructor
        protected Person() { }

        //methods
        public static Person CreatePerson(int userId, string firstName, string lastName, string Bio, DateTime birthdate, DateTime effective, WaiverType waiver) 
        {
            var person = new Person
            {
                UserId = userId,
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Bio = Bio.Trim(),
                BirthDate = birthdate,
                EffectiveDate = effective,
                Waiver = waiver
            };

                var forms = GenerateFormList(person.EffectiveDate);
                person.Forms = forms;
            
            return person;
        }

        public static List<Form> GenerateFormList(DateTime effective)
        {
            var list = new List<Form>();
            list.Add(new Form { DueDate = effective.AddDays(90), Type = FormType.Q1R });
            list.Add(new Form { DueDate = effective.AddDays(180), Type = FormType.Q2R });
            list.Add(new Form { DueDate = effective.AddDays(270), Type = FormType.Q3R });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Q4R });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.PCP });
            list.Add(new Form { DueDate = effective.AddDays(270), Type = FormType.Reclassification });
            list.Add(new Form { DueDate = effective.AddDays(270), Type = FormType.ComprehensiveAssessment });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Release_Agency });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Release_DHHS });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Release_Medical });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.SafetyPlan });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.PrivacyPractices });

            return list;
        }
    }
}
