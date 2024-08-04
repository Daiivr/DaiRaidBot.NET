using PKHeX.Core;
using System.Collections.Generic;
using System.ComponentModel;

namespace SysBot.Pokemon;

public class LegalitySettings
{
    private const string Generate = nameof(Generate);

    private const string Misc = nameof(Misc);

    private string DefaultTrainerName = "Dai";

    [Category(Generate), Description("Permitir a los usuarios enviar una mayor personalización con comandos Batch Editor")]
    public bool AllowBatchCommands { get; set; } = true;

    [Category(Generate), Description("Permitir a los usuarios enviar OT, TID, SID y Género OT personalizados en los sets de Showdown.")]
    public bool AllowTrainerDataOverride { get; set; }

    [Category(Generate), Description("Impide intercambiar Pokémon que requieran un Rastreador HOME, aunque el archivo ya lo tenga."), DisplayName("No permitir Pokémon no nativos")]
    public bool DisallowNonNatives { get; set; } = false;

    [Category(Generate), Description("Impide intercambiar Pokémon que ya tengan un Rastreador HOME."), DisplayName("No permitir Pokémon con rastreador HOME")]
    public bool DisallowTracked { get; set; } = false;

    [Category(Generate), Description("Bot creará un Pokémon huevo de Pascua si se le proporciona un set ilegal.")]
    public bool EnableEasterEggs { get; set; }

    [Category(Generate), Description("Requiere rastreador HOME al intercambiar Pokémon que tenían que haber viajado entre los juegos de Switch.")]
    public bool EnableHOMETrackerCheck { get; set; } = false;

    [Category(Generate), Description("Supone que los conjuntos de nivel 50 son conjuntos competitivos de nivel 100.")]
    public bool ForceLevel100for50 { get; set; }

    [Category(Generate), Description("Fuerza la bola especificada si es legal.")]
    public bool ForceSpecifiedBall { get; set; } = true;

    [Category(Generate), Description("Idioma por defecto para los archivos PKM que no coincidan con ninguno de los archivos PKM proporcionados.")]
    public LanguageID GenerateLanguage { get; set; } = LanguageID.English;

    [Category(Generate), Description("Nombre predeterminado del entrenador original para los archivos PKM que no coincidan con ninguno de los archivos PKM proporcionados.")]
    public string GenerateOT
    {
        get => DefaultTrainerName;
        set
        {
                DefaultTrainerName = value;
        }
    }

    [Category(Generate), Description("Carpeta para los archivos PKM con datos de entrenador que se utilizarán para los archivos PKM regenerados.")]
    public string GeneratePathTrainerInfo { get; set; } = string.Empty;

    [Category(Generate), Description("ID secreto (SID) predeterminado de 16 bits para solicitudes que no coinciden con ninguno de los archivos de datos de entrenador proporcionados. Debe ser un número de 5 dígitos.")]
    public ushort GenerateSID16 { get; set; } = 54321;

    [Category(Generate), Description("ID de entrenador (TID) predeterminado de 16 bits para solicitudes que no coinciden con ninguno de los archivos de datos de entrenador proporcionados. Debe ser un número de 5 dígitos.")]
    public ushort GenerateTID16 { get; set; } = 12345;

    // Generate
    [Category(Generate), Description("Ruta del directorio MGDB para las tarjetas Wonder.")]
    public string MGDBPath { get; set; } = string.Empty;

    [Category(Generate), Description("El orden en el que se intentan los tipos de encuentro Pokémon.")]
    public List<EncounterTypeGroup> PrioritizeEncounters { get; set; } =
    [
        EncounterTypeGroup.Slot, EncounterTypeGroup.Egg,
        EncounterTypeGroup.Static, EncounterTypeGroup.Mystery,
        EncounterTypeGroup.Trade,
    ];

    [Category(Generate), Description("Si PrioritizeGame es \"True\", usa PrioritizeGameVersion para empezar a buscar encuentros. Si es \"Falso\", usa el juego más reciente como versión. Se recomienda dejar esto como \"True\".")]
    public bool PrioritizeGame { get; set; } = true;

    [Browsable(false)]
    [Category(Generate), Description("Especifica el primer juego a utilizar para generar encuentros, o el juego actual si este campo está configurado como \"Cualquiera\". Establezca PrioritizeGame a \"true\" para habilitarlo. Se recomienda dejarlo como \"Any\".")]
    public GameVersion PrioritizeGameVersion { get; set; } = GameVersion.Any;

    // Misc
    [Browsable(false)]
    [Category(Misc), Description("Pone a cero los rastreadores HOME para archivos PKM clonados y solicitados por el usuario. Se recomienda dejar esta opción desactivada para evitar la creación de datos HOME no válidos.")]
    public bool ResetHOMETracker { get; set; } = false;

    [Category(Generate), Description("Establece todas las cintas legales posibles para cualquier Pokémon generado.")]
    public bool SetAllLegalRibbons { get; set; }

    [Browsable(false)]
    [Category(Generate), Description("Añade la Versión Batalla en los juegos que la admiten (solo SWSH) para usar Pokémon de generaciones anteriores en partidas competitivas online.")]
    public bool SetBattleVersion { get; set; }

    [Category(Generate), Description("Establece una bola coincidente (basada en el color) para cualquier Pokémon generado.")]
    public bool SetMatchingBalls { get; set; } = true;

    [Category(Generate), Description("Tiempo máximo en segundos a emplear al generar un set antes de cancelarlo. Esto evita que los sets difíciles congelen el bot.")]
    public int Timeout { get; set; } = 15;

    [Category(Misc), Description("Aplicar pokemon válido con el entrenador OT/SID/TID (AutoOT)")]
    public bool UseTradePartnerInfo { get; set; } = true;

    public override string ToString() => "Configuración de la Legalidad de generación";
}
