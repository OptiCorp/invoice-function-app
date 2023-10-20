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

            string image = "https://github.com/OptiCorp/TurbinSikker-App/blob/develop/src/assets/images/bigLogo.png?raw=true";


            string htmlStyle = @"
                                <style>
                                    .invoice-box {max-width: 80%;margin: auto;padding: 30px;border: 1px solid #eee;box-shadow: 0 0 10px rgba(0, 0, 0, 0.15);font-size: 16px;line-height: 24px;font-family: 'Helvetica Neue', 'Helvetica', Helvetica, Arial, sans-serif;color: #555;}
                                    .invoice-box table {width: 100%;line-height: inherit;text-align: left;}
                                    .invoice-box table.table-class {width: 80%;line-height: inherit;text-align: left;}
                                    .invoice-box table td {padding: 5px;vertical-align: top;}
                                    .right-align {text-align: right;}
                                    .invoice-box table tr.top table td {padding-bottom: 20px;}
                                    .invoice-box table tr.top table td.title {font-size: 45px;line-height: 45px;color: #333;}
                                    .invoice-box table tr.information table td {padding-bottom: 40px;}
                                    .invoice-box table tr.heading td {background: #eee;border-bottom: 1px solid #ddd;font-weight: bold;}
                                    .invoice-box table tr.details td {padding-bottom: 20px;}
                                    .invoice-box table tr.item td {border-bottom: 1px solid #eee;}
                                    .invoice-box table tr.item.last td {border-bottom: none;}
                                    .invoice-box table tr.total td.total-data {border-top: 2px solid #eee;font-weight: bold;text-align: right;}
                                </style>";

            string htmlReceiverInfo = $@"
                                        <tr class='information'><td colspan='5'>
                                            <table>
                                                <tr>
                                                    <td><img src='{image}'></td>
                                                    <td class='right-align'>Invoice ID: {invoice.Id}<br />From: {invoice.Sender}<br />To: {invoice.Receiver}<br />Sent: {invoice.SentDate}</td>
                                                </tr>
                                            </table>
                                        </tr>";

            string htmlInvoiceChecklists = @"";

            for (int i = 0; i<numberOfWorkflows; i++)
            {
                var workflow = workflows[i];
                float completionTime = workflow.CompletionTime;
                float ratePerMin = workflow.HourlyRate/60f;

                string row = $@"
                                <tr class='item'>
                                    <td>{workflow.Name}</td>
                                    <td>{workflow.EstimatedCompletionTime}</td>
                                    <td>{workflow.CompletionTime}</td>
                                    <td>${workflow.HourlyRate}</td>
                                    <td class='right-align'>${Math.Round(completionTime*ratePerMin, 2)}</td>
                                </tr>";

                if (i == numberOfWorkflows-1)
                {
                    row = $@"
                            <tr class='item last'>
                                <td>{workflow.Name}</td>
                                <td>{workflow.EstimatedCompletionTime}</td>
                                <td>{workflow.CompletionTime}</td>
                                <td>${workflow.HourlyRate}</td>
                                <td class='right-align'>${Math.Round(completionTime*ratePerMin, 2)}</td>
                            </tr>";
                }
                htmlInvoiceChecklists += row;
            }
            string htmlInvoiceTable = $@"
                                        <tr class='heading'>
                                            <td>Checklist</td>
                                            <td>Estimated time(mins)</td>
                                            <td>Time(mins)</td>
                                            <td>Hourly rate</td>
                                            <td class='right-align'>Price</td>
                                        </tr>
                                        {htmlInvoiceChecklists}
                                        <tr class='total'>
                                            <td></td><td></td><td></td><td></td>
                                            <td class='total-data'>Total: ${Math.Round(invoice.Amount, 2)}</td>
                                        </tr>";

            string html = $@"
                            <html>
                                <head>
                                    {htmlStyle}
                                </head>
                                <body>
                                    <div class='invoice-box'>
                                        <table cellpadding='0' cellspacing='0' class='table-class'>
                                            {htmlReceiverInfo}
                                            {htmlInvoiceTable}
                                        </table>
                                    </div>
                                </body>
                            </html>";

            

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