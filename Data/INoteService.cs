using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface INoteService
    {
        Task<Note> AddNoteAsync(Note note);
        Task DeleteNoteAsync(Note note);
        Task UpdateNoteAsync(Note note);
        Task<List<Note>> GetAllByPersonAsync(int personId);
        Task UpdateAbandonedNotesAsync(int abandonedAfterDays);
        Task<List<Note>> GetMonthlyNotesAsync(int userId);
        Task<List<Note>> GetByYearAsync(int userId, int year);

        // Every note on one case manager's calendar date, across their whole
        // caseload. Service-time overlap is a property of the case manager's day,
        // not of a client, so this deliberately ignores PersonId.
        Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date);
    }
}
