using System;
using System.Collections;
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

            if (invoice == null) return new NotFoundObjectResult("PDF failed, no invoice found with this ID.");

            var workflows = JsonSerializer.Deserialize<List<Workflow>>(invoice.WorkflowsSerialized);

            if (workflows.Count == 0) return new BadRequestObjectResult("No workflows associated with this invoice.");

            var numberOfWorkflows = workflows.Count;

            string containerEndpoint = Environment.GetEnvironmentVariable("PdfContainerEndpoint");
            BlobContainerClient containerClient = new BlobContainerClient(
                new Uri(containerEndpoint), 
                new DefaultAzureCredential(
                    new DefaultAzureCredentialOptions {ManagedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")}
                ));


            string htmlStyle = "<style>.invoice-box {max-width: 80%;margin: auto;padding: 30px;border: 1px solid #eee;box-shadow: 0 0 10px rgba(0, 0, 0, 0.15);font-size: 16px;line-height: 24px;font-family: 'Helvetica Neue', 'Helvetica', Helvetica, Arial, sans-serif;color: #555;}.invoice-box table {width: 100%;line-height: inherit;text-align: left;}.invoice-box table.table-class {width: 80%;line-height: inherit;text-align: left;}.invoice-box table td {padding: 5px;vertical-align: top;}.right-align {text-align: right;}.invoice-box table tr.top table td {padding-bottom: 20px;}.invoice-box table tr.top table td.title {font-size: 45px;line-height: 45px;color: #333;}.invoice-box table tr.information table td {padding-bottom: 40px;}.invoice-box table tr.heading td {background: #eee;border-bottom: 1px solid #ddd;font-weight: bold;}.invoice-box table tr.details td {padding-bottom: 20px;}.invoice-box table tr.item td {border-bottom: 1px solid #eee;}.invoice-box table tr.item.last td {border-bottom: none;}.invoice-box table tr.total td.total-data {border-top: 2px solid #eee;font-weight: bold;text-align: right;}</style>";
            string htmlInvoiceInfo = string.Format("<tr class='top'><td colspan='5'><table><tr><td class='title'></td><td class='right-align'>Invoice ID: {0}<br />Sent: {1}</td></tr></table></td></tr>", invoice.Id, invoice.SentDate);
            string htmlReceiverInfo = string.Format("<tr class='information'><td colspan='5'><table><tr><td>OptiCorp<br />Laberget 28<br />4020, Stavanger</td><td class='right-align'>Acme Corp.<br />John Doe<br />{0}</td></tr></table></td></tr>", invoice.Receiver);
            string htmlInvoiceChecklists = "";

            for (int i = 0; i<numberOfWorkflows; i++)
            {
                var workflow = workflows[i];
                float completionTime = workflow.CompletionTime;
                float ratePerMin = workflow.HourlyRate/60f;
                string row = string.Format("<tr class='item'><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td class='right-align'>{4}</td></tr>", workflow.Name, workflow.EstimatedCompletionTime, workflow.CompletionTime, workflow.HourlyRate, Math.Round(completionTime*ratePerMin, 2));
                if (i == numberOfWorkflows-1)
                {
                    row = string.Format("<tr class='item last'><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td class='right-align'>{4}</td></tr>", workflow.Name, workflow.EstimatedCompletionTime, workflow.CompletionTime, workflow.HourlyRate, Math.Round(completionTime*ratePerMin, 2));
                }
                htmlInvoiceChecklists += row;
            }

            string htmlInvoiceTable = string.Format("<tr class='heading'><td>Checklist</td><td>Estimated time</td><td>Time</td><td>Hourly rate</td><td class='right-align'>Price</td></tr>{0}<tr class='total'><td></td><td></td><td></td><td></td><td class='total-data'>Total: {1}</td></tr>", htmlInvoiceChecklists, Math.Round(invoice.Amount, 2));

            string html = string.Format("<html><head>{0}</head><body><div class='invoice-box'><table cellpadding='0' cellspacing='0' class='table-class'>{1}{2}{3}</table></div></body></html>", htmlStyle, htmlInvoiceInfo, htmlReceiverInfo, htmlInvoiceTable);

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

            if (response.StatusCode != HttpStatusCode.OK) return new BadRequestObjectResult(response.Content);


            return new OkObjectResult("PDF was generated and email was sent.");
        }
    }
}