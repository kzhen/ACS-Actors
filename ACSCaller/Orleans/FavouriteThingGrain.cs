using Azure.Communication.CallAutomation;
using ACSCaller.Models;

namespace ACSCaller.Orleans;

public interface IFavouriteThingGrain : ICallGrain
{

}

public class FavouriteThingGrain : BaseCallGrain, IFavouriteThingGrain
{
    private enum CallState
    {
        Initial,
        AskMainQuestion,
        AskFavoriteAnimal,
        AskFavoriteBeverage,
        ThankYou,
        Disconnected
    }

    private string _phoneNumber;
    private CallState _state;

    public FavouriteThingGrain(ILogger<FavouriteThingGrain> logger, CallAutomationClient callAutomationClient)
        : base(logger, callAutomationClient)
    {
        _state = CallState.Initial;
    }

    public override Task StartCall(CallDetails callDetails, CallConfiguration orleansCallConfiguration)
    {
        _phoneNumber = callDetails.PhoneNumber;
        _id = callDetails.Id.ToString();
        Dial(_phoneNumber, $"a|{_id}", orleansCallConfiguration);
        _state = CallState.AskMainQuestion;
        return Task.CompletedTask;
    }

    public override Task CallConnected()
    {
        AskMainQuestion();
        return Task.CompletedTask;
    }

    public override Task CallDisconnected()
    {
        _state = CallState.Disconnected;
        return Task.CompletedTask;
    }

    public override Task RecognizeCompleted(string result)
    {
        switch (_state)
        {
            case CallState.AskMainQuestion:
                ProcessMainQuestionResponse(result);
                break;
            case CallState.AskFavoriteAnimal:
                ProcessFavoriteAnimalResponse(result);
                break;
            case CallState.AskFavoriteBeverage:
                ProcessFavoriteBeverageResponse(result);
                break;
            default:
                _logger.LogWarning("Unexpected state: {0}", _state);
                break;
        }
        return Task.CompletedTask;
    }

    public override Task RecognizeFailed()
    {
        switch (_state)
        {
            case CallState.AskMainQuestion:
            case CallState.AskFavoriteAnimal:
            case CallState.AskFavoriteBeverage:
                Reprompt();
                break;
            default:
                _logger.LogWarning("Unexpected state: {0}", _state);
                break;
        }
        return Task.CompletedTask;
    }

    public override Task PlayFinished()
    {
        Hangup();
        return Task.CompletedTask;
    }

    private void AskMainQuestion()
    {
        var prompt = "What do you want to tell us about? Press 1 for favorite animal, press 2 for favorite beverage.";
        RecognizeChoices(prompt, $"a|{_id}");
    }

    private void ProcessMainQuestionResponse(string tone)
    {
        if (tone.Equals("1"))
        {
            _state = CallState.AskFavoriteAnimal;
            AskFavoriteAnimal();
        }
        else if (tone.Equals("2"))
        {
            _state = CallState.AskFavoriteBeverage;
            AskFavoriteBeverage();
        }
        else
        {
            Reprompt();
        }
    }

    private void AskFavoriteAnimal()
    {
        var prompt = "What is your favorite animal? Press 1 for Cat, press 2 for Dog, press 3 for Monkey.";
        RecognizeChoices(prompt, $"a|{_id}");
    }

    private void ProcessFavoriteAnimalResponse(string tone)
    {
        if (tone.Equals("1") || tone.Equals("2") || tone.Equals("3"))
        {
            _state = CallState.ThankYou;
            PlayMessage("Thank you for your response. Goodbye.", $"a|{_id}");
        }
        else
        {
            Reprompt();
        }
    }

    private void AskFavoriteBeverage()
    {
        var prompt = "What is your favorite beverage? Press 1 for Coffee, press 2 for Tea, press 3 for Red Bull.";
        RecognizeChoices(prompt, $"a|{_id}");
    }

    private void ProcessFavoriteBeverageResponse(string tone)
    {
        if (tone.Equals("1") || tone.Equals("2") || tone.Equals("3"))
        {
            _state = CallState.ThankYou;
            PlayMessage("Thank you for your response. Goodbye.", $"a|{_id}");
        }
        else
        {
            Reprompt();
        }
    }

    private void Reprompt()
    {
        _collectInputCount++;
        if (_collectInputCount > 3)
        {
            PlayMessage("Couldn't understand what you're trying to say. Goodbye.", $"a|{_id}");
            _state = CallState.ThankYou;
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
}