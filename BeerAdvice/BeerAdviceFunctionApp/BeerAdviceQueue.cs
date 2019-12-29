using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BeerAdvice.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;

namespace BeerAdvice
{
    public static class BeerAdviceQueue
    {
        private static readonly string OpenWeatherKey = Environment.GetEnvironmentVariable("OpenWeatherKey");
        private static readonly string OpenWeatherBaseAddress = "https://api.openweathermap.org/data/2.5/weather";
        private static readonly string MapsKey = Environment.GetEnvironmentVariable("MapsKey");
        private static readonly string MapsUrl = $"https://atlas.microsoft.com/map/static/png?subscription-key={MapsKey}&api-version=1.0&layer=basic&style=main";
        private static readonly string StorageName = Environment.GetEnvironmentVariable("StorageName");
        private static readonly string StorageKey = Environment.GetEnvironmentVariable("StorageKey");
        private static readonly string ContainerReference = Environment.GetEnvironmentVariable("ContainerReference");
        private static readonly string FallbackCity = "Amsterdam";
        private static readonly bool UseHttps = true;

        [FunctionName("BeerAdviceQueue")]
        public static async Task Run([QueueTrigger("beeradvicequeue", Connection = "AzureWebJobsStorage")]string cloudQueueMessage, ILogger log)
        {
            string functionLogPrefix = "[BeerAdviceQueue] ";
            log.LogInformation("{0}Queue triggered", functionLogPrefix);

            string[] cloudQueueMessageParts = cloudQueueMessage.Split('|');
            string city = cloudQueueMessageParts[0];
            string date = cloudQueueMessageParts[1];

            log.LogInformation("{0}Getting weather information for: {1}", functionLogPrefix, city);
            WeatherData weatherData = await GetWeatherData(city);
            bool retrievedWeatherData = weatherData != null;

            if (!retrievedWeatherData) {
                log.LogError("{0}Failed to get weather information. Using {1} as (fallback) city", functionLogPrefix, FallbackCity);
                weatherData = await GetWeatherData(FallbackCity);
            }

            log.LogInformation("{0}Getting map image", functionLogPrefix);
            byte[] image = await GetMapsImage(weatherData.Longitude, weatherData.Latitude);

            bool getBeer = weatherData.Temperature > 14;
            string adviceText = getBeer ? "Get beer" : "Get Jägertee";
            log.LogInformation("{0}Generated following advice: {1}", functionLogPrefix, adviceText);

            log.LogInformation("{0}Adding advice to map image", functionLogPrefix);
            string[] adviceLines = new string[3];
            adviceLines[0] = "Advice: " + adviceText;
            adviceLines[1] = "City: " + weatherData.City;
            adviceLines[2] = "Temperature: " + weatherData.Temperature.ToString() + "°C";

            Stream adviceImageStream = AddAdviceToImage(new MemoryStream(image, true), adviceLines, getBeer, retrievedWeatherData, city);
            string imageName = $"{city}-beer_advice-{date}.png";
            UploadImage(adviceImageStream, imageName);
            log.LogInformation("{0}Adding advice to map image", functionLogPrefix);
        }

        private static Stream AddAdviceToImage(Stream imageStream, string[] adviceLines, bool getBeer, bool retrievedWeatherData, string originalCity)
        {
            MemoryStream memoryStream = new MemoryStream();
            var imagesharpImage = Image.Load(imageStream);
            imagesharpImage
                .Clone(image =>
                {
                    int x = 10;
                    int y = 10;
                    Rgba32 textColor= Rgba32.DarkSlateGray;
                    Rgba32 adviceColor = getBeer ? Rgba32.DarkGreen : Rgba32.Red;
                    int textSize = 30;
                    string fontType = "Arial";

                    if (!retrievedWeatherData)
                    {
                        image.DrawText("FAILED TO GET DATA FOR CITY: " + originalCity, SystemFonts.CreateFont(fontType, 15), Rgba32.Black, new PointF(x, y));
                        adviceLines[1] = adviceLines[1] + "(default)";
                        y += 20;
                    }

                    // Draw advice
                    image.DrawText(adviceLines[0], SystemFonts.CreateFont(fontType, textSize + 10), adviceColor, new PointF(x, y));
                    y += 50;

                    // Draw rest of advicelines
                    for (int i = 1; i < adviceLines.Length; i++)
                    {
                        image.DrawText(adviceLines[i], SystemFonts.CreateFont(fontType, textSize), textColor, new PointF(x, y));
                        y += 40;
                    }
                })
                .SaveAsPng(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }

        private static void UploadImage(Stream imageStream, string imageName)
        {
            StorageCredentials storageCredentials = new StorageCredentials(StorageName, StorageKey);
            CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(storageCredentials, UseHttps);
            CloudBlobClient cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();
            CloudBlobContainer cloudBlobContainer = cloudBlobClient.GetContainerReference(ContainerReference);
            cloudBlobContainer.CreateIfNotExistsAsync();
            CloudBlockBlob cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference(imageName);
            cloudBlockBlob.UploadFromStreamAsync(imageStream);
        }

        private static async Task<WeatherData> GetWeatherData(string city)
        {
            using (HttpClient client = new HttpClient())
            {
                client.BaseAddress = new Uri(OpenWeatherBaseAddress);
                client.DefaultRequestHeaders.Accept.Clear();
                string weatherDataRequestUrl = $"{OpenWeatherBaseAddress}?q={city}&appid={OpenWeatherKey}";
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await client.GetAsync(weatherDataRequestUrl);
                if (!response.IsSuccessStatusCode) return null;

                string responseAsString = await response.Content.ReadAsStringAsync();
                JObject jObject = JObject.Parse(responseAsString);

                JToken coord = jObject["coord"];
                JToken main = jObject["main"];
                JToken sys = jObject["sys"];

                double kelvin = double.Parse(main["temp"].ToString());
                double celsius = kelvin - 273.15;

                WeatherData weatherData = new WeatherData
                {
                    City = city,
                    Country = sys["country"].ToString(),
                    Longitude = coord["lon"].ToString(),
                    Latitude = coord["lat"].ToString(),
                    Temperature = Math.Round(celsius, 2)
            };

                return weatherData;
            }
        }

        private static async Task<byte[]> GetMapsImage(string longitude, string latitude)
        {
            using (HttpClient client = new HttpClient())
            {
                client.BaseAddress = new Uri(OpenWeatherBaseAddress);
                client.DefaultRequestHeaders.Accept.Clear();
                string requestUri = $"{MapsUrl}&center={longitude},{latitude}";
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = await client.GetAsync(requestUri);
                byte[] responseAsString = await response.Content.ReadAsByteArrayAsync();
                return responseAsString;
            }
        }
    }
}
