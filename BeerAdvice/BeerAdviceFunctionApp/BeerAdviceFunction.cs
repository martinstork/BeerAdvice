using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Queue;

namespace BeerAdvice
{
    public static class BeerAdviceFunction
    {
        private static readonly string StorageName = Environment.GetEnvironmentVariable("StorageName");
        private static readonly string StorageKey = Environment.GetEnvironmentVariable("StorageKey");
        private static readonly string StorageUrl = $"https://{Environment.GetEnvironmentVariable("StorageName")}.blob.core.windows.net/{Environment.GetEnvironmentVariable("ContainerReference")}/";

        [FunctionName("BeerAdviceFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            string functionLogPrefix = "[BeerAdviceFunction] ";
            log.LogInformation("{0}Queue triggered", functionLogPrefix);

            string city = req.Query["city"].ToString();
            if (city == null || city.Trim().Length == 0) return new BadRequestObjectResult("Please enter a city on the query string. For example use city Muiden '?city=muiden'");

            // Make city name readable
            if (city.Length > 1) city = char.ToUpper(city[0]) + city.Substring(1).ToLower();
            else city = city.ToUpper();

            CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(new StorageCredentials(StorageName, StorageKey), true);
            CloudQueueClient cloudQueueClient = cloudStorageAccount.CreateCloudQueueClient();
            CloudQueue cloudQueue = cloudQueueClient.GetQueueReference("beeradvicequeue");
            await cloudQueue.CreateIfNotExistsAsync();

            String date = DateTime.Now.ToString("dd-MM-yyyy_HH-mm");
            String cloudQueueMessage = $"{city}|{date}";
            await cloudQueue.AddMessageAsync(new CloudQueueMessage(cloudQueueMessage));
            string imageName = $"{city}-beer_advice-{date}.png";
            string imageUrl = StorageUrl + imageName;

            log.LogInformation("{0}Generated image url: {1}", functionLogPrefix, imageUrl);
            return new OkObjectResult(imageUrl);
        }
    }
}
