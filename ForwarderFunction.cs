using System.Security.Cryptography;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Collections.Concurrent;

namespace WebRequestServiceBusForwarder;

public class ForwarderFunction(IConfiguration configuration, ServiceBusSender sender)
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull | JsonIgnoreCondition.WhenWritingDefault,
    };

    [Function("ForwarderFunction")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, ["get", "post"], Route = "{*ignored}")] HttpRequestData req
        )
    {
        var requestUri = req.Url;
        var cookies = req.Cookies;
        var headers = req.Headers;
        var identities = req.Identities;

        byte[]? body = null;

        if (req.Body != Stream.Null)
        {
            if (req.Body is MemoryStream ms)
            {
                body = ms.ToArray();
            }
            else
            {
                using var memoryStream = new MemoryStream();
                await req.Body.CopyToAsync(memoryStream);
                body = memoryStream.ToArray();
            }
        }

        if (headers.TryGetValues("x-xero-signature", out var results))
        {
            var path = req.Url.LocalPath[1..];
            var signatureBytes = results.First();
            using var sr = new StreamReader(new MemoryStream(body));
            var payload = sr.ReadToEnd();

            if (!_signingKeys.TryGetValue(path, out var signingKeyBytes))
            {
                var signingKey = configuration["XeroKeys:" + path];

                //Xero requires everything be done in UTF-8
                signingKeyBytes = Encoding.UTF8.GetBytes(signingKey);
                _signingKeys.TryAdd(path, signingKeyBytes);
            }

            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

            var signature = Convert.ToBase64String(HMACSHA256.HashData(signingKeyBytes, payloadBytes));

            if (signature != signatureBytes)
            {
                return req.CreateResponse(HttpStatusCode.Unauthorized);
            }
        }

        var outputMessage = new OutputRequest()
        {
            RequestUri = requestUri,
            Body = body
        };

        if (cookies != null)
        {
            outputMessage.Cookies = cookies;
        }

        if (headers != null)
        {
            outputMessage.Headers = headers;
        }

        if (identities != null)
        {
            outputMessage.Identities = identities;
        }

        var msg = new ServiceBusMessage(JsonSerializer.Serialize(outputMessage, jsonSerializerOptions));
        msg.ApplicationProperties.Add("RequestUri", requestUri.ToString());

        await sender.SendMessageAsync(msg);

        var response = req.CreateResponse(HttpStatusCode.OK);
        return response;
    }

    private static readonly ConcurrentDictionary<string, byte[]> _signingKeys = new();
}

public class OutputRequest
{
    public Uri? RequestUri { get; init; }
    public IReadOnlyCollection<IHttpCookie>? Cookies { get; internal set; }
    public HttpHeadersCollection? Headers { get; internal set; }
    public IEnumerable<ClaimsIdentity>? Identities { get; internal set; }
    public byte[]? Body { get; internal set; }
}
