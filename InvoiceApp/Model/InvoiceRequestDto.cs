using System.Collections;
using System.Collections.Generic;

namespace InvoiceApp.Model
{
    public class InvoiceRequestDto
    {
        public string Receiver { get; set; }

        public float Amount { get; set; }

        public ICollection<Workflow> Workflows { get; set; }

        public string Title { get; set; }
    }
}
