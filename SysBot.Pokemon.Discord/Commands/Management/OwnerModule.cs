using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class OwnerModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private readonly ExtraCommandUtil<T> Util = new();

        [Command("addSudo")]
        [Summary("Adds mentioned user to global sudo")]
        [RequireOwner]
        public async Task SudoUsers([Remainder] string _)
        {
            var users = Context.Message.MentionedUsers;
            var objects = users.Select(GetReference);
            SysCordSettings.Settings.GlobalSudoList.AddIfNew(objects);
            await ReplyAsync("<a:yes:1206485105674166292> Listo.").ConfigureAwait(false);
        }

        [Command("removeSudo")]
        [Summary("Removes mentioned user from global sudo")]
        [RequireOwner]
        public async Task RemoveSudoUsers([Remainder] string _)
        {
            var users = Context.Message.MentionedUsers;
            var objects = users.Select(GetReference);
            SysCordSettings.Settings.GlobalSudoList.RemoveAll(z => objects.Any(o => o.ID == z.ID));
            await ReplyAsync("<a:yes:1206485105674166292> Listo.").ConfigureAwait(false);
        }

        [Command("addChannel")]
        [Summary("Adds a channel to the list of channels that are accepting commands.")]
        [RequireOwner]
        public async Task AddChannel()
        {
            var obj = GetReference(Context.Message.Channel);
            SysCordSettings.Settings.ChannelWhitelist.AddIfNew(new[] { obj });
            await ReplyAsync("<a:yes:1206485105674166292> Listo.").ConfigureAwait(false);
        }

        [Command("removeChannel")]
        [Summary("Removes a channel from the list of channels that are accepting commands.")]
        [RequireOwner]
        public async Task RemoveChannel()
        {
            var obj = GetReference(Context.Message.Channel);
            SysCordSettings.Settings.ChannelWhitelist.RemoveAll(z => z.ID == obj.ID);
            await ReplyAsync("<a:yes:1206485105674166292> Listo.").ConfigureAwait(false);
        }

        [Command("leave")]
        [Alias("bye")]
        [Summary("Leaves the current server.")]
        [RequireOwner]
        public async Task Leave()
        {
            await ReplyAsync("Goodbye.").ConfigureAwait(false);
            await Context.Guild.LeaveAsync().ConfigureAwait(false);
        }

        [Command("leaveguild")]
        [Alias("lg")]
        [Summary("Leaves guild based on supplied ID.")]
        [RequireOwner]
        public async Task LeaveGuild(string userInput)
        {
            if (!ulong.TryParse(userInput, out ulong id))
            {
                await ReplyAsync("<a:warning:1206483664939126795> Proporcione una identificación válida de servidor!").ConfigureAwait(false);
                return;
            }
            var guild = Context.Client.Guilds.FirstOrDefault(x => x.Id == id);
            if (guild is null)
            {
                await ReplyAsync($"<a:yes:1206485105674166292> La entrada proporcionada ({userInput}) no es un ID de server válido o el bot no está en el servidor especificado.").ConfigureAwait(false);
                return;
            }

            await ReplyAsync($"<a:yes:1206485105674166292> Abandonando {guild}.").ConfigureAwait(false);
            await guild.LeaveAsync().ConfigureAwait(false);
        }

        [Command("leaveall")]
        [Summary("Leaves all servers the bot is currently in.")]
        [RequireOwner]
        public async Task LeaveAll()
        {
            await ReplyAsync("<a:yes:1206485105674166292> Abandonando todos los servidores.").ConfigureAwait(false);
            foreach (var guild in Context.Client.Guilds)
            {
                await guild.LeaveAsync().ConfigureAwait(false);
            }
        }

        [Command("listguilds")]
        [Alias("guildlist", "gl")]
        [Summary("Lists all the servers the bot is in.")]
        [RequireOwner]
        public async Task ListGuilds()
        {
            var guilds = Context.Client.Guilds.OrderBy(guild => guild.Name);
            var guildList = new StringBuilder();
            guildList.AppendLine("\n");
            foreach (var guild in guilds)
            {
                guildList.AppendLine($"{Format.Bold($"{guild.Name}")}\nID: {guild.Id}\n");
            }
            await Util.ListUtil(Context, "Aquí hay una lista de todos los servidores en los que se encuentra actualmente este bot.", guildList.ToString()).ConfigureAwait(false);
        }

        [Command("sudoku")]
        [Alias("kill", "shutdown")]
        [Summary("Causes the entire process to end itself!")]
        [RequireOwner]
        public async Task ExitProgram()
        {
            await Context.Channel.EchoAndReply("<a:yes:1206485105674166292> Cerrando... ¡adiós! **Los servicios del bot se están desconectando.**").ConfigureAwait(false);
            Environment.Exit(0);
        }

        [Command("dm")]
        [Summary("Sends a direct message to a specified user.")]
        [RequireOwner]
        public async Task DMUserAsync(SocketUser user, [Remainder] string message)
        {
            var attachments = Context.Message.Attachments;
            var hasAttachments = attachments.Count != 0;
            List<string> imageUrls = new List<string>();
            List<string> nonImageAttachmentUrls = new List<string>();

            // Collect image and non-image attachments separately
            foreach (var attachment in attachments)
            {
                if (attachment.Filename.EndsWith(".png") || attachment.Filename.EndsWith(".jpg") || attachment.Filename.EndsWith(".jpeg") || attachment.Filename.EndsWith(".gif"))
                {
                    if (imageUrls.Count < 3) // Collect up to 3 image URLs
                    {
                        imageUrls.Add(attachment.Url);
                    }
                }
                else
                {
                    nonImageAttachmentUrls.Add(attachment.Url);
                }
            }

            var embed = new EmbedBuilder
            {
                Title = "<a:Megaphone:1218248132954030141> Mensaje privado del propietario del bot",
                Description = $"### Mensaje:\n{message}",
                Color = Color.Gold,
                Timestamp = DateTimeOffset.Now,
                ThumbnailUrl = "https://raw.githubusercontent.com/bdawg1989/sprites/main/pikamail.png"
            };

            // Set the first image as the main embed image if available
            if (imageUrls.Any())
            {
                embed.ImageUrl = imageUrls[0];
            }

            // Add up to two more images as fields with clickable links
            for (int i = 1; i < imageUrls.Count; i++)
            {
                embed.AddField($"Imagen adicional {i}", $"[Ver imagen]({imageUrls[i]})");
            }

            // Add non-image attachments as download links
            foreach (var url in nonImageAttachmentUrls)
            {
                embed.AddField("Enlace de descarga", url);
            }

            try
            {
                var dmChannel = await user.CreateDMChannelAsync();

                await dmChannel.SendMessageAsync(embed: embed.Build());

                var confirmationMessage = await ReplyAsync($"<a:yes:1206485105674166292> Mensaje enviado exitosamente a **{user.Username}**.");
                await Context.Message.DeleteAsync();
                await Task.Delay(TimeSpan.FromSeconds(10));
                await confirmationMessage.DeleteAsync();
            }
            catch (Exception ex)
            {
                await ReplyAsync($"<a:warning:1206483664939126795> No se pudo enviar el mensaje a **{user.Username}**. Error: {ex.Message}");
            }
        }

        [Command("say")]
        [Summary("Sends a message to a specified channel.")]
        [RequireSudo]
        public async Task SayAsync([Remainder] string message)
        {
            var attachments = Context.Message.Attachments;
            var hasAttachments = attachments.Count != 0;

            var indexOfChannelMentionStart = message.LastIndexOf('<');
            var indexOfChannelMentionEnd = message.LastIndexOf('>');
            if (indexOfChannelMentionStart == -1 || indexOfChannelMentionEnd == -1)
            {
                await ReplyAsync($"<a:warning:1206483664939126795> {Context.User.Mention}, por favor mencione un canal correctamente usando #channel.");
                return;
            }

            var channelMention = message.Substring(indexOfChannelMentionStart, indexOfChannelMentionEnd - indexOfChannelMentionStart + 1);
            var actualMessage = message.Substring(0, indexOfChannelMentionStart).TrimEnd();

            var channel = Context.Guild.Channels.FirstOrDefault(c => $"<#{c.Id}>" == channelMention);

            if (channel == null)
            {
                await ReplyAsync("<a:no:1206485104424128593> Canal no encontrado.");
                return;
            }

            if (channel is not IMessageChannel messageChannel)
            {
                await ReplyAsync("<a:warning:1206483664939126795> El canal mencionado no es un canal de texto.");
                return;
            }

            // If there are attachments, send them to the channel
            if (hasAttachments)
            {
                foreach (var attachment in attachments)
                {
                    using var httpClient = new HttpClient();
                    var stream = await httpClient.GetStreamAsync(attachment.Url);
                    var file = new FileAttachment(stream, attachment.Filename);
                    await messageChannel.SendFileAsync(file, actualMessage);
                }
            }
            else
            {
                await messageChannel.SendMessageAsync(actualMessage);
            }

            // Send confirmation message to the user
            await ReplyAsync($"<a:yes:1206485105674166292> {Context.User.Mention}, mensaje publicado exitosamente en {channelMention}.");
        }

        private RemoteControlAccess GetReference(IUser channel) => new()
        {
            ID = channel.Id,
            Name = channel.Username,
            Comment = $"Añadido por {Context.User.Username} el {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
        };

        private RemoteControlAccess GetReference(IChannel channel) => new()
        {
            ID = channel.Id,
            Name = channel.Name,
            Comment = $"Añadido por {Context.User.Username} el {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
        };
    }
}