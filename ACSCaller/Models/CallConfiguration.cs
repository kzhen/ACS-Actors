namespace ACSCaller.Models
{
    [GenerateSerializer]
    public class CallConfiguration
    {
        [Id(0)]
        public required string CallerPhoneNumber { get; set; }
        [Id(1)]
        public required Uri CallbackUri { get; set; }
        [Id(2)]
        public required string CognitiveServiceEndpoint { get; set; }
    }
}
