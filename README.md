# ACS Actors

This project demonstrates to model automated call workflows on Azure Communication Services (ACS) using the two implementations of the Actor framework.

This project explores two popular actor frameworks, Akka.NET and Microsoft Orleans, to implement the workflow logic.

Read the blog post that goes along with this repository - https://www.unravelled.dev/akka-and-microsoft-orleans-a-good-fit-for-programmable-voice/

## Running the solution

To run the solution you will need:
- Access to an Azure subscription, with the following resources created
  - Azure Communication Services (along with purchasing a phone number)
  - Azure Cognitive Services
- ngrok or another localhost tunnelling service

Update the `appsettings.json` with the following values

```
"ConnectionStrings": {
  "acs": "<connection string for your ACS resource>"
},
"cognitiveServicesEndpoint": "<cognitive services endpoint>",
"acsPhonenumber": "<acs phone number that you have purchased>",
"acsCallbackUrl": "<the ngrok tunnel url>"
```

Start ngrok using the following command `ngrok http https://localhost:7010` then put the tunnel url from ngrok into `appsettings.json`.

Run the solution, this will launch the ACSCaller project.

## Initiating an Outbound Call

### Using the Akka.Net callback

Send an http POST request to `/initiate-outboundcall-akka` with the following request body

```
{ "phoneNumber": "<the phone number to call>" }
```

This will start an outbound call to the phone number specified in the request body, all ACS callbacks will be directed to the Akka.Net implementation

### Using the Orleans callback

Send an http POST request to `/initiate-outboundcall-orleans` with the following request body

```
{ "phoneNumber": "<the phone number to call>" }
```

This will start an outbound call to the phone number specified in the request body, all ACS callbacks will be directed to the Orleans implementation
