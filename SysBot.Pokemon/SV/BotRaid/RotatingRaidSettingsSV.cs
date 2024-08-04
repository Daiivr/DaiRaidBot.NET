using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace SysBot.Pokemon
{

    public class RotatingRaidSettingsSV : IBotStateSettings
    {
        private const string Hosting = nameof(Hosting);
        private const string Counts = nameof(Counts);
        private const string FeatureToggle = nameof(FeatureToggle);
        
        public override string ToString() => "Configuración de incursión rotativa (solo Sc/Vi)";
        [DisplayName("Lista de incursiones activas")]

        [Category(Hosting), Description("Tu lista de incursiones activas se encuentra aquí.")]
        public List<RotatingRaidParameters> ActiveRaids { get; set; } = new();

        [DisplayName("Configuración de incursión")]
        public RotatingRaidSettingsCategory RaidSettings { get; set; } = new RotatingRaidSettingsCategory();

        [DisplayName("Configuración de embeds de Discord")]
        public RotatingRaidPresetFiltersCategory EmbedToggles { get; set; } = new RotatingRaidPresetFiltersCategory();

        [DisplayName("Configuración del lobby de incursión")]

        [Category(Hosting), Description("Opciones del lobby"), DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public LobbyFiltersCategory LobbyOptions { get; set; } = new();

        [DisplayName("Lista de Raiders Baneados")]

        [Category(Hosting), Description("Los NIDs de los usuarios que estan aquí son raiders baneados")]
        public RemoteControlAccessList RaiderBanList { get; set; } = new() { AllowIfEmpty = false };

        [DisplayName("Configuraciones randoms")]
        public MiscSettingsCategory MiscSettings { get; set; } = new MiscSettingsCategory();

        [Browsable(false)]
        public bool ScreenOff
        {
            get => MiscSettings.ScreenOff;
            set => MiscSettings.ScreenOff = value;
        }

        public class RotatingRaidParameters
        {
            public override string ToString() => $"{Title}";

            [DisplayName("¿Habilitar Raid?")]
            public bool ActiveInRotation { get; set; } = true;

            [DisplayName("Especies")]
            public Species Species { get; set; } = Species.None;

            [DisplayName("¿Forzar especies seleccionadas?")]
            public bool ForceSpecificSpecies { get; set; } = false;

            [DisplayName("Número de forma de Pokémon")]
            public int SpeciesForm { get; set; } = 0;

            [DisplayName("¿Pokemon es Shiny?")]
            public bool IsShiny { get; set; } = true;

            [DisplayName("Tipo de cristal")]
            public TeraCrystalType CrystalType { get; set; } = TeraCrystalType.Base;

            [DisplayName("¿Hacer la raid coded?")]
            public bool IsCoded { get; set; } = true;

            [DisplayName("Semilla")]
            public string Seed { get; set; } = "0";

            [DisplayName("Número de estrellas")]
            public int DifficultyLevel { get; set; } = 0;

            [DisplayName("Progreso del juego")]
            [TypeConverter(typeof(EnumConverter))]
            public GameProgressEnum StoryProgress { get; set; } = GameProgressEnum.Unlocked6Stars;

            [DisplayName("Raid Battler (Formato Showdown)")]
            public string[] PartyPK { get; set; } = [];

            [DisplayName("Acción que debe usar el bot")]
            public Action1Type Action1 { get; set; } = Action1Type.GoAllOut;

            [DisplayName("Retraso de la acción (en segundos)")]
            public int Action1Delay { get; set; } = 5;

            [DisplayName("ID de grupo (sólo Raids de evento)")]
            public int GroupID { get; set; } = 0;

            [DisplayName("Título del Embed")]
            public string Title { get; set; } = string.Empty;

            [Browsable(false)]
            public bool AddedByRACommand { get; set; } = false;

            [Browsable(false)]
            public bool SpriteAlternateArt { get; set; } = false; // Not enough alt art to even turn on

            [Browsable(false)]
            public string[] Description { get; set; } = [];

            [Browsable(false)]
            public bool RaidUpNext { get; set; } = false;

            [Browsable(false)]
            public string RequestCommand { get; set; } = string.Empty;

            [Browsable(false)]
            public ulong RequestedByUserID { get; set; }

            [Browsable(false)]
            [System.Text.Json.Serialization.JsonIgnore]
            public SocketUser? User { get; set; }

            [Browsable(false)]
            [System.Text.Json.Serialization.JsonIgnore]
            public List<SocketUser> MentionedUsers { get; set; } = [];
        }

        public class TeraTypeBattlers
        {
            public override string ToString() => $"Define a tus Raid Battlers";
            [DisplayName("Bug Battler")]
            public string[] BugBattler { get; set; } = [];

            [DisplayName("Dark Battler")]
            public string[] DarkBattler { get; set; } = [];

            [DisplayName("Dragon Battler")]
            public string[] DragonBattler { get; set; } = [];

            [DisplayName("Electric Battler")]
            public string[] ElectricBattler { get; set; } = [];

            [DisplayName("Fairy Battler")]
            public string[] FairyBattler { get; set; } = [];

            [DisplayName("Fighting Battler")]
            public string[] FightingBattler { get; set; } = [];

            [DisplayName("Fire Battler")]
            public string[] FireBattler { get; set; } = [];

            [DisplayName("Flying Battler")]
            public string[] FlyingBattler { get; set; } = [];

            [DisplayName("Ghost Battler")]
            public string[] GhostBattler { get; set; } = [];

            [DisplayName("Grass Battler")]
            public string[] GrassBattler { get; set; } = [];

            [DisplayName("Ground Battler")]
            public string[] GroundBattler { get; set; } = [];

            [DisplayName("Ice Battler")]
            public string[] IceBattler { get; set; } = [];

            [DisplayName("Normal Battler")]
            public string[] NormalBattler { get; set; } = [];

            [DisplayName("Poison Battler")]
            public string[] PoisonBattler { get; set; } = [];

            [DisplayName("Psychic Battler")]
            public string[] PsychicBattler { get; set; } = [];

            [DisplayName("Rock Battler")]
            public string[] RockBattler { get; set; } = [];

            [DisplayName("Steel Battler")]
            public string[] SteelBattler { get; set; } = [];

            [DisplayName("Water Battler")]
            public string[] WaterBattler { get; set; } = [];
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<RotatingRaidSettingsCategory>))]
        public class RotatingRaidSettingsCategory
        {
            private bool _randomRotation = false;
            private bool _mysteryRaids = false;

            public override string ToString() => "Configuración de incursión";

            [DisplayName("¿Generar incursiones activas a partir de un archivo?")]
            [Category(Hosting), Description("Cuando se activa, el bot intentará autogenerar tus incursiones desde el archivo \"raidsv.txt\" en el botstart.")]
            public bool GenerateRaidsFromFile { get; set; } = true;

            [DisplayName("¿Guardar incursiones activas en un archivo al salir?")]
            [Category(Hosting), Description("Cuando está activado, el bot guardará tu lista actual de ActiveRaids en el archivo \"savedSeeds.txt\" al parar el bot.")]
            public bool SaveSeedsToFile { get; set; } = true;

            [DisplayName("Total de incursiones a acoger antes de parar")]
            [Category(Hosting), Description("Introduce el número total de incursiones a realizar antes de que el bot se detenga automáticamente. Por defecto es 0 para ignorar este ajuste.")]
            public int TotalRaidsToHost { get; set; } = 0;

            [DisplayName("¿Rotar la lista de incursiones en orden aleatorio?"), Category(Hosting), Description("Cuando está activado, el bot elegirá aleatoriamente un Raid para ejecutarlo, manteniendo las peticiones priorizadas.")]
            public bool RandomRotation
            {
                get => _randomRotation;
                set
                {
                    _randomRotation = value;
                    if (value)
                        _mysteryRaids = false;
                }
            }

            [DisplayName("¿Activar las incursiones misteriosas?"), Category(Hosting), Description("Cuando es true, el bot añadirá semillas brillantes aleatorias a la cola. Solo se ejecutarán las Peticiones de Usuario y las Incursiones Misteriosas.")]
            public bool MysteryRaids
            {
                get => _mysteryRaids;
                set
                {
                    _mysteryRaids = value;
                    if (value)
                        _randomRotation = false;
                }
            }

            [DisplayName("Ajustes de la Incursión Misteriosa")]
            [Category("MysteryRaids"), Description("Ajustes específicos de las incursiones misteriosas.")]
            public MysteryRaidsSettings MysteryRaidsSettings { get; set; } = new MysteryRaidsSettings();

            [DisplayName("¿Desactivar las peticiones de incursión de los usuarios?")]
            [Category("Hosting"), Description("Cuando es verdadero, el bot no permitirá incursiones solicitadas por el usuario y les informará de que esta configuración está activada.")]
            public bool DisableRequests { get; set; } = false;

            [DisplayName("¿Permitir peticiones de incursión de usuarios privados?")]
            [Category("Hosting"), Description("Cuando es verdadero, el bot permitirá incursiones privadas.")]
            public bool PrivateRaidsEnabled { get; set; } = true;

            [DisplayName("Limitar las peticiones de los usuarios")]
            [Category("Hosting"), Description("Limitar el número de peticiones que un usuario puede realizar. Configura a 0 para desactivar.\nComandos: $lr <número>")]
            public int LimitRequests { get; set; } = 0;

            [DisplayName("Tiempo de espera para nuevas peticiones")]
            [Category("Hosting"), Description("Define el tiempo (en minutos) que el usuario debe esperar para hacer nuevas peticiones una vez alcanzado el número de peticiones limitadas. Configura a 0 para desactivar.\nComandos: $lrt <número en minutos>")]
            public int LimitRequestsTime { get; set; } = 0;

            [DisplayName("Mensaje de error por límite de peticiones")]
            [Category("Hosting"), Description("Mensaje personalizado para mostrar cuando un usuario alcanza su límite de peticiones.")]
            public string LimitRequestMsg { get; set; } = "Si desea evitar este límite, [describe cómo obtener el rol].";

            [DisplayName("Usuarios/Roles que pueden saltarse el límite de peticiones")]
            [Category("Hosting"), Description("Diccionario de IDs de usuarios y roles con nombres que pueden saltarse los límites de peticiones.\nComandos: $alb @Role o $alb @User")]
            public Dictionary<ulong, string> BypassLimitRequests { get; set; } = new Dictionary<ulong, string>();

            [DisplayName("¿Prevenir batallas en el mundo abierto?")]
            [Category("FeatureToggle"), Description("Prevenir ataques. Cuando es verdadero, los Spawns del mundo abierto (Pokémon) están deshabilitados en la próxima inyección de semilla. Cuando es falso, los Spawns del mundo abierto (Pokémon) están habilitados en la próxima inyección de semilla.")]
            public bool DisableOverworldSpawns { get; set; } = true;

            [DisplayName("¿Mantener la semilla del día actual?")]
            [Category("Hosting"), Description("Cuando está habilitado, el bot inyectará la semilla del día actual a la semilla del día de mañana.")]
            public bool KeepDaySeed { get; set; } = true;

            [DisplayName("¿Prevenir cambios de día?")]
            [Category("FeatureToggle"), Description("Cuando está habilitado, el bot retrocederá el tiempo 5 horas para evitar que tu día cambie. Asegúrate de que cuando inicies el bot la hora del Switch esté entre las 12:01am y las 7:00pm.")]
            public bool EnableTimeRollBack { get; set; } = true;

            [DisplayName("Unirse al programa de incursiones compartidas")]
            [Category("Hosting"), Description("Habilitar para unirse al programa de incursiones compartidas.")]
            public bool JoinSharedRaidsProgram { get; set; } = true;
        }

        public class MoveTypeEmojiInfo
        {
            [Description("El tipo de movimiento.")]
            public MoveType MoveType { get; set; }

            [Description("El código de emoji de Discord para este tipo de movimiento.")]
            public string EmojiCode { get; set; }

            public MoveTypeEmojiInfo() { }

            public MoveTypeEmojiInfo(MoveType moveType)
            {
                MoveType = moveType;
            }
            public override string ToString()
            {
                if (string.IsNullOrEmpty(EmojiCode))
                    return MoveType.ToString();

                return $"{EmojiCode}";
            }
        }

        [TypeConverter(typeof(ExpandableObjectConverter))]
        public class EmojiInfo
        {
            [Description("La cadena completa para el emoji.")]
            [DisplayName("Código de Emoji")]
            public string EmojiString { get; set; } = string.Empty;

            public override string ToString()
            {
                return string.IsNullOrEmpty(EmojiString) ? "No establecido" : EmojiString;
            }

        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<RotatingRaidPresetFiltersCategory>))]
        public class RotatingRaidPresetFiltersCategory
        {
            public override string ToString() => "Alternar Embed";

            [Category("Hosting"), Description("Mostrará iconos de tipo de movimiento junto a los movimientos en la integración de intercambio (solo en Discord). Requiere que el usuario suba los emojis a su servidor.")]
            [DisplayName("¿Usar emojis de tipo de movimiento?")]
            public bool MoveTypeEmojis { get; set; } = true;

            [Category("Hosting"), Description("Información personalizada de emojis para los tipos de movimiento.")]
            [DisplayName("Emojis personalizados de tipo de movimiento")]

            public List<MoveTypeEmojiInfo> CustomTypeEmojis { get; set; } =
            [
            new(MoveType.Bug),
            new(MoveType.Fire),
            new(MoveType.Flying),
            new(MoveType.Ground),
            new(MoveType.Water),
            new(MoveType.Grass),
            new(MoveType.Ice),
            new(MoveType.Rock),
            new(MoveType.Ghost),
            new(MoveType.Steel),
            new(MoveType.Fighting),
            new(MoveType.Electric),
            new(MoveType.Dragon),
            new(MoveType.Psychic),
            new(MoveType.Dark),
            new(MoveType.Normal),
            new(MoveType.Poison),
            new(MoveType.Fairy),
            ];

            [Category(Hosting), Description("The full string for the male gender emoji. Leave blank to not use.")]
            [DisplayName("Male Emoji Code")]
            public EmojiInfo MaleEmoji { get; set; } = new EmojiInfo();

            [Category(Hosting), Description("The full string for the female gender emoji. Leave blank to not use.")]
            [DisplayName("Female Emoji Code")]
            public EmojiInfo FemaleEmoji { get; set; } = new EmojiInfo();

            [Category(Hosting), Description("Raid embed description will be shown on every raid posted at the top of the embed.")]
            [DisplayName("Raid Embed Description")]
            public string[] RaidEmbedDescription { get; set; } = Array.Empty<string>();

            [Category(FeatureToggle), Description("Choose the TeraType Icon set to use in the author area of the embed.  Icon1 are custom, Icon2 is not.")]
            [DisplayName("Tera Icon Choice")]
            public TeraIconType SelectedTeraIconType { get; set; } = TeraIconType.Icon1;

            [Category(Hosting), Description("If true, the bot will show Moves on embeds.")]
            [DisplayName("Include Moves/Extra Moves in Embed?")]
            public bool IncludeMoves { get; set; } = true;

            [Category(Hosting), Description("When true, the embed will display current seed.")]
            [DisplayName("Include Current Seed in Embed?")]
            public bool IncludeSeed { get; set; } = true;

            [Category(FeatureToggle), Description("When enabled, the embed will countdown the amount of seconds in \"TimeToWait\" until starting the raid.")]
            [DisplayName("Include Countdown Timer in Embed?")]
            public bool IncludeCountdown { get; set; } = true;

            [Category(Hosting), Description("If true, the bot will show Type Advantages on embeds.")]
            [DisplayName("Include Type Advantage Hints in Embed?")]
            public bool IncludeTypeAdvantage { get; set; } = true;

            [Category(Hosting), Description("If true, the bot will show Special Rewards on embeds.")]
            [DisplayName("Include Rewards in Embed?")]
            public bool IncludeRewards { get; set; } = true;

            [Category(Hosting), Description("Select which rewards to display in the embed.")]
            [DisplayName("Rewards To Show")]
            public List<string> RewardsToShow { get; set; } = new List<string>
            {
                "Rare Candy",
                "Ability Capsule",
                "Bottle Cap",
                "Ability Patch",
                "Exp. Candy L",
                "Exp. Candy XL",
                "Sweet Herba Mystica",
                "Salty Herba Mystica",
                "Sour Herba Mystica",
                "Bitter Herba Mystica",
                "Spicy Herba Mystica",
                "Pokeball",
                "Shards",
                "Nugget",
                "Tiny Mushroom",
                "Big Mushroom",
                "Pearl",
                "Big Pearl",
                "Stardust",
                "Star Piece",
                "Gold Bottle Cap",
                "PP Up"
            };

            [Category("Hosting"), Description("Cantidad de tiempo (en segundos) para publicar una integración de incursión solicitada.")]
            [DisplayName("Publicar Integraciones de Solicitudes de Usuario en...")]
            public int RequestEmbedTime { get; set; } = 30;

            [Category("FeatureToggle"), Description("Cuando está habilitado, el bot intentará tomar capturas de pantalla para las integraciones de incursión. Si experimentas fallos a menudo sobre \"Tamaño/Parámetro\" intenta configurarlo como falso.")]
            [DisplayName("¿Usar Capturas de Pantalla?")]
            public bool TakeScreenshot { get; set; } = true;

            [Category("Hosting"), Description("Retraso en milisegundos para capturar una captura de pantalla una vez en la incursión.\n 0 Captura el Pokémon de la incursión de cerca.\n3500 Captura solo a los jugadores.\n10000 Captura a los jugadores y al Pokémon de la incursión.")]
            [DisplayName("Tiempo de Captura de Pantalla (Imágenes no Gif)")]
            public ScreenshotTimingOptions ScreenshotTiming { get; set; } = ScreenshotTimingOptions._3500;

            [Category("FeatureToggle"), Description("Cuando está habilitado, el bot tomará una imagen animada (gif) de lo que está sucediendo una vez dentro de la incursión, en lugar de una imagen estática estándar.")]
            [DisplayName("¿Usar Capturas de Pantalla Gif?")]
            public bool AnimatedScreenshot { get; set; } = true;

            [Category("FeatureToggle"), Description("Cantidad de fotogramas a capturar para la integración. 20-30 es un buen número.")]
            [DisplayName("Fotogramas a Capturar (Solo Gifs)")]
            public int Frames { get; set; } = 30;

            [Category("FeatureToggle"), Description("Calidad del GIF. Mayor calidad significa mayor tamaño de archivo.")]
            [DisplayName("Calidad del GIF")]
            public GifQuality GifQuality { get; set; } = GifQuality.Default;

            [Category("FeatureToggle"), Description("Cuando está habilitado, el bot ocultará el código de la incursión en la integración de Discord.")]
            public bool HideRaidCode { get; set; } = false;

            [Category("Customization"), Description("Mensaje personalizado para mostrar en la advertencia de rotación de incursión.")]
            public string CustomRaidRotationMessage { get; set; } = "";
        }

        [Category("MysteryRaids"), TypeConverter(typeof(ExpandableObjectConverter))]
        public class MysteryRaidsSettings
        {

            [DisplayName("Combatientes de Tipo Tera")]
            [TypeConverter(typeof(ExpandableObjectConverter))]
            public TeraTypeBattlers TeraTypeBattlers { get; set; } = new TeraTypeBattlers();

            [TypeConverter(typeof(ExpandableObjectConverter))]
            [DisplayName("Configuraciones de Progreso de 3 Estrellas")]
            public Unlocked3StarSettings Unlocked3StarSettings { get; set; } = new Unlocked3StarSettings();

            [TypeConverter(typeof(ExpandableObjectConverter))]
            [DisplayName("Configuraciones de Progreso de 4 Estrellas")]
            public Unlocked4StarSettings Unlocked4StarSettings { get; set; } = new Unlocked4StarSettings();

            [TypeConverter(typeof(ExpandableObjectConverter))]
            [DisplayName("Configuraciones de Progreso de 5 Estrellas")]
            public Unlocked5StarSettings Unlocked5StarSettings { get; set; } = new Unlocked5StarSettings();

            [TypeConverter(typeof(ExpandableObjectConverter))]
            [DisplayName("Configuraciones de Progreso de 6 Estrellas")]
            public Unlocked6StarSettings Unlocked6StarSettings { get; set; } = new Unlocked6StarSettings();

            public override string ToString() => "Configuraciones de Incursiones Misteriosas";
        }

        public class Unlocked3StarSettings
        {
            [DisplayName("¿Habilitar Progreso de Incursiones Misteriosas de 3 Estrellas?")]
            public bool Enabled { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 1* en Incursiones Desbloqueadas de 3*.")]
            public bool Allow1StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 2* en Incursiones Desbloqueadas de 3*.")]
            public bool Allow2StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 3* en Incursiones Desbloqueadas de 3*.")]
            public bool Allow3StarRaids { get; set; } = true;

            public override string ToString() => "Configuraciones de Incursiones de 3 Estrellas";
        }

        public class Unlocked4StarSettings
        {
            [DisplayName("¿Habilitar Progreso de Incursiones Misteriosas de 4 Estrellas?")]
            public bool Enabled { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 1* en Incursiones Desbloqueadas de 4*.")]
            public bool Allow1StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 2* en Incursiones Desbloqueadas de 4*.")]
            public bool Allow2StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 3* en Incursiones Desbloqueadas de 4*.")]
            public bool Allow3StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 4* en Incursiones Desbloqueadas de 4*.")]
            public bool Allow4StarRaids { get; set; } = true;

            public override string ToString() => "Configuraciones de Incursiones de 4 Estrellas";
        }

        [Category("MysteryRaids"), TypeConverter(typeof(ExpandableObjectConverter))]
        public class Unlocked5StarSettings
        {
            [DisplayName("¿Habilitar Progreso de Incursiones Misteriosas de 5 Estrellas?")]
            public bool Enabled { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 3* en Incursiones Desbloqueadas de 5*.")]
            public bool Allow3StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 4* en Incursiones Desbloqueadas de 5*.")]
            public bool Allow4StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 5* en Incursiones Desbloqueadas de 5*.")]
            public bool Allow5StarRaids { get; set; } = true;

            public override string ToString() => "Configuraciones de Incursiones de 5 Estrellas";
        }

        [Category("MysteryRaids"), TypeConverter(typeof(ExpandableObjectConverter))]
        public class Unlocked6StarSettings
        {
            [DisplayName("¿Habilitar Progreso de Incursiones Misteriosas de 6 Estrellas?")]
            public bool Enabled { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 3* en Incursiones Desbloqueadas de 6*.")]
            public bool Allow3StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 4* en Incursiones Desbloqueadas de 6*.")]
            public bool Allow4StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 5* en Incursiones Desbloqueadas de 6*.")]
            public bool Allow5StarRaids { get; set; } = true;

            [Category("Niveles de Dificultad"), Description("Permitir Incursiones de 6* en Incursiones Desbloqueadas de 6*.")]
            public bool Allow6StarRaids { get; set; } = true;

            public override string ToString() => "Configuraciones de Incursiones de 6 Estrellas";
        }

        [Category("Hosting"), TypeConverter(typeof(CategoryConverter<LobbyFiltersCategory>))]
        public class LobbyFiltersCategory
        {
            public override string ToString() => "Filtros de Lobby";

            [Category("Hosting"), Description("OpenLobby - Abre el lobby después de x lobbies vacíos\nSkipRaid - Pasa a la siguiente después de x pérdidas/lobbies vacíos\nContinue - Continúa hospedando la incursión")]
            [DisplayName("Método de Lobby")]
            public LobbyMethodOptions LobbyMethod { get; set; } = LobbyMethodOptions.SkipRaid;

            private int _raidLimit = 3;

            [Category("Hosting"), Description("Límite de incursiones vacías por parámetro antes de que el bot aloje una incursión sin código. El valor debe estar entre 1 y 3.")]
            [DisplayName("Límite de Incursiones Vacías")]
            public int EmptyRaidLimit
            {
                get => _raidLimit;
                set => SetRaidLimit(value);
            }

            [Category("Hosting"), Description("Límite de incursiones vacías/perdidas por parámetro antes de que el bot pase a la siguiente. El valor debe estar entre 1 y 3.")]
            [DisplayName("Límite para Saltar Incursiones")]
            public int SkipRaidLimit
            {
                get => _raidLimit;
                set => SetRaidLimit(value);
            }

            private void SetRaidLimit(int value)
            {
                _raidLimit = Math.Max(1, Math.Min(3, value));
            }

            [Category("FeatureToggle"), Description("Configura la acción que deseas que realice tu bot. 'AFK' hará que el bot esté inactivo, mientras que 'MashA' presiona A cada 3.5 segundos.")]
            [DisplayName("Acción del Botón A")]
            public RaidAction Action { get; set; } = RaidAction.MashA;

            [Category("FeatureToggle"), Description("Retraso para la acción 'MashA' en segundos. [3.5 es el valor predeterminado]")]
            [DisplayName("Retraso del Botón A (Segundos)")]
            public double MashADelay { get; set; } = 3.5;  // Valor predeterminado establecido en 3.5 segundos

            [Category("FeatureToggle"), Description("Tiempo extra en milisegundos para esperar después de que el lobby se disuelva en la incursión antes de decidir no capturar al raidmon.")]
            [DisplayName("Tiempo Extra para Disolver el Lobby")]
            public int ExtraTimeLobbyDisband { get; set; } = 0;

            [Category("FeatureToggle"), Description("Tiempo extra en milisegundos para esperar antes de cambiar el partypk.")]
            [DisplayName("Tiempo Extra para Preparar al Combatiente de Incursión")]
            public int ExtraTimePartyPK { get; set; } = 0;
        }

        [Category("Hosting"), TypeConverter(typeof(CategoryConverter<MiscSettingsCategory>))]
        public class MiscSettingsCategory
        {
            public override string ToString() => "Configuraciones Misceláneas";

            [Category("FeatureToggle"), Description("Configura el formato de Fecha/Hora de tu Switch en las configuraciones de Fecha/Hora. El día retrocederá automáticamente en 1 si la Fecha cambia.")]
            public DTFormat DateTimeFormat { get; set; } = DTFormat.MMDDYY;

            [Category("Hosting"), Description("Cuando está habilitado, el bot usará el método de overshoot para aplicar la corrección de rollover, de lo contrario usará clics de DDOWN.")]
            public bool UseOvershoot { get; set; } = false;

            [Category("Hosting"), Description("Cantidad de veces que se presiona DDOWN para acceder a las configuraciones de fecha/hora durante la corrección de rollover. [Predeterminado: 39 Clics]")]
            public int DDOWNClicks { get; set; } = 39;

            [Category("Hosting"), Description("Tiempo para la duración del desplazamiento hacia abajo en milisegundos para acceder a las configuraciones de fecha/hora durante la corrección de rollover. Deseas que sobrepase la configuración de Fecha/Hora en 1, ya que hará clic en DUP después de desplazarse hacia abajo. [Predeterminado: 930ms]")]
            public int HoldTimeForRollover { get; set; } = 900;

            [Category("Hosting"), Description("Cuando está habilitado, inicia el bot cuando estés en la pantalla de inicio con el juego cerrado. El bot solo ejecutará la rutina de rollover para que puedas intentar configurar una sincronización precisa.")]
            public bool ConfigureRolloverCorrection { get; set; } = false;

            [Category("FeatureToggle"), Description("Cuando está habilitado, la pantalla se apagará durante el funcionamiento normal del bot para ahorrar energía.")]
            public bool ScreenOff { get; set; }

            private int _completedRaids;

            [Category("Counts"), Description("Incursiones Iniciadas")]
            public int CompletedRaids
            {
                get => _completedRaids;
                set => _completedRaids = value;
            }

            [Category("Counts"), Description("Cuando está habilitado, los conteos se emitirán cuando se solicite una verificación de estado.")]
            public bool EmitCountsOnStatusCheck { get; set; }

            public int AddCompletedRaids() => Interlocked.Increment(ref _completedRaids);

            public IEnumerable<string> GetNonZeroCounts()
            {
                if (!EmitCountsOnStatusCheck)
                    yield break;
                if (CompletedRaids != 0)
                    yield return $"Incursiones Iniciadas: {CompletedRaids}";
            }
        }

        public class CategoryConverter<T> : TypeConverter
        {
            public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

            public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) => TypeDescriptor.GetProperties(typeof(T));

            public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType != typeof(string) && base.CanConvertTo(context, destinationType);
        }
    }
}