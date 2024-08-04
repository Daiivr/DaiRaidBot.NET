using Discord;
using Discord.Commands;
using SysBot.Pokemon.SV.BotRaid.Helpers;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class InfoModule : ModuleBase<SocketCommandContext>
    {
        private const string detail = "Soy un Bot de Incursiones de Código Abierto impulsado por PKHeX.Core y otros programas de código abierto.";
        public const string version = DaiRaidBot.Version;
        private const string support = DaiRaidBot.Repo;
        private const ulong DisallowedUserId = 195756980873199618;

        [Command("info")]
        [Alias("about", "whoami", "owner")]
        public async Task InfoAsync()
        {
            if (Context.User.Id == DisallowedUserId)
            {
                await ReplyAsync("No permitimos que personas sospechosas usen este comando.").ConfigureAwait(false);
                return;
            }
            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            var programIconUrl = "https://i.imgur.com/MdimOfV.png";
            var builder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Description = detail,
                ImageUrl = programIconUrl
            };

            builder.AddField("# __Información del Bot__",
                $"- **Versión**: {version}\n" +
                $"- [Descargar DaiRaidBot]({support})\n" +
                $"- {Format.Bold("Propietario")}: {app.Owner} ({app.Owner.Id})\n" +
                $"- {Format.Bold("Tiempo de Actividad")}: {GetUptime()}\n" +
                $"- {Format.Bold("Versión del PKHeX")}: {GetVersionInfo("PKHeX.Core")}\n" +
                $"- {Format.Bold("Versión de AutoLegality")}: {GetVersionInfo("PKHeX.Core.AutoMod")}\n"
                );

            builder.AddField("Estadísticas",
                $"- {Format.Bold("Servidores")}: {Context.Client.Guilds.Count}\n" +
                $"- {Format.Bold("Canales")}: {Context.Client.Guilds.Sum(g => g.Channels.Count)}\n" +
                $"- {Format.Bold("Usuarios")}: {Context.Client.Guilds.Sum(g => g.MemberCount)}"
                );
            builder.WithThumbnailUrl("https://i.imgur.com/jfG4V11.png");
            await ReplyAsync("¡Aquí tienes un poco de información sobre mí!", embed: builder.Build()).ConfigureAwait(false);
        }

        private static string GetUptime() => (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"dd\.hh\:mm\:ss");

        private static string GetVersionInfo(string assemblyName)
        {
            const string _default = "Desconocido";
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var assembly = assemblies.FirstOrDefault(x => x.GetName().Name == assemblyName);
            if (assembly is null)
                return _default;

            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute is null)
                return _default;

            var info = attribute.InformationalVersion;
            var split = info.Split('+');
            if (split.Length >= 2)
            {
                var versionParts = split[0].Split('.');
                if (versionParts.Length == 3)
                {
                    var major = versionParts[0].PadLeft(2, '0');
                    var minor = versionParts[1].PadLeft(2, '0');
                    var patch = versionParts[2].PadLeft(2, '0');
                    return $"{major}.{minor}.{patch}";
                }
            }
            return _default;
        }
    }
}