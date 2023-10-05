using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InvoiceApp.Model
{
    public class InvoiceContext : DbContext
    {
        public InvoiceContext(DbContextOptions<InvoiceContext> options)
            : base(options)
        { }

        public DbSet<Invoice> Invoice { get; set; }
    }

    public class InvoiceContextFactory : IDesignTimeDbContextFactory<InvoiceContext>
    {
        public InvoiceContext CreateDbContext(string[] args)
        {
            // string sqlConnectionString = Environment.GetEnvironmentVariable("InvoiceDbConnectionString");
            string sqlConnectionString = "Server=tcp:dbserverv2-turbinsikker-prod.database.windows.net,1433;Initial Catalog=sqldb-invoice;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;";
            var optionsBuilder = new DbContextOptionsBuilder<InvoiceContext>();
            optionsBuilder.UseSqlServer(sqlConnectionString);

            return new InvoiceContext(optionsBuilder.Options);
        }
    }
}