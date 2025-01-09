using ACSCaller;
using ACSCaller.Akka;
using ACSCaller.Models;
using ACSCaller.Orleans;
using Azure.Communication.CallAutomation;
using Azure.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddEndpointsApiExplorer();

builder.UseOrleans(builder =>
{
    builder.UseLocalhostClustering();
    builder.UseDashboard();
});

builder.Services.AddSingleton<IActorBridge, AkkaService>();
builder.Services.AddHostedService<AkkaService>(sp => (AkkaService)sp.GetRequiredService<IActorBridge>());

// Your ACS resource connection string
var acsConnectionString = builder.Configuration.GetConnectionString("acs");

var acsPhonenumber = builder.Configuration.GetValue<string>("acsPhonenumber")!;
var callbackUriHost = builder.Configuration.GetValue<string>("acsCallbackUrl")!; //comes from aspire or appsettings.json
var cognitiveServiceEndpoint = builder.Configuration.GetValue<string>("cognitiveServicesEndpoint")!;

builder.Services.AddSingleton(new CallAutomationClient(acsConnectionString));

var akkaCallConfiguration = new CallConfiguration { CallbackUri = new Uri($"{callbackUriHost}/api/Callback-akka"), CallerPhoneNumber = acsPhonenumber, CognitiveServiceEndpoint = cognitiveServiceEndpoint };
var orleansCallConfiguration = new CallConfiguration { CallbackUri = new Uri($"{callbackUriHost}/api/Callback-orleans"), CallerPhoneNumber = acsPhonenumber, CognitiveServiceEndpoint = cognitiveServiceEndpoint };

var app = builder.Build();
app.MapDefaultEndpoints();


app.MapPost("/initiate-outboundcall-akka", async (StartCallRequest request, IActorBridge bridge) =>
{
    var instance = new CallDetails
    {
        PhoneNumber = request.PhoneNumber,
        Id = Guid.NewGuid()
    };

    await bridge.StartCall(instance, akkaCallConfiguration);


    return Results.Ok();
});

app.MapPost("/initiate-outboundcall-orleans", async (string magicString, StartCallRequest request, IGrainFactory factory) =>
{
    var instance = new CallDetails
    {
        PhoneNumber = request.PhoneNumber,
        Id = Guid.NewGuid()
    };

    ICallGrain grain = factory.GetGrain<IFavouriteThingGrain>(instance.Id);

    await grain.StartCall(instance, orleansCallConfiguration);

    return Results.Ok(new { instance.Id });
});

app.MapPost("/api/callback-orleans", async (CloudEvent[] cloudEvents, IGrainFactory factory) =>
{
    foreach (var cloudEvent in cloudEvents)
    {
        var evnt = CallAutomationEventParser.Parse(cloudEvent);
        if (evnt == null)
        {
            return Results.Ok();
        }

        if (string.IsNullOrWhiteSpace(evnt.OperationContext)) { continue; }

        var callId = evnt.OperationContext.Split("|");

        ICallGrain grain = factory.GetGrain<IFavouriteThingGrain>(Guid.Parse(callId[1]));

        switch (evnt)
        {
            case CallConnected:
                await grain.CallConnected();
                break;
            case CallDisconnected:
                await grain.CallDisconnected();
                break;
            case RecognizeCompleted recognizeCompleted:
                var inputFromPhone = (recognizeCompleted.RecognizeResult as DtmfResult)!;
                var tone = inputFromPhone.Tones[0].ToChar().ToString();
                await grain.RecognizeCompleted(tone);
                break;
            case RecognizeFailed:
                await grain.RecognizeFailed();
                break;
            case PlayCompleted:
                await grain.PlayFinished();
                break;
        }
    }
    return Results.Ok();
});

app.MapPost("/api/callback-akka", async (CloudEvent[] cloudEvents, IActorBridge bridge) =>
{
    foreach (var cloudEvent in cloudEvents)
    {
        var evnt = CallAutomationEventParser.Parse(cloudEvent);


        if (evnt == null)
        {
            // ???

            return Results.Ok();
        }

        await bridge.ProcessEvent(evnt);
    }
    return Results.Ok();
});



app.Run();