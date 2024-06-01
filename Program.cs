using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddAzureClients(builder =>
        {
            var serviceBusNamespace = context.Configuration["ServiceBusConnection:fullyQualifiedNamespace"];
            builder.AddServiceBusClientWithNamespace(serviceBusNamespace);

            var queueName = context.Configuration["ServiceBusQueueName"];
            builder.AddClient<ServiceBusSender, ServiceBusClientOptions>((_, _, provider) => provider.GetRequiredService<ServiceBusClient>().CreateSender(queueName));

            builder.UseCredential(new DefaultAzureCredential());
        });
    })
    .Build();

host.Run();
