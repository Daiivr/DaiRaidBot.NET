using Discord;
using Discord.Commands;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class HubModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        [Command("status")]
        [Alias("stats")]
        [Summary("Obtiene el estado del entorno del bot.")]
        public async Task GetStatusAsync()
        {
            var me = SysCord<T>.Runner;
            var hub = me.Hub;

            var builder = new EmbedBuilder
            {
                Color = Color.Gold,
            };

            var runner = SysCord<T>.Runner;
            var allBots = runner.Bots.ConvertAll(z => z.Bot);
            var botCount = allBots.Count;
            builder.AddField(x =>
            {
                x.Name = "Resumen";
                x.Value =
                    $"Cantidad de Bots: {botCount}\n" +
                    $"Estado de los Bots: {SummarizeBots(allBots)}\n";
                x.IsInline = false;
            });

            await ReplyAsync("Estado del Bot", false, builder.Build()).ConfigureAwait(false);
        }

        private static string SummarizeBots(IReadOnlyCollection<RoutineExecutor<PokeBotState>> bots)
        {
            if (bots.Count == 0)
                return "No hay bots configurados.";
            var summaries = bots.Select(z => $"- {z.GetSummary()}");
            return Environment.NewLine + string.Join(Environment.NewLine, summaries);
        }
    }
}