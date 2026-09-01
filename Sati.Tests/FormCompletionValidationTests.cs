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
        detached.MarkComplete(DateTime.Today.AddDays(1));
        var service = new FormService(fixture.Factory);

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.UpdateFormAsync(detached));

        Assert.Contains(FormCompletionRules.FutureDateMessage, error.Message);
        await using var verification = fixture.Factory.CreateDbContext();
        var stored = await verification.Forms.AsNoTracking().SingleAsync(form => form.Id == formId);
        Assert.Null(stored.CompletedDate);
        Assert.False(stored.IsCompliant);
    }
}
