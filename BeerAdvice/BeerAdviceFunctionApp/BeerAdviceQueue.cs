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
        private static readonly string FallbackCity = "Muiden";
        private static readonly bool UseHttps = true;

        private static readonly string FontType = "Arial";
        private static readonly int FontSize = 30;
        private static readonly int ErrorFontSize = 12;
        private static Rgba32 TextColor = Rgba32.DarkSlateGray;
        private static Rgba32 AdviceColor;
        private static Rgba32 ErrorColor = Rgba32.White;


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

            if (!retrievedWeatherData)
            {
                log.LogError("{0}Failed to get weather information. Using {1} as (fallback) city", functionLogPrefix, FallbackCity);
                weatherData = await GetWeatherData(FallbackCity);
            }

            log.LogInformation("{0}Getting map image", functionLogPrefix);
            byte[] image = await GetMapsImage(weatherData.Longitude, weatherData.Latitude);

            bool getBeer = weatherData.Temperature > 14;
            string adviceText = getBeer ? "Get beer" : "Get Jägertee";
            log.LogInformation("{0}Generated following advice: {1}", functionLogPrefix, adviceText);

            log.LogInformation("{0}Adding advice to map image", functionLogPrefix);
            Advice advice = new Advice(adviceText, weatherData.City, weatherData.Temperature.ToString());

            Stream adviceImageStream = AddAdviceToImage(new MemoryStream(image, true), advice, getBeer, retrievedWeatherData, city);
            string imageName = $"{city}-beer_advice-{date}.png";
            UploadImage(adviceImageStream, imageName);
            log.LogInformation("{0}Adding advice to map image", functionLogPrefix);
        }

        private static Stream AddAdviceToImage(Stream imageStream, Advice advice, bool getBeer, bool retrievedWeatherData, string originalCity)
        {
            MemoryStream memoryStream = new MemoryStream();
            var imagesharpImage = Image.Load(imageStream);
            imagesharpImage
                .Clone(image =>
                {
                    int x = 10;
                    int y = 10;

                    if (!retrievedWeatherData)
                    {
                        DrawTextOnImage(image, $"CITY: '{originalCity.ToUpper()}' NOT FOUND. USING DEFAULT CITY: '{FallbackCity.ToUpper()}'", false, ErrorFontSize, x, y, true);
                        y += 20;
                    }

                    DrawAdviceOnImage(image, advice, getBeer, x, y);
                })
                .SaveAsPng(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }

        private static void DrawAdviceOnImage(IImageProcessingContext image, Advice advice, bool getBeer, int x, int y)
        {
            AdviceColor = getBeer ? Rgba32.DarkGreen : Rgba32.Red;

            DrawTextOnImage(image, advice.AdviceText, true, FontSize + 10, x, y);
            y += 50;
            DrawTextOnImage(image, advice.City, false, FontSize, x, y);
            y += 40;
            DrawTextOnImage(image, advice.TemperatureText, false, FontSize, x, y);
        }

        private static void DrawTextOnImage(IImageProcessingContext image, string text, bool advice, int fontSize, int x, int y, bool error = false)
        {
            Rgba32 textColor = (error) ? ErrorColor : (advice) ? AdviceColor : TextColor;
            image.DrawText(text, SystemFonts.CreateFont(FontType, fontSize), textColor, new PointF(x, y));
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
