using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Ez.Handball.Ingestion.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    })
    .Build();

await host.RunAsync();
