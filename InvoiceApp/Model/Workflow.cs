namespace InvoiceApp.Model
{
    public class Workflow
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public float CompletionTime { get; set; }

        public int HourlyRate { get; set; }
    }
}