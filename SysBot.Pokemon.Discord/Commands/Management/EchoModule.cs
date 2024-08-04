using Discord;
using Discord.Commands;
using Discord.WebSocket;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public static class EmbedColorConverter
    {
        public static Color ToDiscordColor(this EmbedColorOption colorOption)
        {
            return colorOption switch
            {
                EmbedColorOption.Blue => Color.Blue,
                EmbedColorOption.Green => Color.Green,
                EmbedColorOption.Red => Color.Red,
                EmbedColorOption.Gold => Color.Gold,
                EmbedColorOption.Purple => Color.Purple,
                EmbedColorOption.Teal => Color.Teal,
                EmbedColorOption.Orange => Color.Orange,
                EmbedColorOption.Magenta => Color.Magenta,
                EmbedColorOption.LightGrey => Color.LightGrey,
                EmbedColorOption.DarkGrey => Color.DarkGrey,
                _ => Color.Blue,  // Por defecto, Azul si se usa un valor de enumeración indefinido
            };
        }
    }

    public class EchoModule : ModuleBase<SocketCommandContext>
    {
        private static DiscordSettings Settings { get; set; }

        private class EchoChannel
        {
            public readonly ulong ChannelID;
            public readonly string ChannelName;
            public readonly Action<string> Action;
            public readonly Action<byte[], string, EmbedBuilder> RaidAction;
            public string EmbedResult = string.Empty;

            public EchoChannel(ulong channelId, string channelName, Action<string> action, Action<byte[], string, EmbedBuilder> raidAction)
            {
                ChannelID = channelId;
                ChannelName = channelName;
                Action = action;
                RaidAction = raidAction;
            }
        }

        private class EncounterEchoChannel
        {
            public readonly ulong ChannelID;
            public readonly string ChannelName;
            public readonly Action<string, Embed> EmbedAction;
            public string EmbedResult = string.Empty;

            public EncounterEchoChannel(ulong channelId, string channelName, Action<string, Embed> embedaction)
            {
                ChannelID = channelId;
                ChannelName = channelName;
                EmbedAction = embedaction;
            }
        }

        private static readonly Dictionary<ulong, EchoChannel> Channels = new();
        private static readonly Dictionary<ulong, EncounterEchoChannel> EncounterChannels = new();

        public static void RestoreChannels(DiscordSocketClient discord, DiscordSettings cfg)
        {
            Settings = cfg;
            foreach (var ch in cfg.EchoChannels)
            {
                if (discord.GetChannel(ch.ID) is ISocketMessageChannel c)
                    AddEchoChannel(c, ch.ID);
            }
            // EchoUtil.Echo("Se añadieron notificaciones de eco a los canales de Discord en el inicio del Bot.");
        }

        [Command("Announce", RunMode = RunMode.Async)]
        [Alias("announce")]
        [Summary("Envía un anuncio a todos los EchoChannels agregados por el comando aec.")]
        [RequireOwner]
        public async Task AnnounceAsync([Remainder] string announcement)
        {
            var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var formattedTimestamp = $"<t:{unixTimestamp}:F>";
            var embedColor = Settings.AnnouncementSettings.RandomAnnouncementColor ? GetRandomColor() : Settings.AnnouncementSettings.AnnouncementEmbedColor.ToDiscordColor();
            var thumbnailUrl = Settings.AnnouncementSettings.RandomAnnouncementThumbnail ? GetRandomThumbnail() : GetSelectedThumbnail();

            var embedDescription = $"## {announcement}\n\n**Enviado: {formattedTimestamp}**";

            var embed = new EmbedBuilder
            {
                Color = embedColor,
                Description = embedDescription
            }
            .WithTitle("¡Anuncio Importante!")
            .WithThumbnailUrl(thumbnailUrl)
            .Build();

            var client = Context.Client;
            foreach (var channelEntry in Channels)
            {
                var channelId = channelEntry.Key;
                var channel = client.GetChannel(channelId) as ISocketMessageChannel;
                if (channel == null)
                {
                    LogUtil.LogError($"No se pudo encontrar o acceder al canal {channelId}", nameof(AnnounceAsync));
                    continue;
                }

                try
                {
                    await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"No se pudo enviar el anuncio al canal {channel.Name}: {ex.Message}", nameof(AnnounceAsync));
                }
            }
            var confirmationMessage = await ReplyAsync("Anuncio enviado a todos los EchoChannels.").ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await confirmationMessage.DeleteAsync().ConfigureAwait(false);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
        }

        private Color GetRandomColor()
        {
            // Generar un color aleatorio
            var random = new Random();
            var colors = Enum.GetValues(typeof(EmbedColorOption)).Cast<EmbedColorOption>().ToList();
            return colors[random.Next(colors.Count)].ToDiscordColor();
        }

        private string GetRandomThumbnail()
        {
            // Definir una lista de URLs de miniaturas disponibles
            var thumbnailOptions = new List<string>
            {
                "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/gengarmegaphone.png",
                "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/pikachumegaphone.png",
                "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/umbreonmegaphone.png",
                "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/sylveonmegaphone.png",
                "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/charmandermegaphone.png",
                "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/jigglypuffmegaphone.png",
                "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/flareonmegaphone.png",
            };

            // Generar un índice aleatorio y devolver la URL correspondiente
            var random = new Random();
            return thumbnailOptions[random.Next(thumbnailOptions.Count)];
        }

        private string GetSelectedThumbnail()
        {
            // Usar la URL de miniatura personalizada si no está vacía; de lo contrario, usar la opción seleccionada
            if (!string.IsNullOrEmpty(Settings.AnnouncementSettings.CustomAnnouncementThumbnailUrl))
            {
                return Settings.AnnouncementSettings.CustomAnnouncementThumbnailUrl;
            }
            else
            {
                return GetUrlFromThumbnailOption(Settings.AnnouncementSettings.AnnouncementThumbnailOption);
            }
        }

        private string GetUrlFromThumbnailOption(ThumbnailOption option)
        {
            switch (option)
            {
                case ThumbnailOption.Gengar:
                    return "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/gengarmegaphone.png";

                case ThumbnailOption.Pikachu:
                    return "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/pikachumegaphone.png";

                case ThumbnailOption.Umbreon:
                    return "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/umbreonmegaphone.png";

                case ThumbnailOption.Sylveon:
                    return "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/sylveonmegaphone.png";

                case ThumbnailOption.Charmander:
                    return "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/charmandermegaphone.png";

                case ThumbnailOption.Jigglypuff:
                    return "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/jigglypuffmegaphone.png";

                case ThumbnailOption.Flareon:
                    return "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/flareonmegaphone.png";

                default:
                    return "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/gengarmegaphone.png";
            }
        }

        [Command("addEmbedChannel")]
        [Alias("aec")]
        [Summary("Hace que el bot publique embeds de incursiones en el canal.")]
        [RequireSudo]
        public async Task AddEchoAsync()
        {
            var c = Context.Channel;
            var cid = c.Id;
            if (Channels.TryGetValue(cid, out _))
            {
                await ReplyAsync("Ya está notificando aquí.").ConfigureAwait(false);
                return;
            }

            AddEchoChannel(c, cid);

            // Añadir a los registradores globales de Discord (se guarda al cerrar el programa)
            SysCordSettings.Settings.EchoChannels.AddIfNew(new[] { GetReference(Context.Channel) });
            await ReplyAsync("¡Salida de embed de incursión añadida a este canal!").ConfigureAwait(false);
        }

        private static async Task<bool> SendMessageWithRetry(ISocketMessageChannel c, string message, int maxRetries = 3)
        {
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                try
                {
                    await c.SendMessageAsync(message).ConfigureAwait(false);
                    return true; // Mensaje enviado con éxito, salir del bucle.
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"No se pudo enviar el mensaje al canal '{c.Name}' (Intento {retryCount + 1}): {ex.Message}", nameof(AddEchoChannel));
                    retryCount++;
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false); // Esperar 5 segundos antes de reintentar.
                }
            }
            return false; // Se alcanzó el número máximo de intentos sin éxito.
        }

        private static async Task<bool> RaidEmbedAsync(ISocketMessageChannel c, byte[] bytes, string fileName, EmbedBuilder embed, int maxRetries = 2)
        {
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                try
                {
                    if (bytes is not null && bytes.Length > 0)
                    {
                        await c.SendFileAsync(new MemoryStream(bytes), fileName, "", false, embed: embed.Build()).ConfigureAwait(false);
                    }
                    else
                    {
                        await c.SendMessageAsync("", false, embed.Build()).ConfigureAwait(false);
                    }
                    return true; // Mensaje enviado con éxito, salir del bucle.
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"No se pudo enviar el embed al canal '{c.Name}' (Intento {retryCount + 1}): {ex.Message}", nameof(AddEchoChannel));
                    retryCount++;
                    if (retryCount < maxRetries)
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false); // Esperar un segundo antes de reintentar.
                }
            }
            return false; // Se alcanzó el número máximo de intentos sin éxito.
        }

        private static void AddEchoChannel(ISocketMessageChannel c, ulong cid)
        {
            Action<string> l = async (msg) => await SendMessageWithRetry(c, msg).ConfigureAwait(false);
            Action<byte[], string, EmbedBuilder> rb = async (bytes, fileName, embed) => await RaidEmbedAsync(c, bytes, fileName, embed).ConfigureAwait(false);

            EchoUtil.Forwarders.Add(l);
            EchoUtil.RaidForwarders.Add(rb);
            var entry = new EchoChannel(cid, c.Name, l, rb);
            Channels.Add(cid, entry);
        }

        public static bool IsEchoChannel(ISocketMessageChannel c)
        {
            var cid = c.Id;
            return Channels.TryGetValue(cid, out _);
        }

        public static bool IsEmbedEchoChannel(ISocketMessageChannel c)
        {
            var cid = c.Id;
            return EncounterChannels.TryGetValue(cid, out _);
        }

        [Command("echoInfo")]
        [Summary("Vuelca la configuración de mensajes especiales (Echo).")]
        [RequireSudo]
        public async Task DumpEchoInfoAsync()
        {
            foreach (var c in Channels)
                await ReplyAsync($"{c.Key} - {c.Value}").ConfigureAwait(false);
        }

        [Command("echoClear")]
        [Alias("rec")]
        [Summary("Borra la configuración de mensajes especiales (Echo) en ese canal específico.")]
        [RequireSudo]
        public async Task ClearEchosAsync()
        {
            var id = Context.Channel.Id;
            if (!Channels.TryGetValue(id, out var echo))
            {
                await ReplyAsync("No se está haciendo eco en este canal.").ConfigureAwait(false);
                return;
            }
            EchoUtil.Forwarders.Remove(echo.Action);
            EchoUtil.RaidForwarders.Remove(echo.RaidAction);
            Channels.Remove(Context.Channel.Id);
            SysCordSettings.Settings.EchoChannels.RemoveAll(z => z.ID == id);
            await ReplyAsync($"Ecos eliminados del canal: {Context.Channel.Name}").ConfigureAwait(false);
        }

        [Command("echoClearAll")]
        [Alias("raec")]
        [Summary("Borra toda la configuración de mensajes especiales (Echo) en todos los canales.")]
        [RequireSudo]
        public async Task ClearEchosAllAsync()
        {
            foreach (var l in Channels)
            {
                var entry = l.Value;
                await ReplyAsync($"Eco eliminado de {entry.ChannelName} ({entry.ChannelID})!").ConfigureAwait(false);
                EchoUtil.Forwarders.Remove(entry.Action);
            }
            EchoUtil.Forwarders.RemoveAll(y => Channels.Select(x => x.Value.Action).Contains(y));
            EchoUtil.RaidForwarders.RemoveAll(y => Channels.Select(x => x.Value.RaidAction).Contains(y));
            Channels.Clear();
            SysCordSettings.Settings.EchoChannels.Clear();
            await ReplyAsync("Ecos eliminados de todos los canales!").ConfigureAwait(false);
        }

        private RemoteControlAccess GetReference(IChannel channel) => new()
        {
            ID = channel.Id,
            Name = channel.Name,
            Comment = $"Añadido por {Context.User.Username} el {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
        };
    }
}