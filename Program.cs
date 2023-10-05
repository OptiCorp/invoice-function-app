using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System;
using InvoiceApp.Model;

namespace InvoiceApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // string sqlConnectionString = Environment.GetEnvironmentVariable("InvoiceDbConnectionString");
            string sqlConnectionString = "Server=tcp:dbserverv2-turbinsikker-prod.database.windows.net,1433;Initial Catalog=sqldb-invoice;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;";

            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureServices(s => {
                    s.AddDbContext<InvoiceContext>(
                        options => options.UseSqlServer(sqlConnectionString)
                    );
                })
                .Build();

            await host.RunAsync();
        }
    }
}