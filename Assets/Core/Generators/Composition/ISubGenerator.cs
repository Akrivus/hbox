using System.Threading.Tasks;

public interface ISubGenerator
{
    Task<Chat> Generate(PromptResolver prompt, Chat chat);

    public interface Sync : ISubGenerator
    {
        Task<Chat> ISubGenerator.Generate(PromptResolver prompt, Chat chat)
        {
            return Task.FromResult(Generate(prompt, chat));
        }

        new Chat Generate(PromptResolver prompt, Chat chat);
    }
}