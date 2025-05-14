public class SplashScreenConfigs : IConfig
{
    public string Type => "splash";
    public string[] Splashes { get; set; }
    public float TitleDuration { get; set; }
    public float SplashDuration { get; set; }
}