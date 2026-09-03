using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class FormCompletionValidationTests
{
    [Fact]
    public async Task LocalPersistenceRejectsAFutureCompletionDateWithoutWritingIt()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = DateTime.Today.AddMonths(-6);
            var form = new Form(FormType.Q1R, DateTime.Today)
            {
                PersonId = fixture.PersonOneId
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        Form detached;
        await using (var db = fixture.Factory.CreateDbContext())
            detached = await db.Forms.AsNoTracking().SingleAsync(form => form.Id == formId);
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new FormService(fixture.Factory, session);

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.AttestAsync(detached, DateTime.Today.AddDays(1)));

        Assert.Contains(FormCompletionRules.FutureDateMessage, error.Message);
        await using var verification = fixture.Factory.CreateDbContext();
        var stored = await verification.Forms.AsNoTracking().SingleAsync(form => form.Id == formId);
        Assert.Null(stored.CompletedDate);
        Assert.False(stored.IsCompliant);
    }
}
