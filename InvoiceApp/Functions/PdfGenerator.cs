using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Storage.Blobs;
using InvoiceApp.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using PdfSharpCore.Pdf;
using HtmlRendererCore.Core;

namespace InvoiceApp.Functions
{
    public class PdfGenerator
    {
        private readonly InvoiceContext _context;
        public PdfGenerator(InvoiceContext invoiceContext)
        {
            _context = invoiceContext;
        }

        [Function("PdfGenerator")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestData req)
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var invoiceId = query["invoiceId"];

            Invoice invoice = await _context.Invoice.FirstOrDefaultAsync(i => i.Id == invoiceId);
            var workflows = JsonSerializer.Deserialize<List<Workflow>>(invoice.WorkflowsSerialized);

            var numberOfWorkflows = workflows.Count;

            string containerEndpoint = Environment.GetEnvironmentVariable("PdfContainerEndpoint");
            BlobContainerClient containerClient = new BlobContainerClient(
                new Uri(containerEndpoint), 
                new DefaultAzureCredential(
                    new DefaultAzureCredentialOptions {ManagedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")}
                ));

            string htmlStyle = "<style>body{padding: 5%;}table{border: solid;width: 100%;border-collapse: collapse;}th,td{padding: 8px;border: solid;}td{text-align: center;}</style>";
            string html = string.Format("<html><head>{0}</head><body><h2>Hello {1}, this is your requested invoice</h2><table><tr><th>Checklist name</th><th>Hours</th><th>Hourly rate</th><th>Total</th></tr>", htmlStyle, invoice.Receiver);

            for (int i = 0; i<numberOfWorkflows; i++)
            {
                var workflow = workflows[i];
                string row = string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}kr</td><td>{3}kr</td></tr>", workflow.Name, workflow.CompletionTime, workflow.HourlyRate, workflow.CompletionTime*workflow.HourlyRate);
                html += row;
            }

            string ending = string.Format("</table><h3>Your total is {0}kr</h3></body></html>", invoice.Amount);
            html += ending;

            var pdf = HtmlRendererCore.PdfSharp.PdfGenerator.GeneratePdf(html, PdfSharpCore.PageSize.A4);

            try
            {
                await containerClient.CreateIfNotExistsAsync();

                using (MemoryStream blobStream = new MemoryStream())
                {
                    pdf.Save(blobStream, false);
                    blobStream.Position = 0;
                    await containerClient.UploadBlobAsync(invoice.PdfBlobLink, blobStream);
                }
            }
            catch (Exception e)
            {
                return new BadRequestObjectResult(e.Message);
            }

            var client = new HttpClient();
            

            var response = await client.PostAsync(
                string.Format(Environment.GetEnvironmentVariable("SendEmailEndpoint") + "{0}", invoice.Id),
                null);

            if (response.StatusCode != HttpStatusCode.OK) return new BadRequestObjectResult("Email sender failed");


            return new OkResult();
        }
    }
}