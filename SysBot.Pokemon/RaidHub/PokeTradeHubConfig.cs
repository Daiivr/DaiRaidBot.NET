using System.ComponentModel;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SysBot.Pokemon
{
    public sealed class PokeRaidHubConfig : BaseConfig
    {
        private const string BotRaid = nameof(BotRaid);
        private const string Integration = nameof(Integration);

        [Category(Operation), Description("Añade tiempo extra para cambios más lentos.")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public TimingSettings Timings { get; set; } = new();

        [Category(BotRaid), Description("Nombre del bot de Discord que está ejecutando el programa. Esto le dará título a la ventana para que sea más fácil reconocerlo. Requiere reiniciar el programa.")]
        [DisplayName("El nombre de este bot es...")]
        public string BotName { get; set; } = string.Empty;

        [Browsable(false)]
        [Category(Integration), Description("Elección de opción de tema de los usuarios.")]
        public string ThemeOption { get; set; } = string.Empty;

        [Category(BotRaid)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public RotatingRaidSettingsSV RotatingRaidSV { get; set; } = new();

        // Integration

        [Category(Integration)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public DiscordSettings Discord { get; set; } = new();
    }
}