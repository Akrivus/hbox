using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class TextureGenerator : MonoBehaviour, ISubGenerator
{
    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var text = await LLM.CompleteAsync(await prompt.Resolve(chat.Topic), false);
        var request = await LLM.API.ImagesEndPoint.GenerateImageAsync(
            new OpenAI.Images.ImageGenerationRequest(text, model: "dall-e-3", size: "1792x1024"));
        var image = request.First();

        chat.Texture = image.Texture;

        return chat;
    }
}
