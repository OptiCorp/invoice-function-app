using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using InvoiceApp.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace InvoiceApp.Functions
{
    public class InvoiceControllerBus
    {
        private readonly InvoiceContext _context;
        public InvoiceControllerBus(InvoiceContext invoiceContext)
        {
            _context = invoiceContext;
        }

        [Function("InvoiceControllerBus")]
        public async Task<IActionResult> Run(
            [ServiceBusTrigger("generate-invoice", "generate-invoice-function", Connection = "connectionStringBus")]string mySbMsg)
        {
            InvoiceDto invoiceDto = System.Text.Json.JsonSerializer.Deserialize<InvoiceDto>(mySbMsg);

            Invoice invoice = new Invoice
            {
                CreatedDate = DateTime.Now,
                SentDate = DateTime.Now,
                Status = InvoiceStatus.Unpaid,
                Sender = "Opticorp",
                Receiver = invoiceDto.Receiver,
                Amount = invoiceDto.Amount,
                PdfBlobLink = Guid.NewGuid().ToString()
            };
            

            await _context.Invoice.AddAsync(invoice);
            await _context.SaveChangesAsync();

            var client = new HttpClient();
            var response = await client.PostAsync(
                string.Format(Environment.GetEnvironmentVariable("GeneratePdfEndpoint") + "{0}", invoice.Id),
                null
            );

            var connectionString = Environment.GetEnvironmentVariable("connectionStringBus");
            var sbClient = new ServiceBusClient(connectionString);
            var sender = sbClient.CreateSender("add-invoice");
            var body = System.Text.Json.JsonSerializer.Serialize(invoice);
            var sbMessage = new ServiceBusMessage(body);
            await sender.SendMessageAsync(sbMessage);

            return new OkObjectResult("Success");
        }
    }
}