using System.ComponentModel;
using static SysBot.Pokemon.RotatingRaidSettingsSV;

namespace SysBot.Pokemon
{
    public class DiscordSettings
    {
        private const string Startup = nameof(Startup);
        private const string Operation = nameof(Operation);
        private const string Channels = nameof(Channels);
        private const string Roles = nameof(Roles);
        private const string Users = nameof(Users);

        public override string ToString() => "Configuración de integración de Discord";

        // Startup

        [Category(Startup), Description("Token de inicio de sesión del bot.")]
        public string Token { get; set; } = string.Empty;

        [Category(Startup), Description("Prefijo de comando de bot.")]
        public string CommandPrefix { get; set; } = "$";

        [Category(Startup), Description("Alternar para manejar comandos de forma asincrónica o sincrónica.")]
        public bool AsyncCommands { get; set; }

        [Category(Startup), Description("Estado personalizado para jugar un juego.")]
        public string BotGameStatus { get; set; } = "Hosteando Incurciones de S/V";

        [Category(Operation), Description("Mensaje personalizado con el que el bot responderá cuando un usuario le diga hola. Utiliza el formato de cadena para mencionar al usuario en la respuesta.")]
        public string HelloResponse { get; set; } = "Hola, {0}!  Estoy online!";

        // Whitelists
        [Category(Roles), Description("Los usuarios con este rol pueden entrar en la cola de Raid.")]
        public RemoteControlAccessList RoleRaidRequest { get; set; } = new() { AllowIfEmpty = false };

        [Browsable(false)]
        [Category(Roles), Description("Los usuarios con este rol pueden controlar remotamente la consola (si se ejecuta como Remote Control Bot).")]
        public RemoteControlAccessList RoleRemoteControl { get; set; } = new() { AllowIfEmpty = false };

        [Category(Roles), Description("Los usuarios con este rol pueden saltarse las restricciones de comandos.")]
        public RemoteControlAccessList RoleSudo { get; set; } = new() { AllowIfEmpty = false };

        // Operation
        [Category(Users), Description("Los usuarios con estos ID de usuario no pueden utilizar el bot.")]
        public RemoteControlAccessList UserBlacklist { get; set; } = new();

        [Category(Channels), Description("Los canales con estos IDs son los únicos canales en los que el bot reconoce los comandos.")]
        public RemoteControlAccessList ChannelWhitelist { get; set; } = new();

        [Category(Users), Description("IDs de usuario de Discord separados por comas que tendrán acceso sudo al Bot Hub.")]
        public RemoteControlAccessList GlobalSudoList { get; set; } = new();

        [Category(Users), Description("Deshabilitar esto eliminará el soporte global de sudo.")]
        public bool AllowGlobalSudo { get; set; } = true;

        [Category(Channels), Description("IDs de canal que se harán eco de los datos del bot de registro.")]
        public RemoteControlAccessList LoggingChannels { get; set; } = new();

        [Category(Channels), Description("Canales de Embeds de Raid.")]
        public RemoteControlAccessList EchoChannels { get; set; } = new();

        public AnnouncementSettingsCategory AnnouncementSettings { get; set; } = new();

        [Category(Operation), TypeConverter(typeof(CategoryConverter<AnnouncementSettingsCategory>))]
        public class AnnouncementSettingsCategory
        {
            public override string ToString() => "Configuración de anuncios";

            [Category("Embed Settings"), Description("Opción de miniaturas para los anuncios.")]
            public ThumbnailOption AnnouncementThumbnailOption { get; set; } = ThumbnailOption.Gengar;

            [Category("Embed Settings"), Description("URL en miniatura personalizada para anuncios.")]
            public string CustomAnnouncementThumbnailUrl { get; set; } = string.Empty;

            public EmbedColorOption AnnouncementEmbedColor { get; set; } = EmbedColorOption.Blue;

            [Category("Embed Settings"), Description("Activar la selección aleatoria de miniaturas para los anuncios.")]
            public bool RandomAnnouncementThumbnail { get; set; } = false;

            [Category("Embed Settings"), Description("Activar la selección aleatoria de colores para los anuncios.")]
            public bool RandomAnnouncementColor { get; set; } = false;
        }
    }
}