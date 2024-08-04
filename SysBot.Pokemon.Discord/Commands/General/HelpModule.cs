using Discord;
using Discord.Commands;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class HelpModule : ModuleBase<SocketCommandContext>
    {
        private readonly CommandService _service;

        public HelpModule(CommandService service)
        {
            _service = service;
        }

        [Command("help")]
        [Summary("Enumera los comandos disponibles.")]
        public async Task HelpAsync()
        {
            List<Embed> embeds = new();
            var builder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Description = "Estos son los comandos que puedes usar:",
            };

            var mgr = SysCordSettings.Manager;
            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            var owner = app.Owner.Id;
            var uid = Context.User.Id;

            foreach (var module in _service.Modules)
            {
                string? description = null;
                HashSet<string> mentioned = new();
                foreach (var cmd in module.Commands)
                {
                    var name = cmd.Name;
                    if (mentioned.Contains(name))
                        continue;
                    if (cmd.Attributes.Any(z => z is RequireOwnerAttribute) && owner != uid)
                        continue;
                    if (cmd.Attributes.Any(z => z is RequireSudoAttribute) && !mgr.CanUseSudo(uid))
                        continue;

                    mentioned.Add(name);
                    var result = await cmd.CheckPreconditionsAsync(Context).ConfigureAwait(false);
                    if (result.IsSuccess)
                        description += $"{cmd.Aliases[0]}\n";
                }
                if (string.IsNullOrWhiteSpace(description))
                    continue;

                var moduleName = module.Name;
                var gen = moduleName.IndexOf('`');
                if (gen != -1)
                    moduleName = moduleName[..gen];

                if (builder.Fields.Count == 25)
                {
                    embeds.Add(builder.Build());
                    builder.Fields.Clear();
                    builder.Description = string.Empty;
                }

                builder.AddField(x =>
                {
                    x.Name = moduleName;
                    x.Value = description;
                    x.IsInline = false;
                });
            }

            if (builder.Fields.Count > 0)
                embeds.Add(builder.Build());

            await ReplyAsync("¡La ayuda ha llegado!", false, null, null, null, null, null, null, embeds.ToArray()).ConfigureAwait(false);
        }

        [Command("help")]
        [Summary("Enumera información sobre un comando específico.")]
        public async Task HelpAsync([Summary("El comando para el que deseas ayuda")] string command)
        {
            var result = _service.Search(Context, command);

            if (!result.IsSuccess)
            {
                await ReplyAsync($"Lo siento, no pude encontrar un comando como **{command}**.").ConfigureAwait(false);
                return;
            }

            var builder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Description = $"Aquí hay algunos comandos como **{command}**:",
            };

            foreach (var match in result.Commands)
            {
                var cmd = match.Command;

                builder.AddField(x =>
                {
                    x.Name = string.Join(", ", cmd.Aliases);
                    x.Value = GetCommandSummary(cmd);
                    x.IsInline = false;
                });
            }

            await ReplyAsync("¡La ayuda ha llegado!", false, builder.Build()).ConfigureAwait(false);
        }

        private static string GetCommandSummary(CommandInfo cmd)
        {
            return $"Resumen: {cmd.Summary}\nParámetros: {GetParameterSummary(cmd.Parameters)}";
        }

        private static string GetParameterSummary(IReadOnlyList<ParameterInfo> p)
        {
            if (p.Count == 0)
                return "Ninguno";
            return $"{p.Count}\n- " + string.Join("\n- ", p.Select(GetParameterSummary));
        }

        private static string GetParameterSummary(ParameterInfo z)
        {
            var result = z.Name;
            if (!string.IsNullOrWhiteSpace(z.Summary))
                result += $" ({z.Summary})";
            return result;
        }
    }
}