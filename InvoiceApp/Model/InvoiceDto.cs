using System.Collections;
using System.Collections.Generic;

namespace InvoiceApp.Model
{
    public class InvoiceDto
    {
        public string Receiver { get; set; }

        public int Amount { get; set; }

        public List<Workflow> Workflows { get; set; }
    }
}