using System;
using System.Collections;
using System.Collections.Generic;
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
            InvoiceRequestDto invoiceDto = JsonSerializer.Deserialize<InvoiceRequestDto>(mySbMsg);

            var workflowsSerialized = JsonSerializer.Serialize<ICollection<Workflow>>(invoiceDto.Workflows);

            Invoice invoice = new Invoice
            {
                CreatedDate = DateTime.Now,
                SentDate = DateTime.Now,
                Status = InvoiceStatus.Unpaid,
                Sender = "Opticorp",
                Receiver = invoiceDto.Receiver,
                Amount = invoiceDto.Amount,
                PdfBlobLink = Guid.NewGuid().ToString(),
                WorkflowsSerialized = workflowsSerialized
            };
            

            await _context.Invoice.AddAsync(invoice);
            await _context.SaveChangesAsync();

            var client = new HttpClient();
            var response = await client.PostAsync(
                string.Format("https://turbinsikker-fa-prod.azurewebsites.net/api/PdfGenerator?code=hc8nBX45bjn4GJtWTlEnfu-MZsGb_cQEL8attWcjTx58AzFuzOPSqg==&invoiceId={0}", invoice.Id),
                // string.Format("http://localhost:7071/api/PdfGenerator?invoiceId={0}", invoice.Id),
                null
            );

            InvoiceResponseDto invoiceResponse = new InvoiceResponseDto
            {
                Id = invoice.Id,
                CreatedDate = invoice.CreatedDate,
                SentDate = invoice.SentDate,
                Status = invoice.Status,
                Sender = invoice.Sender,
                Receiver = invoice.Receiver,
                Amount = invoice.Amount,
                PdfBlobLink = invoice.PdfBlobLink,
                Workflows = invoiceDto.Workflows
            };

            var connectionString = Environment.GetEnvironmentVariable("connectionStringBus");
            var sbClient = new ServiceBusClient(connectionString);
            var sender = sbClient.CreateSender("add-invoice");
            var body = JsonSerializer.Serialize(invoiceResponse);
            var sbMessage = new ServiceBusMessage(body);
            await sender.SendMessageAsync(sbMessage);

            return new OkObjectResult("Success");
        }
    }
}