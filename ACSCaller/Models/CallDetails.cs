namespace ACSCaller;

[GenerateSerializer]
public class CallDetails
{
    [Id(0)]
    public Guid Id { get; set; }
    [Id(1)]
    public required string PhoneNumber { get; set; }
}