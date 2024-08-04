using System.ComponentModel;
using static SysBot.Pokemon.RotatingRaidSettingsSV;

namespace SysBot.Pokemon
{
    public class TimingSettings
    {
        private const string OpenGame = nameof(OpenGame);
        private const string CloseGame = nameof(CloseGame);
        private const string RestartGame = nameof(RestartGame);
        private const string Raid = nameof(Raid);
        private const string Misc = nameof(Misc);

        public override string ToString() => "Ajustes de tiempo extra";

        [Category(OpenGame), Description("Tiempo extra en milisegundos para esperar a que se cargue el mundo después de la pantalla de título.")]
        public int ExtraTimeLoadOverworld { get; set; } = 3000;

        [Category(OpenGame), Description("Tiempo extra en milisegundos para esperar después de que la semilla y el storyprogress sean inyectados antes de pulsar A.")]
        public int ExtraTimeInjectSeed { get; set; } = 0;

        [Category(CloseGame), Description("Tiempo extra en milisegundos a esperar después de pulsar HOME para minimizar el juego.")]
        public int ExtraTimeReturnHome { get; set; }

        [Category(Misc), Description("Tiempo extra en milisegundos para esperar a que el Poké Portal se cargue.")]
        public int ExtraTimeLoadPortal { get; set; } = 1000;

        [Category(Misc), Description("Tiempo extra en milisegundos que hay que esperar después de hacer clic en + para conectarse a Y-Comm (SWSH) o en L para conectarse en línea (SV).")]
        public int ExtraTimeConnectOnline { get; set; }

        [Category(Misc), Description("Número de veces que se intenta reconectar a una conexión de socket después de que se pierda una conexión. Establézcalo a -1 para intentarlo indefinidamente.")]
        public int ReconnectAttempts { get; set; } = 30;

        [Category(Misc), Description("Tiempo extra en milisegundos para esperar entre intentos de reconexión. El tiempo base es de 30 segundos.")]
        public int ExtraReconnectDelay { get; set; }

        [Category(Misc), Description("Tiempo de espera después de pulsar cada tecla al navegar por los menús de la switch o al introducir el código de enlace.")]
        public int KeypressTime { get; set; } = 200;

        [Category(RestartGame), Description("Ajustes relacionados con Reiniciar el juego.")]
        public RestartGameSettingsCategory RestartGameSettings { get; set; } = new();

        [Category(RestartGame), TypeConverter(typeof(CategoryConverter<RestartGameSettingsCategory>))]
        public class RestartGameSettingsCategory
        {
            public override string ToString() => "Ajustes de Reinicio del Juego";

            [Category(OpenGame), Description("Active esta opción para rechazar las actualizaciones entrantes del sistema.")]
            public bool AvoidSystemUpdate { get; set; } = false;

            [Category(OpenGame), Description("Active esta opción para añadir un retardo a la ventana emergente \"Comprobando si se puede jugar\".")]
            public bool CheckGameDelay { get; set; } = false;

            [Category(OpenGame), Description("Tiempo extra para esperar la ventana emergente \"Comprobando si se puede jugar\".")]
            public int ExtraTimeCheckGame { get; set; } = 200;

            [Category(OpenGame), Description("Habilítalo SÓLO cuando tengas DLC en el sistema y no puedas utilizarlo.")]
            public bool CheckForDLC { get; set; } = false;

            [Category(OpenGame), Description("Tiempo extra en milisegundos a esperar para comprobar si el DLC es utilizable.")]
            public int ExtraTimeCheckDLC { get; set; } = 0;

            [Category(OpenGame), Description("Tiempo extra en milisegundos a esperar antes de pulsar A en la pantalla de título.")]
            public int ExtraTimeLoadGame { get; set; } = 5000;

            [Category(CloseGame), Description("Tiempo extra en milisegundos a esperar después de hacer clic para cerrar el juego.")]
            public int ExtraTimeCloseGame { get; set; } = 0;

            [Category(RestartGame), Description("Ajustes relacionados con Reiniciar el juego.")]
            public ProfileSelectSettingsCategory ProfileSelectSettings { get; set; } = new();
        }

        [Category(RestartGame), TypeConverter(typeof(CategoryConverter<ProfileSelectSettingsCategory>))]
        public class ProfileSelectSettingsCategory
        {
            public override string ToString() => "Configuración de la selección de perfiles";

            [Category(OpenGame), Description("Actívalo si necesitas seleccionar un perfil al iniciar el juego.")]
            public bool ProfileSelectionRequired { get; set; } = true;

            [Category(OpenGame), Description("Tiempo extra en milisegundos para esperar a que se carguen los perfiles al iniciar el juego.")]
            public int ExtraTimeLoadProfile { get; set; } = 0;
        }
    }
}