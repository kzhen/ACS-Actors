using ACSCaller.Models;
using Akka.Actor;
using Azure.Communication.CallAutomation;

namespace ACSCaller.Akka
{
    public class CallCoordinatorActor : ReceiveActor
    {
        public CallCoordinatorActor(CallAutomationClient callAutomationClient, CallConfiguration callConfiguration)
        {
            Receive<StartNewCall>(details =>
            {
                var props = Props.Create(() => new FavouriteThingsCallActor(details.Instance, callAutomationClient, callConfiguration));

                var callActor = Context.Child(details.Instance.Id.ToString()).GetOrElse(() => Context.ActorOf(props, details.Instance.Id.ToString()));
                callActor.Tell(new FavouriteThingsCallActor.StartCall());
            });

            Receive<FavouriteThingsCallActor.BaseACSEvent>(@event =>
            {
                //var props = Props.Create(() => new FavouriteThingsCallActor(details.Instance, callAutomationClient, callConfiguration));

                var callActor = Context.Child(@event.Id);
                if (callActor == null)
                {
                    // ???
                    throw new Exception("actor doesnt exist??!");
                }

                callActor.Tell(@event);
            });
        }

        internal class StartNewCall
        {
            public CallDetails Instance { get; set; }

            public StartNewCall(CallDetails instance)
            {
                Instance = instance;
            }
        }
    }
}
