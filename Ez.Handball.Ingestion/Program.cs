using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ez.Handball.Ingestion;

internal static class Program
{
    internal static async Task Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;

                services.AddHttpClient<IHsiApiClient, HsiApiClient>(client =>
                {
                    client.BaseAddress = new Uri(config["HsiApiBaseUrl"] ?? "https://hsi.is");
                });

                var storageConnection = config["HandballStorageConnection"]
                    ?? "UseDevelopmentStorage=true";

                services.AddSingleton(_ => new BlobServiceClient(storageConnection));
                services.AddSingleton<IBlobArchiver>(sp =>
                {
                    var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
                    var containerName = config["BlobContainerName"] ?? "raw";
                    return new BlobArchiver(blobServiceClient, containerName);
                });

                services.AddSingleton(_ => new TableServiceClient(storageConnection));
                services.AddSingleton<ITableWriter, TableWriter>();
                services.AddSingleton<IMatchParser, MatchParser>();
                services.AddSingleton<IPlayerParser, PlayerParser>();

                services.AddSingleton(_ => new QueueServiceClient(storageConnection));
                services.AddHttpClient<IHbStatzClient, HbStatzClient>(client =>
                {
                    client.BaseAddress = new Uri("https://hbstatz.is/");
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("EzHandball-Ingestion/1.0 (+https://github.com/kromby/Ez.Handball.Backend)");
                });
                services.AddSingleton<IMatchReportClient, MatchReportClient>();
            })
            .Build();

        await host.RunAsync();
    }
}
