using System.Collections;
using System.Collections.Generic;

namespace InvoiceApp.Model
{
    public class InvoiceRequestDto
    {
        public string Receiver { get; set; }

        public int Amount { get; set; }

        public ICollection<Workflow> Workflows { get; set; }
    }
}