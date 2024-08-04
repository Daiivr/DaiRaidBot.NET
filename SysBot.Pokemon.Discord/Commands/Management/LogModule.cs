using Discord;
using Discord.Commands;
using Discord.WebSocket;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class LogModule : ModuleBase<SocketCommandContext>
    {
        private class LogAction : ChannelAction<string, string>
        {
            public LogAction(ulong id, Action<string, string> messager, string channel) : base(id, messager, channel)
            {
            }
        }

        private static readonly Dictionary<ulong, LogAction> Channels = new();

        public static void RestoreLogging(DiscordSocketClient discord, DiscordSettings settings)
        {
            foreach (var ch in settings.LoggingChannels)
            {
                if (discord.GetChannel(ch.ID) is ISocketMessageChannel c)
                    AddLogChannel(c, ch.ID);
            }

            LogUtil.LogInfo("Se añadió el registro a los canales de Discord al inicio del bot.", "Discord");
        }

        [Command("logHere")]
        [Summary("Hace que el bot registre en el canal.")]
        [RequireSudo]
        public async Task AddLogAsync()
        {
            var c = Context.Channel;
            var cid = c.Id;
            if (Channels.TryGetValue(cid, out _))
            {
                await ReplyAsync("Ya se está registrando aquí.").ConfigureAwait(false);
                return;
            }

            AddLogChannel(c, cid);

            // Añadir a los registradores globales de Discord (se guarda al cerrar el programa)
            SysCordSettings.Settings.LoggingChannels.AddIfNew(new[] { GetReference(Context.Channel) });
            await ReplyAsync("¡Se añadió la salida de registro a este canal!").ConfigureAwait(false);
        }

        private static void AddLogChannel(ISocketMessageChannel c, ulong cid)
        {
            void Logger(string msg, string identity)
            {
                try
                {
                    c.SendMessageAsync(GetMessage(msg, identity));
                }
                catch (Exception ex)
                {
                    LogUtil.LogSafe(ex, identity);
                }
            }

            Action<string, string> l = Logger;
            LogUtil.Forwarders.Add(l);
            static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";

            var entry = new LogAction(cid, l, c.Name);
            Channels.Add(cid, entry);
        }

        [Command("logInfo")]
        [Summary("Vuelca la configuración de registro.")]
        [RequireSudo]
        public async Task DumpLogInfoAsync()
        {
            foreach (var c in Channels)
                await ReplyAsync($"{c.Key} - {c.Value}").ConfigureAwait(false);
        }

        [Command("logClear")]
        [Summary("Borra la configuración de registro en ese canal específico.")]
        [RequireSudo]
        public async Task ClearLogsAsync()
        {
            var id = Context.Channel.Id;
            if (!Channels.TryGetValue(id, out var log))
            {
                await ReplyAsync("No se está haciendo eco en este canal.").ConfigureAwait(false);
                return;
            }
            LogUtil.Forwarders.Remove(log.Action);
            Channels.Remove(Context.Channel.Id);
            SysCordSettings.Settings.LoggingChannels.RemoveAll(z => z.ID == id);
            await ReplyAsync($"Registro borrado del canal: {Context.Channel.Name}").ConfigureAwait(false);
        }

        [Command("logClearAll")]
        [Summary("Borra toda la configuración de registro.")]
        [RequireSudo]
        public async Task ClearLogsAllAsync()
        {
            foreach (var l in Channels)
            {
                var entry = l.Value;
                await ReplyAsync($"Registro borrado de {entry.ChannelName} ({entry.ChannelID})!").ConfigureAwait(false);
                LogUtil.Forwarders.Remove(entry.Action);
            }

            LogUtil.Forwarders.RemoveAll(y => Channels.Select(x => x.Value.Action).Contains(y));
            Channels.Clear();
            SysCordSettings.Settings.LoggingChannels.Clear();
            await ReplyAsync("¡Registro borrado de todos los canales!").ConfigureAwait(false);
        }

        private RemoteControlAccess GetReference(IChannel channel) => new()
        {
            ID = channel.Id,
            Name = channel.Name,
            Comment = $"Añadido por {Context.User.Username} el {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
        };
    }
}