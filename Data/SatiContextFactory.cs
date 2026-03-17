using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sati.Data
{
    public class SatiContextFactory : IDesignTimeDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<SatiContext>();
            optionsBuilder.UseSqlServer(config.GetConnectionString("SatiDb"));

            return new SatiContext(optionsBuilder.Options);


        }
    }
}
