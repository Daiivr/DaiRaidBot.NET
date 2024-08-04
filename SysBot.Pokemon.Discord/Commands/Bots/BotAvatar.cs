using Discord;
using Discord.Commands;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class BotAvatar : ModuleBase<SocketCommandContext>
    {
        [Command("setavatar")]
        [Alias("botavatar", "changeavatar", "sa", "ba")]
        [Summary("Establece el avatar del bot a un GIF especificado.")]
        [RequireOwner]
        public async Task SetAvatarAsync()
        {
            if (Context.Message.Attachments.Count == 0)
            {
                await ReplyAsync("Por favor, adjunta una imagen GIF para establecer como avatar."); // las imágenes estándar (aburridas) se pueden configurar a través del panel de control
                return;
            }
            var attachment = Context.Message.Attachments.First();
            if (!attachment.Filename.EndsWith(".gif"))
            {
                await ReplyAsync("Por favor, proporciona una imagen GIF.");
                return;
            }

            using var httpClient = new HttpClient();
            var imageBytes = await httpClient.GetByteArrayAsync(attachment.Url);

            using var ms = new MemoryStream(imageBytes);
            var image = new Image(ms);
            await Context.Client.CurrentUser.ModifyAsync(user => user.Avatar = image);

            await ReplyAsync("¡Avatar actualizado con éxito!");
        }
    }
}