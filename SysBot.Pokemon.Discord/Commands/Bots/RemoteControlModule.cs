using Discord.Commands;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    [Summary("Controla remotamente un bot.")]
    public class RemoteControlModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        [Command("click")]
        [Summary("Hace clic en el botón especificado.")]
        [RequireRoleAccess(nameof(DiscordManager.RolesRemoteControl))]
        public async Task ClickAsync(SwitchButton b)
        {
            var bot = SysCord<T>.Runner.Bots.Find(z => IsRemoteControlBot(z.Bot));
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot disponible para ejecutar tu comando: {b}").ConfigureAwait(false);
                return;
            }

            await ClickAsyncImpl(b, bot).ConfigureAwait(false);
        }

        [Command("click")]
        [Summary("Hace clic en el botón especificado.")]
        [RequireSudo]
        public async Task ClickAsync(string ip, SwitchButton b)
        {
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot disponible para ejecutar tu comando: {b}").ConfigureAwait(false);
                return;
            }

            await ClickAsyncImpl(b, bot).ConfigureAwait(false);
        }

        [Command("setStick")]
        [Summary("Establece la posición del stick especificado.")]
        [RequireRoleAccess(nameof(DiscordManager.RolesRemoteControl))]
        public async Task SetStickAsync(SwitchStick s, short x, short y, ushort ms = 1_000)
        {
            var bot = SysCord<T>.Runner.Bots.Find(z => IsRemoteControlBot(z.Bot));
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot disponible para ejecutar tu comando: {s}").ConfigureAwait(false);
                return;
            }

            await SetStickAsyncImpl(s, x, y, ms, bot).ConfigureAwait(false);
        }

        [Command("setStick")]
        [Summary("Establece la posición del stick especificado.")]
        [RequireSudo]
        public async Task SetStickAsync(string ip, SwitchStick s, short x, short y, ushort ms = 1_000)
        {
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot con esa dirección IP ({ip}).").ConfigureAwait(false);
                return;
            }

            await SetStickAsyncImpl(s, x, y, ms, bot).ConfigureAwait(false);
        }

        private string GetRunningBotIP()
        {
            var r = SysCord<T>.Runner;
            var runningBot = r.Bots.Find(x => x.IsRunning);

            // Comprobar si se encuentra un bot en ejecución
            if (runningBot != null)
            {
                return runningBot.Bot.Config.Connection.IP;
            }
            else
            {
                // Dirección IP predeterminada o lógica si no se encuentra ningún bot en ejecución
                return "192.168.1.1";
            }
        }

        [Command("setScreenOn")]
        [Alias("screenOn", "scrOn")]
        [Summary("Enciende la pantalla")]
        [RequireSudo]
        public async Task SetScreenOnAsync()
        {
            await SetScreen(true).ConfigureAwait(false);
        }

        [Command("setScreenOff")]
        [Alias("screenOff", "scrOff")]
        [Summary("Apaga la pantalla")]
        [RequireSudo]
        public async Task SetScreenOffAsync()
        {
            await SetScreen(false).ConfigureAwait(false);
        }

        private async Task SetScreen(bool on)
        {
            string ip = GetRunningBotIP();
            var bot = GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot con esa dirección IP ({ip}).").ConfigureAwait(false);
                return;
            }

            var b = bot.Bot;
            var crlf = b is SwitchRoutineExecutor<PokeBotState> { UseCRLF: true };
            await b.Connection.SendAsync(SwitchCommand.SetScreen(on ? ScreenState.On : ScreenState.Off, crlf), CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync("Estado de la pantalla establecido a: " + (on ? "Encendida" : "Apagada")).ConfigureAwait(false);
        }

        private static BotSource<PokeBotState>? GetBot(string ip)
        {
            var r = SysCord<T>.Runner;
            return r.GetBot(ip) ?? r.Bots.Find(x => x.IsRunning); // fallback seguro para usuarios que escriben mal la dirección IP en instancias de un solo bot
        }

        private async Task ClickAsyncImpl(SwitchButton button, BotSource<PokeBotState> bot)
        {
            if (!Enum.IsDefined(typeof(SwitchButton), button))
            {
                await ReplyAsync($"Valor de botón desconocido: {button}").ConfigureAwait(false);
                return;
            }

            var b = bot.Bot;
            var crlf = b is SwitchRoutineExecutor<PokeBotState> { UseCRLF: true };
            await b.Connection.SendAsync(SwitchCommand.Click(button, crlf), CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"{b.Connection.Name} ha realizado: {button}").ConfigureAwait(false);
        }

        private async Task SetStickAsyncImpl(SwitchStick s, short x, short y, ushort ms, BotSource<PokeBotState> bot)
        {
            if (!Enum.IsDefined(typeof(SwitchStick), s))
            {
                await ReplyAsync($"Stick desconocido: {s}").ConfigureAwait(false);
                return;
            }

            var b = bot.Bot;
            var crlf = b is SwitchRoutineExecutor<PokeBotState> { UseCRLF: true };
            await b.Connection.SendAsync(SwitchCommand.SetStick(s, x, y, crlf), CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"{b.Connection.Name} ha realizado: {s}").ConfigureAwait(false);
            await Task.Delay(ms).ConfigureAwait(false);
            await b.Connection.SendAsync(SwitchCommand.ResetStick(s, crlf), CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"{b.Connection.Name} ha restablecido la posición del stick.").ConfigureAwait(false);
        }

        private bool IsRemoteControlBot(RoutineExecutor<PokeBotState> botstate)
            => botstate is RemoteControlBotSV;
    }
}