using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
namespace Sati.Data
{
    public class SatiContextFactory : IDesignTimeDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext(string[] args)
        {
            var workingDirectory = Directory.GetCurrentDirectory();
            var repositoryDirectory = Directory.GetParent(workingDirectory)?.FullName;
            var configurationDirectory = File.Exists(Path.Combine(workingDirectory, "appsettings.json"))
                ? workingDirectory
                : repositoryDirectory is not null &&
                  File.Exists(Path.Combine(repositoryDirectory, "appsettings.json"))
                    ? repositoryDirectory
                    : throw new InvalidOperationException(
                        "The design-time SatiContext requires appsettings.json in the working " +
                        "directory or its immediate parent.");

            var config = new ConfigurationBuilder()
                .SetBasePath(configurationDirectory)
                .AddJsonFile("appsettings.json")
                .Build();

            var connectionString = config.GetConnectionString("SatiProduction")
                ?? throw new InvalidOperationException(
                    "appsettings.json does not define ConnectionStrings:SatiProduction.");

            var optionsBuilder = new DbContextOptionsBuilder<SatiContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return new SatiContext(optionsBuilder.Options);
        }
    }
}
