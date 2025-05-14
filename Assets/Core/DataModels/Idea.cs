using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class Idea
{
    public string Prompt { get; set; }
    public string Text { get; set; }

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
        Text = Prompt = prompt;
    }

    public Idea(string title, string text, string author, string source, string slug = null)
    {
        if (!string.IsNullOrEmpty(text))
            text = $"{title}: {text}";
        else
            text = title;
        Text = title;
        Prompt = text;
        Author = author;
        Source = source;

        if (string.IsNullOrEmpty(slug))
            slug = NewSlug;
        Slug = slug;
    }

    public async Task<Idea> RePrompt(PromptResolver prompt, string preamble = "")
    {
        await prompt.Resolve(preamble + "\n\n" + Prompt);
        Prompt = prompt.Text;
        return this;
    }
}