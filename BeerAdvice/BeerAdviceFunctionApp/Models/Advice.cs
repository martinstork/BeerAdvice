namespace BeerAdvice.Models
{
    public class Advice
    {
        public Advice()
        {
        }

        public Advice(string adviceText, string city, string temperatureText)
        {
            AdviceText = adviceText;
            City = city;
            TemperatureText = temperatureText;
        }

        private string _adviceText;
        public string AdviceText
        {
            get
            {
                return _adviceText;
            }

            set
            {
                _adviceText = "Advice: " + value;
            }
        }

        private string _city;
        public string City
        {
            get
            {
                return _city;
            }

            set
            {
                _city = "City: " + value;
            }
        }

        private string _temperatureText;
        public string TemperatureText
        {
            get
            {
                return _temperatureText;
            }

            set
            {
                _temperatureText = "Temperature: " + value + "°C";
            }
        }
    }
}
