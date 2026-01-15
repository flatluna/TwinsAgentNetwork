using System.ComponentModel;

public static class GlobalTools
{
    [System.ComponentModel.Description("Get the weather for a given location.")]
    public static string GetWeather([System.ComponentModel.Description("The location to get the weather for.")] string location)
        => $"The weather in {location} is cloudy with a high of 15°C.";
}