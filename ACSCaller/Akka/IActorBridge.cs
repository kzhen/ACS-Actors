using ACSCaller.Models;
using Akka.Actor;
using Akka.DependencyInjection;
using Azure.Communication.CallAutomation;

namespace ACSCaller.Akka;

public interface IActorBridge
{
    Task StartCall(CallDetails instance, Models.CallConfiguration akkaCallConfiguration);
    Task ProcessEvent(CallAutomationEventBase evnt);
}

public class AkkaService : IHostedService, IActorBridge
{
    private ActorSystem _actorSystem;
    private readonly IConfiguration _configuration;
    private readonly CallAutomationClient _callAutomationClient;
    private readonly IServiceProvider _serviceProvider;

    private readonly IHostApplicationLifetime _applicationLifetime;

    public AkkaService(IServiceProvider serviceProvider, IHostApplicationLifetime appLifetime, IConfiguration configuration, CallAutomationClient callAutomationClient)
    {
        _serviceProvider = serviceProvider;
        _applicationLifetime = appLifetime;
        _configuration = configuration;
        _callAutomationClient = callAutomationClient;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bootstrap = BootstrapSetup.Create();


        // enable DI support inside this ActorSystem, if needed
        var diSetup = DependencyResolverSetup.Create(_serviceProvider);

        // merge this setup (and any others) together into ActorSystemSetup
        var actorSystemSetup = bootstrap.And(diSetup);

        // start ActorSystem
        _actorSystem = ActorSystem.Create("akka-universe", actorSystemSetup);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        _actorSystem.WhenTerminated.ContinueWith(_ =>
        {
            _applicationLifetime.StopApplication();
        });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // strictly speaking this may not be necessary - terminating the ActorSystem would also work
        // but this call guarantees that the shutdown of the cluster is graceful regardless
        await CoordinatedShutdown.Get(_actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
    }

    public async Task StartCall(CallDetails callDetails, Models.CallConfiguration akkaCallConfiguration)
    {
        var actor = await GetOrCreateFavouriteThingCallActor(callDetails.Id.ToString(), callDetails, _callAutomationClient, akkaCallConfiguration);
        actor.Tell(new FavouriteThingsCallActor.StartCall());
    }

    private async Task<IActorRef> GetOrCreateFavouriteThingCallActor(string actorId, CallDetails callDetails, CallAutomationClient callAutomationClient, Models.CallConfiguration akkaCallConfiguration)
    {
        var actorPath = $"/user/{actorId}";
        var actorSelection = _actorSystem.ActorSelection(actorPath);

        try
        {
            var actorRefTask = await actorSelection.ResolveOne(TimeSpan.FromSeconds(2));

            return actorRefTask;
        }
        catch (Exception ex)
        {
            return _actorSystem.ActorOf(Props.Create(() => new FavouriteThingsCallActor(callDetails, callAutomationClient, akkaCallConfiguration)), actorId);
        }
    }

    public async Task ProcessEvent(CallAutomationEventBase evnt)
    {


        var id = evnt.OperationContext;

        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var actor = await GetOrCreateFavouriteThingCallActor(id, null, null, null);

        if (evnt is CallConnected)
        {
            actor.Tell(new FavouriteThingsCallActor.CallConnected());
        }
        if (evnt is CallDisconnected)
        {
            actor.Tell(new FavouriteThingsCallActor.CallDisconnected());
        }
        if (evnt is RecognizeCompleted recognizeCompleted)
        {
            var result = recognizeCompleted.RecognizeResult is DtmfResult dtmfResult;

            if (result)
            {
                var inputFromPhone = (recognizeCompleted.RecognizeResult as DtmfResult)!;
                actor.Tell(new FavouriteThingsCallActor.RecognizeCompleted() { Result = inputFromPhone });
            }
        }
        if (evnt is PlayCompleted playCompleted)
        {
            actor.Tell(new FavouriteThingsCallActor.PlayFinished());
        }
        if (evnt is RecognizeFailed failed)
        {
            actor.Tell(new FavouriteThingsCallActor.RecognizeFailed());
        }
    }
}