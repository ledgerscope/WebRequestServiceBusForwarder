using System.Net;
using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace WebRequestServiceBusForwarder;

public class Function1(ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<Function1>();

    [Function("Function1")]
    public async Task<OutputType> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req
        )
    {
        var requestUri = req.Url;
        var cookies = req.Cookies;
        var headers = req.Headers;
        var identities = req.Identities;

        byte[]? body = null;

        if (req.Body != Stream.Null)
        {
            using var memoryStream = new MemoryStream();
            await req.Body.CopyToAsync(memoryStream);
            body = memoryStream.ToArray();
        }

        var outputMessage = new OutputRequest()
        {
            RequestUri = requestUri,
            Cookies = cookies,
            Headers = headers,
            Identities = identities,
            Body = body
        };

        _logger.LogInformation("C# HTTP trigger function processed a request.");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

        response.WriteString("Welcome to Azure Functions!");

        return new OutputType()
        {
            HttpResponse = response,
            OutputEvent = outputMessage
        };
    }
}

public class OutputType
{
    [ServiceBusOutput("RequestQueue", Connection = "ServiceBusConnection")]
    public OutputRequest OutputEvent { get; set; }

    public HttpResponseData HttpResponse { get; set; }
}

public class OutputRequest
{
    public Uri RequestUri { get; internal set; }
    public IReadOnlyCollection<IHttpCookie> Cookies { get; internal set; }
    public HttpHeadersCollection Headers { get; internal set; }
    public IEnumerable<ClaimsIdentity> Identities { get; internal set; }
    public byte[]? Body { get; internal set; }
}
