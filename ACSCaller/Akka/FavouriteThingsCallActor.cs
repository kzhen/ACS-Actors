using Akka.Actor;
using Akka.Event;
using Azure.Communication;
using Azure.Communication.CallAutomation;
using ACSCaller.Models;

namespace ACSCaller.Akka;

public class FavouriteThingsCallActor : ReceiveActor
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
    private int _collectInputCount;
    private CallState _state;

    private enum CallState
    {
        Initial,
        AskMainQuestion,
        AskFavoriteAnimal,
        AskFavoriteBeverage,
        ThankYou,
        Disconnected
    }

    public FavouriteThingsCallActor(CallDetails callDetails, CallAutomationClient callAutomationClient, Models.CallConfiguration callConfiguration)
    {
        _callConfiguration = callConfiguration;
        _callAutomationClient = callAutomationClient;
        _callDetails = callDetails;
        _collectInputCount = 0;
        _state = CallState.Initial;

        Receive<StartCall>(_ =>
        {
            Dial();
            Become(Ringing);
        });
    }

    private void Ringing()
    {
        Receive<CallConnected>(_ =>
        {
            Become(AskMainQuestionState);
            AskMainQuestion();
        });
    }

    private void AskMainQuestionState()
    {
        Receive<RecognizeCompleted>(msg =>
        {
            ProcessMainQuestionResponse(msg.Result);
        });

        Receive<RecognizeFailed>(_ =>
        {
            Reprompt();
        });

        Receive<RecognizeFailedThreeTimes>(_ =>
        {
            Become(CouldntParseResponseAfterThreeAttempts);
            PlayMessage("Couldn't understand what you're trying to say. Goodbye.");
        });

        Receive<CallDisconnected>(_ =>
        {
            Become(Disconnected);
        });
    }

    private void AskFavoriteAnimalState()
    {
        Receive<RecognizeCompleted>(msg =>
        {
            ProcessFavoriteAnimalResponse(msg.Result);
        });

        Receive<RecognizeFailed>(_ =>
        {
            Reprompt();
        });

        Receive<RecognizeFailedThreeTimes>(_ =>
        {
            Become(CouldntParseResponseAfterThreeAttempts);
            PlayMessage("Couldn't understand what you're trying to say. Goodbye.");
        });
    }

    private void AskFavoriteBeverageState()
    {
        Receive<RecognizeCompleted>(msg =>
        {
            ProcessFavoriteBeverageResponse(msg.Result);
        });

        Receive<RecognizeFailed>(_ =>
        {
            Reprompt();
        });

        Receive<RecognizeFailedThreeTimes>(_ =>
        {
            Become(CouldntParseResponseAfterThreeAttempts);
            PlayMessage("Couldn't understand what you're trying to say. Goodbye.");
        });
    }

    private void ThankYouState()
    {
        Receive<PlayFinished>(_ =>
        {
            Hangup();
        });
    }

    private void CouldntParseResponseAfterThreeAttempts()
    {
        Receive<PlayFinished>(_ =>
        {
            Hangup();
        });
    }

    private void Disconnected() { }

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

    private void Reprompt()
    {
        _collectInputCount++;
        if (_collectInputCount > 3)
        {
            Self.Tell(new RecognizeFailedThreeTimes());
        }
        else
        {
            switch (_state)
            {
                case CallState.AskMainQuestion:
                    AskMainQuestion();
                    break;
                case CallState.AskFavoriteAnimal:
                    AskFavoriteAnimal();
                    break;
                case CallState.AskFavoriteBeverage:
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
            _state = CallState.AskFavoriteAnimal;
            Become(AskFavoriteAnimalState);
            AskFavoriteAnimal();
        }
        else if (tone.Equals(DtmfTone.Two))
        {
            _state = CallState.AskFavoriteBeverage;
            Become(AskFavoriteBeverageState);
            AskFavoriteBeverage();
        }
        else
        {
            Reprompt();
        }
    }

    private void ProcessFavoriteAnimalResponse(DtmfResult arg)
    {
        var tone = arg.Tones[0];

        if (tone.Equals(DtmfTone.One) || tone.Equals(DtmfTone.Two) || tone.Equals(DtmfTone.Three))
        {
            _state = CallState.ThankYou;
            Become(ThankYouState);
            PlayMessage("Thank you for your response. Goodbye.");
        }
        else
        {
            Reprompt();
        }
    }

    private void ProcessFavoriteBeverageResponse(DtmfResult arg)
    {
        var tone = arg.Tones[0];

        if (tone.Equals(DtmfTone.One) || tone.Equals(DtmfTone.Two) || tone.Equals(DtmfTone.Three))
        {
            _state = CallState.ThankYou;
            Become(ThankYouState);
            PlayMessage("Thank you for your response. Goodbye.");
        }
        else
        {
            Reprompt();
        }
    }

    private void AskMainQuestion()
    {
        _collectInputCount = 1;

        var prompt = "What do you want to tell us about? Press 1 for favorite animal, press 2 for favorite beverage.";
        RecognizeChoices(prompt);
    }

    private void AskFavoriteAnimal()
    {
        _collectInputCount = 1;

        var prompt = "What is your favorite animal? Press 1 for Cat, press 2 for Dog, press 3 for Monkey.";
        RecognizeChoices(prompt);
    }

    private void AskFavoriteBeverage()
    {
        _collectInputCount = 1;

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
}
