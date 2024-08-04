using Discord;
using Discord.Commands;
using Newtonsoft.Json.Linq;
using PKHeX.Core;
using SysBot.Pokemon.SV.BotRaid.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class BotModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        [Command("botStatus")]
        [Summary("Obtiene el estado de los bots.")]
        [RequireSudo]
        public async Task GetStatusAsync()
        {
            var me = SysCord<T>.Runner;
            var bots = me.Bots.Select(z => z.Bot).OfType<PokeRoutineExecutorBase>().ToArray();
            if (bots.Length == 0)
            {
                await ReplyAsync("No hay bots configurados.").ConfigureAwait(false);
                return;
            }

            var summaries = bots.Select(GetDetailedSummary);
            var lines = string.Join(Environment.NewLine, summaries);
            await ReplyAsync(Format.Code(lines)).ConfigureAwait(false);
        }

        private string GetBotIPFromJsonConfig()
        {
            try
            {
                // Leer el archivo y analizar el JSON
                var jsonData = File.ReadAllText(DaiRaidBot.ConfigPath);
                var config = JObject.Parse(jsonData);

                // Acceder a la dirección IP del primer bot en el array de Bots
                var ip = config["Bots"][0]["Connection"]["IP"].ToString();
                return ip;
            }
            catch (Exception ex)
            {
                // Manejar cualquier error que ocurra durante la lectura o análisis del archivo
                Console.WriteLine($"Error al leer el archivo de configuración: {ex.Message}");
                return "192.168.1.1"; // IP predeterminada si ocurre un error
            }
        }

        private static string GetDetailedSummary(PokeRoutineExecutorBase z)
        {
            return $"- {z.Connection.Name} | {z.Connection.Label} - {z.Config.CurrentRoutineType} ~ {z.LastTime:hh:mm:ss} | {z.LastLogged}";
        }

        [Command("botStart")]
        [Summary("Inicia el bot que se está ejecutando actualmente.")]
        [RequireSudo]
        public async Task StartBotAsync()
        {
            string ip = GetBotIPFromJsonConfig();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot con esa dirección IP ({ip}).").ConfigureAwait(false);
                return;
            }

            bot.Start();
            await ReplyAsync("El bot ha sido iniciado.").ConfigureAwait(false);
        }

        [Command("botStop")]
        [Summary("Detiene el bot que se está ejecutando actualmente.")]
        [RequireSudo]
        public async Task StopBotAsync()
        {
            string ip = GetBotIPFromJsonConfig();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot con esa dirección IP ({ip}).").ConfigureAwait(false);
                return;
            }

            bot.Stop();
            await ReplyAsync("El bot ha sido detenido.").ConfigureAwait(false);
        }

        [Command("botIdle")]
        [Alias("botPause")]
        [Summary("Ordena al bot que se está ejecutando actualmente que se ponga en modo inactivo.")]
        [RequireSudo]
        public async Task IdleBotAsync()
        {
            string ip = GetBotIPFromJsonConfig();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot con esa dirección IP ({ip}).").ConfigureAwait(false);
                return;
            }

            bot.Pause();
            await ReplyAsync("El bot se ha puesto en modo inactivo.").ConfigureAwait(false);
        }

        [Command("botChange")]
        [Summary("Cambia la rutina del bot que se está ejecutando actualmente (intercambios).")]
        [RequireSudo]
        public async Task ChangeTaskAsync([Summary("Nombre del enum de la rutina")] PokeRoutineType task)
        {
            string ip = GetBotIPFromJsonConfig();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot con esa dirección IP ({ip}).").ConfigureAwait(false);
                return;
            }

            bot.Bot.Config.Initialize(task);
            await ReplyAsync($"El bot ha cambiado su rutina a {task}.").ConfigureAwait(false);
        }

        [Command("botRestart")]
        [Summary("Reinicia el(los) bot(s) que se están ejecutando actualmente.")]
        [RequireSudo]
        public async Task RestartBotAsync()
        {
            string ip = GetBotIPFromJsonConfig();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No hay ningún bot con esa dirección IP ({ip}).").ConfigureAwait(false);
                return;
            }

            var c = bot.Bot.Connection;
            c.Reset();
            bot.Start();
            await ReplyAsync("El bot ha sido reiniciado.").ConfigureAwait(false);
        }
    }
}