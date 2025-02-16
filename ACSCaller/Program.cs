using ACSCaller;
using ACSCaller.Akka;
using ACSCaller.Models;
using ACSCaller.Orleans;
using Akka.Actor;
using Akka.Hosting;
using Azure.Communication.CallAutomation;
using Azure.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Your ACS resource connection string
var acsConnectionString = builder.Configuration.GetConnectionString("acs");

var acsPhonenumber = builder.Configuration.GetValue<string>("acsPhonenumber")!;
var callbackUriHost = builder.Configuration.GetValue<string>("acsCallbackUrl")!; //comes from aspire or appsettings.json
var cognitiveServiceEndpoint = builder.Configuration.GetValue<string>("cognitiveServicesEndpoint")!;

var akkaCallConfiguration = new CallConfiguration { CallbackUri = new Uri($"{callbackUriHost}/api/Callback-akka"), CallerPhoneNumber = acsPhonenumber, CognitiveServiceEndpoint = cognitiveServiceEndpoint };
var orleansCallConfiguration = new CallConfiguration { CallbackUri = new Uri($"{callbackUriHost}/api/Callback-orleans"), CallerPhoneNumber = acsPhonenumber, CognitiveServiceEndpoint = cognitiveServiceEndpoint };

builder.Services.AddEndpointsApiExplorer();

builder.UseOrleans(builder =>
{
    builder.UseLocalhostClustering();
    builder.UseDashboard();
});

builder.Services.AddAkka("acs", builder =>
{
    builder.WithActors((system, registry, resolver) =>
    {
        var callAutomationClient = resolver.GetService<CallAutomationClient>();

        var parent = system.ActorOf(Props.Create(() => new CallCoordinatorActor(callAutomationClient, akkaCallConfiguration)));
        
        registry.Register<CallCoordinatorActor>(parent);
    });
});

builder.Services.AddSingleton(new CallAutomationClient(acsConnectionString));

var app = builder.Build();


app.MapPost("/initiate-outboundcall-akka", (StartCallRequest request, IRequiredActor<CallCoordinatorActor> callCoordinator) =>
{
    var instance = new CallDetails
    {
        PhoneNumber = request.PhoneNumber,
        Id = Guid.NewGuid()
    };

    callCoordinator.ActorRef.Tell(new CallCoordinatorActor.StartNewCall(instance));

    return Results.Ok();
});

app.MapPost("/initiate-outboundcall-orleans", async (StartCallRequest request, IGrainFactory factory) =>
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

app.MapPost("/api/callback-akka", (CloudEvent[] cloudEvents, IRequiredActor<CallCoordinatorActor> callCoordinator) =>
{
    foreach (var cloudEvent in cloudEvents)
    {
        var evnt = CallAutomationEventParser.Parse(cloudEvent);


        if (evnt == null || string.IsNullOrWhiteSpace(evnt.OperationContext))
        {
            // ???

            return Results.Ok();
        }


        var id = evnt.OperationContext;

        if (evnt is CallConnected)
        {
            callCoordinator.ActorRef.Tell(new FavouriteThingsCallActor.CallConnected() { Id = id });
        }
        if (evnt is CallDisconnected)
        {
            callCoordinator.ActorRef.Tell(new FavouriteThingsCallActor.CallDisconnected() { Id = id });
        }
        if (evnt is RecognizeCompleted recognizeCompleted)
        {
            var result = recognizeCompleted.RecognizeResult is DtmfResult dtmfResult;

            if (result)
            {
                var inputFromPhone = (recognizeCompleted.RecognizeResult as DtmfResult)!;
                callCoordinator.ActorRef.Tell(new FavouriteThingsCallActor.RecognizeCompleted() { Result = inputFromPhone, Id = id });
            }
        }
        if (evnt is PlayCompleted playCompleted)
        {
            callCoordinator.ActorRef.Tell(new FavouriteThingsCallActor.PlayFinished() { Id = id });
        }
        if (evnt is RecognizeFailed failed)
        {
            callCoordinator.ActorRef.Tell(new FavouriteThingsCallActor.RecognizeFailed() { Id = id });
        }



    }
    return Results.Ok();
});



app.Run();
