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
using PdfSharpCore.Drawing;
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
            var workflows = JsonSerializer.Deserialize<List<Workflow>>(invoice.WorkflowsSerialized);

            var numberOfWorkflows = workflows.Count;

            string containerEndpoint = Environment.GetEnvironmentVariable("PdfContainerEndpoint");
            BlobContainerClient containerClient = new BlobContainerClient(
                new Uri(containerEndpoint), 
                new DefaultAzureCredential(
                    new DefaultAzureCredentialOptions {ManagedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")}
                ));


            PdfDocument document = new PdfDocument();
            document.Info.Title = "Created with PDFsharp";

            //Create an empty page
            PdfPage page = document.AddPage();
            
            //Get an XGraphics object for drawing
            XGraphics gfx = XGraphics.FromPdfPage(page);

            //Create a font
            XFont font = new XFont("Verdana", 12, XFontStyle.BoldItalic);

            //Draw the text
            gfx.DrawString(string.Format("Invoice ID: {0}", invoice.Id), font, XBrushes.Black,
                new XRect(0, page.Height*0/(6+numberOfWorkflows), page.Width, page.Height/(6+numberOfWorkflows)),
                XStringFormats.Center);

            gfx.DrawString(string.Format("Date sent: {0}", invoice.SentDate), font, XBrushes.Black,
                new XRect(0, page.Height*1/(6+numberOfWorkflows), page.Width, page.Height/(6+numberOfWorkflows)),
                XStringFormats.Center);

            gfx.DrawString(string.Format("Hello {0}, this is your invoice", invoice.Receiver), font, XBrushes.Black,
                new XRect(0, page.Height*2/(6+numberOfWorkflows), page.Width, page.Height/(6+numberOfWorkflows)),
                XStringFormats.Center);

            gfx.DrawString(string.Format("This invoice is from: {0}", invoice.Sender), font, XBrushes.Black,
                new XRect(0, page.Height*3/(6+numberOfWorkflows), page.Width, page.Height/(6+numberOfWorkflows)),
                XStringFormats.Center);

            gfx.DrawString(string.Format("Invoice status: {0}", invoice.Status), font, XBrushes.Black,
                new XRect(0, page.Height*4/(6+numberOfWorkflows), page.Width, page.Height/(6+numberOfWorkflows)),
                XStringFormats.Center);

            for (int i = 0; i<numberOfWorkflows; i++)
            {
                gfx.DrawString(string.Format("Workflow: {0}, Hours: {1}, Rate: {2}, Total: {3}", workflows[i].Name, workflows[i].CompletionTime, workflows[i].HourlyRate, workflows[i].CompletionTime*workflows[i].HourlyRate), font, XBrushes.Black,
                new XRect(0, page.Height*(5+i)/(6+numberOfWorkflows), page.Width, page.Height/(6+numberOfWorkflows)),
                XStringFormats.Center);
            }

            gfx.DrawString(string.Format("Your total is: {0}kr", invoice.Amount), font, XBrushes.Black,
                new XRect(0, page.Height*(5+numberOfWorkflows)/(6+numberOfWorkflows), page.Width, page.Height/(6+numberOfWorkflows)),
                XStringFormats.Center);


            try
            {
                await containerClient.CreateIfNotExistsAsync();

                using (MemoryStream blobStream = new MemoryStream())
                {
                    document.Save(blobStream, false);
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