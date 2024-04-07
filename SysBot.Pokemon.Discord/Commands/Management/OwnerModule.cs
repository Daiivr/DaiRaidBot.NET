using Discord;
using Discord.Commands;
using PKHeX.Core;
using System;
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
            await ReplyAsync("Done.").ConfigureAwait(false);
        }

        [Command("removeSudo")]
        [Summary("Removes mentioned user from global sudo")]
        [RequireOwner]
        public async Task RemoveSudoUsers([Remainder] string _)
        {
            var users = Context.Message.MentionedUsers;
            var objects = users.Select(GetReference);
            SysCordSettings.Settings.GlobalSudoList.RemoveAll(z => objects.Any(o => o.ID == z.ID));
            await ReplyAsync("Done.").ConfigureAwait(false);
        }

        [Command("addChannel")]
        [Summary("Adds a channel to the list of channels that are accepting commands.")]
        [RequireOwner]
        public async Task AddChannel()
        {
            var obj = GetReference(Context.Message.Channel);
            SysCordSettings.Settings.ChannelWhitelist.AddIfNew(new[] { obj });
            await ReplyAsync("Done.").ConfigureAwait(false);
        }

        [Command("removeChannel")]
        [Summary("Removes a channel from the list of channels that are accepting commands.")]
        [RequireOwner]
        public async Task RemoveChannel()
        {
            var obj = GetReference(Context.Message.Channel);
            SysCordSettings.Settings.ChannelWhitelist.RemoveAll(z => z.ID == obj.ID);
            await ReplyAsync("Done.").ConfigureAwait(false);
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
                await ReplyAsync("Please provide a valid Guild ID.").ConfigureAwait(false);
                return;
            }
            var guild = Context.Client.Guilds.FirstOrDefault(x => x.Id == id);
            if (guild is null)
            {
                await ReplyAsync($"Provided input ({userInput}) is not a valid guild ID or the bot is not in the specified guild.").ConfigureAwait(false);
                return;
            }

            await ReplyAsync($"Leaving {guild}.").ConfigureAwait(false);
            await guild.LeaveAsync().ConfigureAwait(false);
        }

        [Command("leaveall")]
        [Summary("Leaves all servers the bot is currently in.")]
        [RequireOwner]
        public async Task LeaveAll()
        {
            await ReplyAsync("Leaving all servers.").ConfigureAwait(false);
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
            await Util.ListUtil(Context, "Here is a list of all servers this bot is currently in", guildList.ToString()).ConfigureAwait(false);
        }

        [Command("sudoku")]
        [Alias("kill", "shutdown")]
        [Summary("Causes the entire process to end itself!")]
        [RequireOwner]
        public async Task ExitProgram()
        {
            await Context.Channel.EchoAndReply("Shutting down... goodbye! **Bot services are going offline.**").ConfigureAwait(false);
            Environment.Exit(0);
        }

        [Command("say")]
        [Summary("Sends a message to a specified channel.")]
        [RequireSudo]
        public async Task SayAsync([Remainder] string message)
        {
            var attachments = Context.Message.Attachments;
            var hasAttachments = attachments.Any();

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

            if (!(channel is IMessageChannel messageChannel))
            {
                await ReplyAsync("<a:warning:1206483664939126795> El canal mencionado no es un canal de texto.");
                return;
            }

            // If there are attachments, send them to the channel
            if (hasAttachments)
            {
                foreach (var attachment in attachments)
                {
                    using (var httpClient = new HttpClient())
                    {
                        var stream = await httpClient.GetStreamAsync(attachment.Url);
                        var file = new FileAttachment(stream, attachment.Filename);
                        await messageChannel.SendFileAsync(file, actualMessage);
                    }
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
            Comment = $"Added by {Context.User.Username} on {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
        };

        private RemoteControlAccess GetReference(IChannel channel) => new()
        {
            ID = channel.Id,
            Name = channel.Name,
            Comment = $"Added by {Context.User.Username} on {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
        };
    }
}
