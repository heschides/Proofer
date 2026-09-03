using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sati.Contracts.V1;
using Sati.Persistence.Migrations;
using Xunit;

namespace Sati.Api.Tests;

public sealed class DocumentTemplateMigrationTests
{
    [Fact]
    public void MigrationSeedsThePublishedPrivacyNoticeAndPublisherIndex()
    {
        var operations = new AddDocumentTemplatesAndSafetyPlans().UpOperations;
        var seed = Assert.Single(operations.OfType<InsertDataOperation>(), operation => operation.Table == "DocumentTemplates");
        Assert.Equal(SatiDefaultDocumentTemplates.PrivacyPracticesBody,
            seed.Values[0, Array.IndexOf(seed.Columns, "Body")]);
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "DocumentTemplates" && index.Columns.SequenceEqual(["PublishedByUserId"]));
    }
}
