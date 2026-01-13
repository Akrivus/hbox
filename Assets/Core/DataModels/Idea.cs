using System;
using System.Threading.Tasks;

[Serializable]
public class Idea
{
    public string Prompt { get; set; }
    public string Author { get; set; }
    public string Source { get; set; }
    public string Slug { get; set; }

    private string NewSlug => Guid.NewGuid().ToString().Substring(0, 7);

    public Idea()
    {
        Author = "polbot";
        Source = "manual";
        Slug = NewSlug;
    }

    public Idea(string prompt) : this()
    {
        Prompt = prompt;
    }

    public Idea(string text, string author, string source, string slug = null)
    {
        Prompt = text;
        Author = author;
        Source = source;

        if (string.IsNullOrEmpty(slug))
            slug = NewSlug;
        Slug = slug;
    }
}