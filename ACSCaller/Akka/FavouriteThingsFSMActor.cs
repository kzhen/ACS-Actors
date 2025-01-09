using Akka.Actor;
using Akka.Event;
using Azure.Communication.CallAutomation;
using Azure.Communication;
using ACSCaller.Models;
using System.Security.Cryptography.X509Certificates;

namespace ACSCaller.Akka;

public class FavouriteThingsFSMCallActor : FSM<FavouriteThingsFSMCallActor.State, FavouriteThingsFSMCallActor.Data>
{
    public class StartCall { }
    public class CallConnected { }
    public class CallDisconnected { }
    public class RecognizeCompleted { public DtmfResult Result { get; set; } }
    public class RecognizeFailed { }
    public class RecognizeFailedThreeTimes { }
    public class PlayFinished { }

    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly CallConfiguration _callConfiguration;
    private readonly CallAutomationClient _callAutomationClient;
    private CallConnection _callConnection;
    private CallDetails _callDetails;

    public FavouriteThingsFSMCallActor(CallDetails callDetails, CallAutomationClient callAutomationClient, CallConfiguration callConfiguration)
    {
        _callConfiguration = callConfiguration;
        _callAutomationClient = callAutomationClient;
        _callDetails = callDetails;

        StartWith(State.Initial, new Data { CollectInputCount = 0 });

        When(State.Initial, state =>
        {
            if (state.FsmEvent is StartCall)
            {
                Dial();
                return GoTo(State.Ringing);
            }
            return null;
        });

        When(State.Ringing, state =>
        {
            if (state.FsmEvent is CallConnected)
            {
                AskMainQuestion();
                return GoTo(State.AskMainQuestion).Using(new Data { CollectInputCount = 1 });
            }
            return null;
        });

        When(State.AskMainQuestion, state =>
        {
            switch (state.FsmEvent)
            {
                case RecognizeCompleted msg:
                    ProcessMainQuestionResponse(msg.Result);
                    return Stay();
                case RecognizeFailed:
                    Reprompt(state.StateData.CollectInputCount);
                    return Stay();
                case RecognizeFailedThreeTimes:
                    PlayMessage("Couldn't understand what you're trying to say. Goodbye.");
                    return GoTo(State.CouldntParseResponseAfterThreeAttempts);
                case CallDisconnected:
                    return GoTo(State.Disconnected);
                default:
                    return null;
            }
        });

        When(State.AskFavoriteAnimal, state =>
        {
            switch (state.FsmEvent)
            {
                case RecognizeCompleted msg:
                    ProcessFavoriteAnimalResponse(msg.Result);
                    return Stay();
                case RecognizeFailed:
                    Reprompt(state.StateData.CollectInputCount);
                    return Stay();
                case RecognizeFailedThreeTimes:
                    PlayMessage("Couldn't understand what you're trying to say. Goodbye.");
                    return GoTo(State.CouldntParseResponseAfterThreeAttempts);
                default:
                    return null;
            }
        });

        When(State.AskFavoriteBeverage, state =>
        {
            switch (state.FsmEvent)
            {
                case RecognizeCompleted msg:
                    ProcessFavoriteBeverageResponse(msg.Result);
                    return Stay();
                case RecognizeFailed:
                    Reprompt(state.StateData.CollectInputCount);
                    return Stay();
                case RecognizeFailedThreeTimes:
                    PlayMessage("Couldn't understand what you're trying to say. Goodbye.");
                    return GoTo(State.CouldntParseResponseAfterThreeAttempts);
                default:
                    return null;
            }
        });

        When(State.ThankYou, state =>
        {
            if (state.FsmEvent is PlayFinished)
            {
                Hangup();
                return Stop();
            }
            return null;
        });

        When(State.CouldntParseResponseAfterThreeAttempts, state =>
        {
            if (state.FsmEvent is PlayFinished)
            {
                Hangup();
                return Stop();
            }
            return null;
        });

        When(State.Disconnected, state => null);

        Initialize();
    }

    private void Dial()
    {
        var transformedPhoneNumber = _callDetails.PhoneNumber;

        PhoneNumberIdentifier target = new PhoneNumberIdentifier(transformedPhoneNumber);
        PhoneNumberIdentifier caller = new PhoneNumberIdentifier(_callConfiguration.CallerPhoneNumber);

        CallInvite callInvite = new CallInvite(target, caller);
        var createCallOptions = new CreateCallOptions(callInvite, _callConfiguration.CallbackUri)
        {
            CallIntelligenceOptions = new CallIntelligenceOptions() { CognitiveServicesEndpoint = new Uri(_callConfiguration.CognitiveServiceEndpoint) },
            OperationContext = _callDetails.Id.ToString()
        };

        CreateCallResult createCallResult = _callAutomationClient.CreateCall(createCallOptions);
        _callConnection = createCallResult.CallConnection;
    }

    private void Reprompt(int collectInputCount)
    {
        if (collectInputCount > 3)
        {
            Self.Tell(new RecognizeFailedThreeTimes());
        }
        else
        {
            switch (StateName)
            {
                case State.AskMainQuestion:
                    AskMainQuestion();
                    break;
                case State.AskFavoriteAnimal:
                    AskFavoriteAnimal();
                    break;
                case State.AskFavoriteBeverage:
                    AskFavoriteBeverage();
                    break;
            }
        }
    }

    private void ProcessMainQuestionResponse(DtmfResult arg)
    {
        var tone = arg.Tones[0];

        if (tone.Equals(DtmfTone.One))
        {
            AskFavoriteAnimal();
            GoTo(State.AskFavoriteAnimal).Using(new Data { CollectInputCount = 1 });
        }
        else if (tone.Equals(DtmfTone.Two))
        {
            AskFavoriteBeverage();
            GoTo(State.AskFavoriteBeverage).Using(new Data { CollectInputCount = 1 });
        }
        else
        {
            Reprompt(StateData.CollectInputCount);
        }
    }

    private void ProcessFavoriteAnimalResponse(DtmfResult arg)
    {
        var tone = arg.Tones[0];

        if (tone.Equals(DtmfTone.One) || tone.Equals(DtmfTone.Two) || tone.Equals(DtmfTone.Three))
        {
            PlayMessage("Thank you for your response. Goodbye.");
            GoTo(State.ThankYou);
        }
        else
        {
            Reprompt(StateData.CollectInputCount);
        }
    }

    private void ProcessFavoriteBeverageResponse(DtmfResult arg)
    {
        var tone = arg.Tones[0];

        if (tone.Equals(DtmfTone.One) || tone.Equals(DtmfTone.Two) || tone.Equals(DtmfTone.Three))
        {
            PlayMessage("Thank you for your response. Goodbye.");
            GoTo(State.ThankYou);
        }
        else
        {
            Reprompt(StateData.CollectInputCount);
        }
    }

    private void AskMainQuestion()
    {
        var prompt = "What do you want to tell us about? Press 1 for favorite animal, press 2 for favorite beverage.";
        RecognizeChoices(prompt);
    }

    private void AskFavoriteAnimal()
    {
        var prompt = "What is your favorite animal? Press 1 for Cat, press 2 for Dog, press 3 for Monkey.";
        RecognizeChoices(prompt);
    }

    private void AskFavoriteBeverage()
    {
        var prompt = "What is your favorite beverage? Press 1 for Coffee, press 2 for Tea, press 3 for Red Bull.";
        RecognizeChoices(prompt);
    }

    private void Hangup()
    {
        _callConnection.HangUp(true);
        Console.WriteLine("Hangup");
    }

    private void PlayMessage(string message)
    {
        var playSource = new TextSource(message) { VoiceName = SpeechToTextVoice };
        var options = new PlayToAllOptions(playSource)
        {
            OperationContext = _callDetails.Id.ToString()
        };
        _callConnection.GetCallMedia().PlayToAll(options);
        Console.WriteLine("Play message: " + message);
    }

    private void RecognizeChoices(string prompt)
    {
        var playSource = new TextSource(prompt) { VoiceName = SpeechToTextVoice };

        var participants = _callConnection.GetParticipants();
        var val = participants.Value;

        var options = new CallMediaRecognizeDtmfOptions(_callConnection.GetParticipants().Value.First().Identifier, 1)
        {
            InterruptCallMediaOperation = true,
            InterruptPrompt = true,
            InitialSilenceTimeout = TimeSpan.FromSeconds(10),
            Prompt = playSource,
            OperationContext = _callDetails.Id.ToString()
        };

        var result = _callConnection.GetCallMedia().StartRecognizing(options);
    }

    private const string SpeechToTextVoice = "en-GB-SoniaNeural";

    public enum State
    {
        Initial,
        Ringing,
        AskMainQuestion,
        AskFavoriteAnimal,
        AskFavoriteBeverage,
        ThankYou,
        CouldntParseResponseAfterThreeAttempts,
        Disconnected
    }

    public class Data
    {
        public int CollectInputCount { get; set; }
    }
}