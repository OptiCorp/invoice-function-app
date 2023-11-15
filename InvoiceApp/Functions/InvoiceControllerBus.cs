using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Grpc.Core;
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
		public async Task Run(
			[ServiceBusTrigger("generate-invoice", "generate-invoice-function", Connection = "connectionStringBus")] string mySbMsg)
		{
			var connectionString = Environment.GetEnvironmentVariable("connectionStringBus");
			var sbClient = new ServiceBusClient(connectionString);

			InvoiceRequestDto invoiceDto = JsonSerializer.Deserialize<InvoiceRequestDto>(mySbMsg);

			if (invoiceDto == null || invoiceDto.Workflows.Count == 0 || invoiceDto.Workflows == null)
			{
				await ReturnError(sbClient, null, "Invoicing failed due to no checklists being provided", invoiceDto.Sender);
				return;
			}

			var workflowsSerialized = JsonSerializer.Serialize<ICollection<Workflow>>(invoiceDto.Workflows);

			Invoice invoice = new Invoice
			{
				Title = invoiceDto.Title,
				CreatedDate = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time")),
				SentDate = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time")),
				Status = InvoiceStatus.Unpaid,
				Sender = invoiceDto.Sender,
				Receiver = invoiceDto.Receiver,
				Amount = invoiceDto.Amount,
				PdfBlobLink = Guid.NewGuid().ToString(),
				WorkflowsSerialized = workflowsSerialized
			};

			await _context.Invoice.AddAsync(invoice);
			await _context.SaveChangesAsync();

			var client = new HttpClient();
			var response = await client.PostAsync(
				string.Format(Environment.GetEnvironmentVariable("GeneratePdfEndpoint") + "{0}", invoice.Id),
				null
			);


			if (response.StatusCode != HttpStatusCode.OK)
			{
				// Use this when notifications work:
				await ReturnError(sbClient, invoice, await response.Content.ReadAsStringAsync(), invoice.Sender);
				return;
			}

			await ReturnInvoice(sbClient, invoice);
		}

		public async Task ReturnInvoice(ServiceBusClient client, Invoice invoice)
		{
			InvoiceResponseDto invoiceResponse = new InvoiceResponseDto
			{
				Id = invoice.Id,
				Number = invoice.Number,
				Title = invoice.Title,
				CreatedDate = invoice.CreatedDate,
				SentDate = invoice.SentDate,
				Status = invoice.Status,
				Sender = invoice.Sender,
				Receiver = invoice.Receiver,
				Amount = invoice.Amount,
				PdfBlobLink = invoice.PdfBlobLink,
				Workflows = JsonSerializer.Deserialize<ICollection<Workflow>>(invoice.WorkflowsSerialized)
			};

			var success = new InvoiceNotification
			{
				Message = "Invoice created successfully",
				ReceiverId = invoice.Sender,
				NotificationType = "Success"
			};

			var notificationSender = client.CreateSender("notification");
			var notificationBody = JsonSerializer.Serialize(success);
			var notificationMessage = new ServiceBusMessage(notificationBody);
			await notificationSender.SendMessageAsync(notificationMessage);

			var invoiceSender = client.CreateSender("add-invoice");
			var invoiceBody = JsonSerializer.Serialize(invoiceResponse);
			var invoiceMessage = new ServiceBusMessage(invoiceBody);
			await invoiceSender.SendMessageAsync(invoiceMessage);
		}

		public async Task ReturnError(ServiceBusClient client, Invoice invoice, string errorMessage, string sender)
		{
			if (invoice != null)
			{
				_context.Invoice.Remove(invoice);
				await _context.SaveChangesAsync();
			}

			var error = new InvoiceNotification
			{
				Message = errorMessage,
				ReceiverId = sender,
				NotificationType = "Error"
			};

			var notificationSender = client.CreateSender("notification");
			var messageBody = JsonSerializer.Serialize(error);
			var sbMessage = new ServiceBusMessage(messageBody);
			await notificationSender.SendMessageAsync(sbMessage);
		}
	}
}
