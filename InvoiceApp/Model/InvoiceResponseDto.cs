using System;
using System.Collections.Generic;

namespace InvoiceApp.Model
{
    public class InvoiceResponseDto
    {
        public string Id { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime? UpdatedDate { get; set; }

        public DateTime SentDate { get; set; }

        public InvoiceStatus Status { get; set; }

        public string Sender { get; set; }

        public string Receiver { get; set; }

        public int Amount { get; set; }

        public string PdfBlobLink { get; set; }

        public ICollection<Workflow> Workflows { get; set; }
    }
}