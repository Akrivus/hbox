using System.Net;
using System.Text;
using System;
using UnityEngine;
using Newtonsoft.Json;
using OpenAI;
using System.IO;

public class YouTubeIntegration : MonoBehaviour, IConfigurable<YouTubeConfigs>
{
    public static YouTubeIntegration Instance => _instance ?? (_instance = FindFirstObjectByType<YouTubeIntegration>());
    private static YouTubeIntegration _instance;

    private string accessToken;
    private string refreshToken;
    private string[] tags;

    public void Configure(YouTubeConfigs config)
    {
        accessToken = config.AccessToken;
        refreshToken = config.RefreshToken;
        tags = config.Tags;
    }

    public string GetUpload(string accessToken, YouTubeMetadata metadata)
    {
        string url = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable";

        using (WebClient client = new WebClient())
        {
            client.Headers.Add("Authorization", $"Bearer {accessToken}");
            client.Headers.Add("Content-Type", "application/json; charset=UTF-8");
            client.Headers.Add("X-Upload-Content-Type", "video/*");

            try
            {
                var json = JsonConvert.SerializeObject(metadata);
                var bytes = Encoding.UTF8.GetBytes(json);
                byte[] res = client.UploadData(url, "POST", bytes);
                return client.ResponseHeaders["Location"];
            }
            catch (WebException ex)
            {
                Console.WriteLine($"Error initiating upload: {ex.Message}");
                return null;
            }
        }
    }

    public void UploadVideo(string location, string videoPath)
    {
        byte[] fileBytes = File.ReadAllBytes(videoPath);

        using (WebClient client = new WebClient())
        {
            client.Headers.Add("Authorization", $"Bearer {accessToken}");
            client.Headers.Add("Content-Length", fileBytes.Length.ToString());
            client.Headers.Add("Content-Type", "video/*");

            try
            {
                byte[] response = client.UploadData(location, "PUT", fileBytes);
                Console.WriteLine("Video uploaded successfully!");
                Console.WriteLine(Encoding.UTF8.GetString(response));
            }
            catch (WebException ex)
            {
                Console.WriteLine($"Error uploading video: {ex.Message}");
            }
        }
    }

    public class YouTubeMetadata
    {
        public YouTubeSnippet snippet { get; set; }
        public YouTubeStatus status { get; set; }
    }

    public class YouTubeSnippet
    {
        public string title { get; set; }
        public string description { get; set; }
        public string[] tags { get; set; }
        public string categoryId { get; set; }
    }

    public class YouTubeStatus
    {
        public string privacyStatus { get; set; }
    }
}
