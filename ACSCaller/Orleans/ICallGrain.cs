using ACSCaller.Models;

namespace ACSCaller.Orleans;

public interface ICallGrain : IGrainWithGuidKey
{
    Task StartCall(CallDetails details, CallConfiguration callConfiguration);
    Task CallConnected();
    Task CallDisconnected();
    Task RecognizeCompleted(string result);
    Task RecognizeFailed();
    Task PlayFinished();
}
