using Azure.Communication;
using Azure.Communication.CallAutomation;
using ACSCaller.Models;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ACSCaller.Orleans;
public abstract class BaseCallGrain : Grain, ICallGrain
{
    protected readonly ILogger _logger;
    protected readonly CallAutomationClient _callAutomationClient;
    protected CallConnection _callConnection;
    protected int _collectInputCount;
    protected string _id;

    protected BaseCallGrain(ILogger logger, CallAutomationClient callAutomationClient)
    {
        _logger = logger;
        _callAutomationClient = callAutomationClient;
    }

    public abstract Task StartCall(CallDetails details, CallConfiguration orleansCallConfiguration);
    public abstract Task CallConnected();
    public abstract Task CallDisconnected();
    public abstract Task RecognizeCompleted(string result);
    public abstract Task RecognizeFailed();
    public abstract Task PlayFinished();

    protected void Dial(string phoneNumber, string operationContext, CallConfiguration orleansCallConfiguration)
    {
        PhoneNumberIdentifier target = new PhoneNumberIdentifier(phoneNumber);
        PhoneNumberIdentifier caller = new PhoneNumberIdentifier(orleansCallConfiguration.CallerPhoneNumber);

        CallInvite callInvite = new CallInvite(target, caller);
        var createCallOptions = new CreateCallOptions(callInvite, orleansCallConfiguration.CallbackUri)
        {
            CallIntelligenceOptions = new CallIntelligenceOptions() { CognitiveServicesEndpoint = new Uri(orleansCallConfiguration.CognitiveServiceEndpoint) },
            OperationContext = operationContext
        };

        CreateCallResult createCallResult = _callAutomationClient.CreateCall(createCallOptions);
        _callConnection = createCallResult.CallConnection;
    }

    protected void Hangup()
    {
        _callConnection.HangUp(true);
        Console.WriteLine("Hangup");
    }

    protected void PlayMessage(string message, string operationContext)
    {
        var playSource = new TextSource(message) { VoiceName = "en-GB-SoniaNeural" };
        var options = new PlayToAllOptions(playSource)
        {
            OperationContext = operationContext
        };
        _callConnection.GetCallMedia().PlayToAll(options);
        Console.WriteLine("Play message: " + message);
    }

    protected void RecognizeChoices(string prompt, string operationContext)
    {
        var playSource = new TextSource(prompt) { VoiceName = "en-GB-SoniaNeural" };

        var participants = _callConnection.GetParticipants();
        var val = participants.Value;

        var options = new CallMediaRecognizeDtmfOptions(_callConnection.GetParticipants().Value.First().Identifier, 1)
        {
            InterruptCallMediaOperation = true,
            InterruptPrompt = true,
            InitialSilenceTimeout = TimeSpan.FromSeconds(10),
            Prompt = playSource,
            OperationContext = operationContext
        };

        var result = _callConnection.GetCallMedia().StartRecognizing(options);
    }
}
