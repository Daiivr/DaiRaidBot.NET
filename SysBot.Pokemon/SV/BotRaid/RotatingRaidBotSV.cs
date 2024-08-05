using Discord;
using Newtonsoft.Json;
using PKHeX.Core;
using RaidCrawler.Core.Structures;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using AnimatedGif;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.RotatingRaidSettingsSV;
using static SysBot.Pokemon.SV.BotRaid.Blocks;
using System.Text.RegularExpressions;
using static NatureTranslations;
using static MovesTranslationDictionary;

namespace SysBot.Pokemon.SV.BotRaid
{
    public class RotatingRaidBotSV : PokeRoutineExecutor9SV
    {
        private readonly PokeRaidHub<PK9> Hub;
        private readonly RotatingRaidSettingsSV Settings;
        private RemoteControlAccessList RaiderBanList => Settings.RaiderBanList;
        public static Dictionary<string, List<(int GroupID, int Index, string DenIdentifier)>> SpeciesToGroupIDMap = [];
        private static readonly HttpClient httpClient = new HttpClient();

        public RotatingRaidBotSV(PokeBotState cfg, PokeRaidHub<PK9> hub) : base(cfg)
        {
            Hub = hub;
            Settings = hub.Config.RotatingRaidSV;
        }

        public class PlayerInfo
        {
            public string OT { get; set; }
            public int RaidCount { get; set; }
        }

        private int LobbyError;
        private int RaidCount;
        private int WinCount;
        private int LossCount;
        private int SeedIndexToReplace = -1;
        public static GameProgress GameProgress;
        public static bool? currentSpawnsEnabled;
        public int StoryProgress;
        private int EventProgress;
        private int EmptyRaid = 0;
        private int LostRaid = 0;
        private readonly int FieldID = 0;
        private bool firstRun = true;
        public static int RotationCount { get; set; }
        private ulong TodaySeed;
        private ulong OverworldOffset;
        private ulong ConnectedOffset;
        private ulong RaidBlockPointerP;
        private ulong RaidBlockPointerK;
        private ulong RaidBlockPointerB;
        private readonly ulong[] TeraNIDOffsets = new ulong[3];
        private string TeraRaidCode { get; set; } = string.Empty;
        private string BaseDescription = string.Empty;
        private readonly Dictionary<ulong, int> RaidTracker = [];
        private SAV9SV HostSAV = new();
        private static readonly DateTime StartTime = DateTime.Now;
        public static RaidContainer? container;
        public static bool IsKitakami = false;
        public static bool IsBlueberry = false;
        private static DateTime TimeForRollBackCheck = DateTime.Now;
        private string denHexSeed;
        private int seedMismatchCount = 0;
        private readonly bool indicesInitialized = false;
        private static readonly int KitakamiDensCount = 0;
        private static readonly int BlueberryDensCount = 0;
        private readonly int InvalidDeliveryGroupCount = 0;
        private bool shouldRefreshMap = false;

        public override async Task MainLoop(CancellationToken token)
        {
            if (Settings.RaidSettings.GenerateRaidsFromFile)
            {
                GenerateSeedsFromFile();
                Log("Done.");
                Settings.RaidSettings.GenerateRaidsFromFile = false;
            }

            if (Settings.MiscSettings.ConfigureRolloverCorrection)
            {
                await RolloverCorrectionSV(token).ConfigureAwait(false);
                return;
            }

            if (Settings.ActiveRaids.Count < 1)
            {
                Log("ActiveRaids no puede ser 0. Por favor, configure sus parámetros para las raid(s) que está alojando.");
                return;
            }

            try
            {
                Log("Datos identificativos del entrenador de la consola anfitriona.");
                HostSAV = await IdentifyTrainer(token).ConfigureAwait(false);
                await InitializeHardware(Settings, token).ConfigureAwait(false);
                Log("Iniciando bucle principal de RotatingRaidBot.");
                await InnerLoop(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log(e.Message);
            }
            finally
            {
                SaveSeeds();
            }
            Log($"Finalizando bucle {nameof(RotatingRaidBotSV)}.");
            await HardStop().ConfigureAwait(false);
        }

        public override async Task RebootReset(CancellationToken t)
        {
            await ReOpenGame(new PokeRaidHubConfig(), t).ConfigureAwait(false);
            await HardStop().ConfigureAwait(false);
            await Task.Delay(2_000, t).ConfigureAwait(false);
            if (!t.IsCancellationRequested)
            {
                Log("Reiniciando el bucle interno.");
                await InnerLoop(t).ConfigureAwait(false);
            }
        }

        public override Task RefreshMap(CancellationToken t)
        {
            shouldRefreshMap = true;
            return Task.CompletedTask;
        }

        public class PlayerDataStorage
        {
            private readonly string filePath;

            public PlayerDataStorage(string baseDirectory)
            {
                var directoryPath = Path.Combine(baseDirectory, "raidfilessv");
                Directory.CreateDirectory(directoryPath);
                filePath = Path.Combine(directoryPath, "player_data.json");

                if (!File.Exists(filePath))
                    File.WriteAllText(filePath, "{}"); // Create a new JSON file if it does not exist.
            }

            public Dictionary<ulong, PlayerInfo> LoadPlayerData()
            {
                string json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<Dictionary<ulong, PlayerInfo>>(json) ?? [];
            }

            public void SavePlayerData(Dictionary<ulong, PlayerInfo> data)
            {
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
        }

        private void GenerateSeedsFromFile()
        {
            var folder = "raidfilessv";
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var prevrotationpath = "raidsv.txt";
            var rotationpath = "raidfilessv\\raidsv.txt";
            if (File.Exists(prevrotationpath))
                File.Move(prevrotationpath, rotationpath);
            if (!File.Exists(rotationpath))
            {
                File.WriteAllText(rotationpath, "000091EC-Kricketune-3-6,0000717F-Seviper-3-6");
                Log("Creación de un archivo raidsv.txt por defecto, omitiendo la generación ya que el archivo está vacío.");
                return;
            }

            if (!File.Exists(rotationpath))
                Log("raidsv.txt no está presente, omitiendo la generación de parámetros.");

            BaseDescription = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "raidfilessv\\bodyparam.txt";
            if (File.Exists(prevpath))
                File.Move(prevpath, filepath);
            if (File.Exists(filepath))
                BaseDescription = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "raidfilessv\\pkparam.txt";
            if (File.Exists(prevpk))
                File.Move(prevpk, pkpath);
            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            DirectorySearch(rotationpath, data);
        }

        private void SaveSeeds()
        {
            // Exit the function if saving seeds to file is not enabled
            if (!Settings.RaidSettings.SaveSeedsToFile)
                return;

            // Filter out raids that don't need to be saved
            var raidsToSave = Settings.ActiveRaids.Where(raid => !raid.AddedByRACommand).ToList();

            // Exit the function if there are no raids to save
            if (!raidsToSave.Any())
                return;

            // Define directory and file paths
            var directoryPath = "raidfilessv";
            var fileName = "savedSeeds.txt";
            var savePath = Path.Combine(directoryPath, fileName);

            // Create directory if it doesn't exist
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Initialize StringBuilder to build the save string
            StringBuilder sb = new();

            // Loop through each raid to be saved
            foreach (var raid in raidsToSave)
            {
                // Increment the StoryProgressLevel by 1 before saving
                int storyProgressValue = (int)raid.StoryProgress;

                // Build the string to save, including the incremented StoryProgressLevel
                sb.Append($"{raid.Seed}-{raid.Species}-{raid.DifficultyLevel}-{storyProgressValue}");
            }

            // Remove the trailing comma at the end
            if (sb.Length > 0)
                sb.Length--;

            // Write the built string to the file
            File.WriteAllText(savePath, sb.ToString());
        }

        private void DirectorySearch(string sDir, string data)
        {
            // Clear the active raids before populating it
            Settings.ActiveRaids.Clear();

            // Read the entire content from the file into a string
            string contents = File.ReadAllText(sDir);

            // Split the string based on commas to get each raid entry
            string[] moninfo = contents.Split(separator, StringSplitOptions.RemoveEmptyEntries);

            // Iterate over each raid entry
            for (int i = 0; i < moninfo.Length; i++)
            {
                // Split the entry based on dashes to get individual pieces of information
                var div = moninfo[i].Split(separatorArray, StringSplitOptions.RemoveEmptyEntries);

                // Check if the split result has exactly 4 parts
                if (div.Length != 4)
                {
                    Log($"Error al procesar la entrada: {moninfo[i]}. Se esperaban 4 partes pero se encontró {div.Length}. Omitiendo esta entrada.");
                    continue; // Skip processing this entry and move to the next one
                }

                // Extracting seed, title, and difficulty level
                var monseed = div[0];
                var montitle = div[1];

                if (!int.TryParse(div[2], out int difficultyLevel))
                {
                    Log($"No se ha podido analizar el nivel de dificultad de la entrada: {moninfo[i]}");
                    continue;
                }

                // Extract and convert the StoryProgressLevel
                if (!int.TryParse(div[3], out int storyProgressLevelFromSeed))
                {
                    Log($"No se puede analizar StoryProgressLevel para la entrada: {moninfo[i]}");
                    continue;
                }

                int convertedStoryProgressLevel = storyProgressLevelFromSeed - 1; // Converting based on given conditions

                // Determine the TeraCrystalType based on the difficulty level
                TeraCrystalType type = difficultyLevel switch
                {
                    6 => TeraCrystalType.Black,
                    7 => TeraCrystalType.Might,
                    _ => TeraCrystalType.Base,
                };

                // Create a new RotatingRaidParameters object and populate its properties
                RotatingRaidParameters param = new()
                {
                    Seed = monseed,
                    Title = montitle,
                    Species = RaidExtensions<PK9>.EnumParse<Species>(montitle),
                    CrystalType = type,
                    PartyPK = [data],
                    DifficultyLevel = difficultyLevel,
                    StoryProgress = (GameProgressEnum)convertedStoryProgressLevel
                };

                // Add the RotatingRaidParameters object to the ActiveRaids list
                Settings.ActiveRaids.Add(param);

                // Log the raid parameter generation
                Log($"Parámetros generados a partir de un archivo de texto para {montitle}.");
            }
        }

        private async Task InnerLoop(CancellationToken token)
        {
            try
            {
                bool partyReady;
                RotationCount = 0;
                var raidsHosted = 0;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Initialize offsets at the start of the routine and cache them.
                        await InitializeSessionOffsets(token).ConfigureAwait(false);
                        if (RaidCount == 0)
                        {
                            TodaySeed = BitConverter.ToUInt64(await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 8, token).ConfigureAwait(false), 0);
                            Log($"Semilla de hoy: {TodaySeed:X8}");
                        }

                        Log($"Preparación de parámetros para {Settings.ActiveRaids[RotationCount].Species}");
                        await ReadRaids(token).ConfigureAwait(false);

                        var currentSeed = BitConverter.ToUInt64(await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 8, token).ConfigureAwait(false), 0);
                        if (TodaySeed != currentSeed || LobbyError >= 2)
                        {
                            if (TodaySeed != currentSeed)
                            {
                                Log($"La Semilla de Hoy Actual {currentSeed:X8} no coincide con la Semilla de Hoy Inicial: {TodaySeed:X8}.\nIntentando anular la Semilla de Hoy...");
                                TodaySeed = currentSeed;
                                await OverrideTodaySeed(token).ConfigureAwait(false);
                                Log("La semilla de hoy ha sido sustituida por la semilla actual.");
                            }

                            if (LobbyError >= 2)
                            {
                                string? msg = $"Error al crear un lobby {LobbyError} veces.\n";
                                Log(msg);
                                await CloseGame(Hub.Config, token).ConfigureAwait(false);
                                await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
                                LobbyError = 0;
                                continue;
                            }
                        }

                        // Clear NIDs.
                        await SwitchConnection.WriteBytesAbsoluteAsync(new byte[32], TeraNIDOffsets[0], token).ConfigureAwait(false);

                        // Connect online and enter den.
                        int prepareResult;
                        do
                        {
                            prepareResult = await PrepareForRaid(token).ConfigureAwait(false);
                            if (prepareResult == 0)
                            {
                                Log("Fallo al preparar la incursión, reiniciando el juego.");
                                await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                            }
                        } while (prepareResult == 0);

                        if (prepareResult == 2)
                        {
                            // Seed was injected, restart the loop
                            continue;
                        }

                        // Wait until we're in lobby.
                        if (!await GetLobbyReady(false, token).ConfigureAwait(false))
                            continue;

                        if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
                        {
                            var user = Settings.ActiveRaids[RotationCount].User;
                            var mentionedUsers = Settings.ActiveRaids[RotationCount].MentionedUsers;

                            // Determine if the raid is a "Free For All"
                            bool isFreeForAll = !Settings.ActiveRaids[RotationCount].IsCoded || EmptyRaid >= Settings.LobbyOptions.EmptyRaidLimit;

                            if (!isFreeForAll)
                            {
                                try
                                {
                                    // Only get and send the raid code if it's not a "Free For All"
                                    var code = await GetRaidCode(token).ConfigureAwait(false);
                                    if (user != null)
                                    {
                                        await user.SendMessageAsync($"Su código de incursión es **{code}**").ConfigureAwait(false);
                                    }
                                    foreach (var mentionedUser in mentionedUsers)
                                    {
                                        await mentionedUser.SendMessageAsync($"El código de incursión para la incursión privada a la que has sido invitado por {user?.Username ?? "el anfitrión"} es **{code}**.").ConfigureAwait(false);
                                    }
                                }
                                catch (Discord.Net.HttpException ex)
                                {
                                    // Handle exception (e.g., log the error or send a message to a logging channel)
                                    Log($"No se ha podido enviar DM al usuario o usuarios mencionados. Puede que tengan los DMs desactivados. Excepción: {ex.Message}");
                                }
                            }
                        }

                        // Read trainers until someone joins.
                        (partyReady, _) = await ReadTrainers(token).ConfigureAwait(false);
                        if (!partyReady)
                        {
                            if (LostRaid >= Settings.LobbyOptions.SkipRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipRaid)
                            {
                                await SkipRaidOnLosses(token).ConfigureAwait(false);
                                EmptyRaid = 0;
                                continue;
                            }

                            // Should add overworld recovery with a game restart fallback.
                            await RegroupFromBannedUser(token).ConfigureAwait(false);

                            if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                            {
                                Log("Algo salió mal, intentando recuperarse.");
                                await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                                continue;
                            }

                            // Clear trainer OTs.
                            Log("Borrando OTs almacenados");
                            for (int i = 0; i < 3; i++)
                            {
                                List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                                ptr[2] += i * 0x30;
                                await SwitchConnection.PointerPoke(new byte[16], ptr, token).ConfigureAwait(false);
                            }
                            continue;
                        }
                        await CompleteRaid(token).ConfigureAwait(false);
                        raidsHosted++;
                        if (raidsHosted == Settings.RaidSettings.TotalRaidsToHost && Settings.RaidSettings.TotalRaidsToHost > 0)
                            break;
                    }
                    catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "_0")
                    {
                        Log("Error de conexión detectado. Realizando reinicio y reset.");
                        await PerformRebootAndReset(token).ConfigureAwait(false);
                        return; // Exit the InnerLoop method after reboot and reset
                    }
                }
                if (Settings.RaidSettings.TotalRaidsToHost > 0 && raidsHosted != 0)
                    Log("Se ha llegado al límite de incursiones para unirse");
            }
            catch (Exception ex)
            {
                Log($"Se ha producido un error inesperado en InnerLoop: {ex.Message}");
                // Handle other exceptions as needed
            }
        }

        public override async Task HardStop()
        {
            try
            {
                Directory.Delete("cache", true);
            }
            catch (Exception)
            { }
            Settings.ActiveRaids.RemoveAll(p => p.AddedByRACommand);
            Settings.ActiveRaids.RemoveAll(p => p.Title == "Mystery Shiny Raid");
            await CleanExit(CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LocateSeedIndex(CancellationToken token)
        {
            int upperBound = KitakamiDensCount == 25 ? 94 : 95;
            int startIndex = KitakamiDensCount == 25 ? 94 : 95;

            var data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 2304, token).ConfigureAwait(false);
            for (int i = 0; i < 69; i++)  // Paldea Raids
            {
                var seed = BitConverter.ToUInt32(data.AsSpan(0x20 + i * 0x20, 4));
                if (seed == 0)
                {
                    SeedIndexToReplace = i;
                    Log($"Raid Den Ubicada en {i} esta en Paldea.");
                    return;
                }
            }

            data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK + 0x10, 0xC80, token).ConfigureAwait(false);
            for (int i = 69; i < upperBound; i++)  // Kitakami Raids
            {
                var seed = BitConverter.ToUInt32(data.AsSpan((i - 69) * 0x20, 4));
                if (seed == 0)
                {
                    SeedIndexToReplace = i;
                    Log($"Raid Den Ubicada en {i} esta en Kitakami.");
                    IsKitakami = true;
                    return;
                }
            }

            // Adding support for Blueberry Raids
            data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerB + 0x10, 0xA00, token).ConfigureAwait(false);
            for (int i = startIndex; i < 118; i++)  // Blueberry Raids
            {
                var seed = BitConverter.ToUInt32(data.AsSpan((i - startIndex) * 0x20, 4));
                if (seed == 0)
                {
                    SeedIndexToReplace = i - 1;  // Adjusting the index by subtracting one
                    Log($"Raid Den Ubicada en {i} esta en Blueberry.");
                    IsBlueberry = true;
                    return;
                }
            }
            Log($"Índice no ubicado.");
        }

        private async Task CompleteRaid(CancellationToken token)
        {
            try
            {
                var trainers = new List<(ulong, RaidMyStatus)>();

                if (!await CheckIfConnectedToLobbyAndLog(token))
                {
                    throw new Exception("No conectado al lobby");
                }

                if (!await EnsureInRaid(token))
                {
                    throw new Exception("No en incursión");
                }

                if (!Settings.EmbedToggles.AnimatedScreenshot)
                {
                    var screenshotDelay = (int)Settings.EmbedToggles.ScreenshotTiming;
                    await Task.Delay(screenshotDelay, token).ConfigureAwait(false);
                }

                var lobbyTrainersFinal = new List<(ulong, RaidMyStatus)>();
                if (!await UpdateLobbyTrainersFinal(lobbyTrainersFinal, trainers, token))
                {
                    throw new Exception("No se pudo actualizar a los entrenadores de la sala");
                }

                if (!await HandleDuplicatesAndEmbeds(lobbyTrainersFinal, token))
                {
                    throw new Exception("No se pudieron controlar duplicados y embeds");
                }

                await Task.Delay(10_000, token).ConfigureAwait(false);

                if (!await ProcessBattleActions(token))
                {
                    throw new Exception("No se pudieron procesar las acciones de batalla.");
                }

                bool isRaidCompleted = await HandleEndOfRaidActions(token);
                if (!isRaidCompleted)
                {
                    throw new Exception("Incursión no completada");
                }

                await FinalizeRaidCompletion(trainers, isRaidCompleted, token);
            }
            catch (Exception ex)
            {
                Log($"Se ha producido un error durante el raid: {ex.Message}");
                await PerformRebootAndReset(token);
            }
        }

        private async Task PerformRebootAndReset(CancellationToken t)
        {
            EmbedBuilder embed = new()
            {
                Title = "Bot Reiniciando",
                Description = "El bot encontró un problema y actualmente se está reiniciando. Por favor espere.",
                Color = Discord.Color.Red,
                ThumbnailUrl = "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/x.png"
            };
            EchoUtil.RaidEmbed(null, "", embed);

            await ReOpenGame(new PokeRaidHubConfig(), t).ConfigureAwait(false);
            await HardStop().ConfigureAwait(false);
            await Task.Delay(2_000, t).ConfigureAwait(false);

            if (!t.IsCancellationRequested)
            {
                Log("Reiniciando el bucle interno");
                await InnerLoop(t).ConfigureAwait(false);
            }
        }

        private async Task<bool> CheckIfConnectedToLobbyAndLog(CancellationToken token)
        {
            try
            {
                if (await IsConnectedToLobby(token).ConfigureAwait(false))
                {
                    Log("¡Preparándonos para la batalla!");
                    return true;
                }
                else
                {
                    Log("No conectado al lobby, reabriendo el juego.");
                    await ReOpenGame(Hub.Config, token);
                    return false;
                }
            }
            catch (Exception ex) // Catch the appropriate exception
            {
                Log($"Error al comprobar la conexión del lobby: {ex.Message}, reabriendo el juego.");
                await ReOpenGame(Hub.Config, token);
                return false;
            }
        }

        private async Task<bool> EnsureInRaid(CancellationToken linkedToken)
        {
            var startTime = DateTime.Now;

            while (!await IsInRaid(linkedToken).ConfigureAwait(false))
            {
                if (linkedToken.IsCancellationRequested || (DateTime.Now - startTime).TotalMinutes > 5)
                {
                    Log("Se ha alcanzado un tiempo muerto o se ha solicitado la cancelación, reabriendo el juego.");
                    await ReOpenGame(Hub.Config, linkedToken);
                    return false;
                }

                if (!await IsConnectedToLobby(linkedToken).ConfigureAwait(false))
                {
                    Log("Se perdió la conexión con el lobby, reabriendo el juego.");
                    await ReOpenGame(Hub.Config, linkedToken);
                    return false;
                }

                await Click(A, 1_000, linkedToken).ConfigureAwait(false);
            }
            return true;
        }

        public async Task<bool> UpdateLobbyTrainersFinal(List<(ulong, RaidMyStatus)> lobbyTrainersFinal, List<(ulong, RaidMyStatus)> trainers, CancellationToken token)
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var storage = new PlayerDataStorage(baseDirectory);
            var playerData = storage.LoadPlayerData();

            // Clear NIDs to refresh player check.
            await SwitchConnection.WriteBytesAbsoluteAsync(new byte[32], TeraNIDOffsets[0], token).ConfigureAwait(false);
            await Task.Delay(5_000, token).ConfigureAwait(false);

            // Loop through trainers again in case someone disconnected.
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var nidOfs = TeraNIDOffsets[i];
                    var data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                    var nid = BitConverter.ToUInt64(data, 0);

                    if (nid == 0)
                        continue;

                    List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                    ptr[2] += i * 0x30;
                    var trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(trainer.OT) || HostSAV.OT == trainer.OT)
                        continue;

                    lobbyTrainersFinal.Add((nid, trainer));

                    if (!playerData.TryGetValue(nid, out var info))
                    {
                        // New player
                        playerData[nid] = new PlayerInfo { OT = trainer.OT, RaidCount = 1 };
                        Log($"Nuevo jugador: {trainer.OT} | TID: {trainer.DisplayTID} | NID: {nid}.");
                    }
                    else
                    {
                        // Returning player
                        info.RaidCount++;
                        playerData[nid] = info; // Update the info back to the dictionary.
                        Log($"Jugador que regresa: {trainer.OT} | TID: {trainer.DisplayTID} | NID: {nid} | Raids: {info.RaidCount}");
                    }
                }
                catch (IndexOutOfRangeException ex)
                {
                    Log($"Excepción de índice fuera de rango capturada: {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    Log($"Un error desconocido ocurrió: {ex.Message}");
                    return false;
                }
            }

            // Save player data after processing all players.
            storage.SavePlayerData(playerData);
            return true;
        }

        private async Task<bool> HandleDuplicatesAndEmbeds(List<(ulong, RaidMyStatus)> lobbyTrainersFinal, CancellationToken token)
        {
            var nidDupe = lobbyTrainersFinal.Select(x => x.Item1).ToList();
            var dupe = lobbyTrainersFinal.Count > 1 && nidDupe.Distinct().Count() == 1;
            if (dupe)
            {
                // We read bad data, reset game to end early and recover.
                var msg = "¡Ups! Algo salió mal, reiniciando para recuperar.";
                bool success = false;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await Task.Delay(5_000, token);
                        await EnqueueEmbed(null, "", false, false, false, false, token).ConfigureAwait(false);
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"El intento {attempt} falló con error: {ex.Message}");
                        if (attempt == 3)
                        {
                            Log("Todos los intentos fracasaron. Continuando sin enviar el embed.");
                        }
                    }
                }

                if (!success)
                {
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    return false;
                }
            }

            var names = lobbyTrainersFinal.Select(x => x.Item2.OT).ToList();
            bool hatTrick = lobbyTrainersFinal.Count == 3 && names.Distinct().Count() == 1;

            bool embedSuccess = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await EnqueueEmbed(names, "", hatTrick, false, false, true, token).ConfigureAwait(false);
                    embedSuccess = true;
                    break;
                }
                catch (Exception ex)
                {
                    Log($"El intento {attempt} falló con error: {ex.Message}");
                    if (attempt == 3)
                    {
                        Log("Todos los intentos fracasaron. Continuando sin enviar el embed.");
                    }
                }
            }

            return embedSuccess;
        }

        private async Task<bool> ProcessBattleActions(CancellationToken token)
        {
            int nextUpdateMinute = 2;
            DateTime battleStartTime = DateTime.Now;
            bool hasPerformedAction1 = false;
            bool timedOut = false;
            bool hasPressedHome = false;

            while (await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                // New check: Are we still in a raid?
                if (!await IsInRaid(token).ConfigureAwait(false))
                {
                    Log("Ya no estoy en la incursión, deteniendo las acciones de batalla.");
                    return false;
                }
                TimeSpan timeInBattle = DateTime.Now - battleStartTime;

                // Check for battle timeout
                if (timeInBattle.TotalMinutes >= 15)
                {
                    Log("La batalla se agotó después de 15 minutos. Incluso Netflix me preguntó si todavía estaba viendo...");
                    timedOut = true;
                    break;
                }

                // Handle the first action with a delay
                if (!hasPerformedAction1)
                {
                    int action1DelayInSeconds = Settings.ActiveRaids[RotationCount].Action1Delay;
                    var action1Name = Settings.ActiveRaids[RotationCount].Action1;
                    int action1DelayInMilliseconds = action1DelayInSeconds * 1000;
                    Log($"Esperando {action1DelayInSeconds} segundos. No hay prisa, nos estamos relajando.");
                    await Task.Delay(action1DelayInMilliseconds, token).ConfigureAwait(false);
                    await MyActionMethod(token).ConfigureAwait(false);
                    Log($"{action1Name} hecho. ¿No fue divertido?");
                    hasPerformedAction1 = true;
                }
                else
                {
                    // Execute raid actions based on configuration
                    switch (Settings.LobbyOptions.Action)
                    {
                        case RaidAction.AFK:
                            await Task.Delay(3_000, token).ConfigureAwait(false);
                            break;

                        case RaidAction.MashA:
                            if (await IsConnectedToLobby(token).ConfigureAwait(false))
                            {
                                int mashADelayInMilliseconds = (int)(Settings.LobbyOptions.MashADelay * 1000);
                                await Click(A, mashADelayInMilliseconds, token).ConfigureAwait(false);
                            }
                            break;
                    }
                }

                // Periodic battle status log at 2-minute intervals
                if (timeInBattle.TotalMinutes >= nextUpdateMinute)
                {
                    Log($"Han pasado {nextUpdateMinute} minutos. Todavía estamos en la batalla...");
                    nextUpdateMinute += 2; // Update the time for the next status update.
                }
                // Check if the battle has been ongoing for 6 minutes
                if (timeInBattle.TotalMinutes >= 6 && !hasPressedHome)
                {
                    // Hit Home button twice in case we are stuck
                    await Click(HOME, 0_500, token).ConfigureAwait(false);
                    await Click(HOME, 0_500, token).ConfigureAwait(false);
                    hasPressedHome = true;
                }
                // Make sure to wait some time before the next iteration to prevent a tight loop
                await Task.Delay(1000, token); // Wait for a second before checking again
            }

            return !timedOut;
        }

        private async Task<bool> HandleEndOfRaidActions(CancellationToken token)
        {
            LobbyFiltersCategory settings = new();

            Log("¡Lobby de incursión disuelto!");
            await Task.Delay(1_500 + settings.ExtraTimeLobbyDisband, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(DDOWN, 0_500, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);
            bool ready = true;

            if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipRaid)
            {
                Log($"Lobbies perdidos/vacíos: {LostRaid}/{Settings.LobbyOptions.SkipRaidLimit}");

                if (LostRaid >= Settings.LobbyOptions.SkipRaidLimit)
                {
                    Log($"Tuvimos {Settings.LobbyOptions.SkipRaidLimit} incursiones perdidas/vacías. ¡Seguimos adelante!");
                    await SanitizeRotationCount(token).ConfigureAwait(false);
                    await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
                    ready = true;
                }
            }

            return ready;
        }

        private async Task FinalizeRaidCompletion(List<(ulong, RaidMyStatus)> trainers, bool ready, CancellationToken token)
        {
            Log("Volviendo al mundo exterior...");
            await Task.Delay(2_500, token).ConfigureAwait(false);
            while (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                await Click(A, 1_000, token).ConfigureAwait(false);

            await LocateSeedIndex(token).ConfigureAwait(false);
            await CountRaids(trainers, token).ConfigureAwait(false);
            // Update RotationCount after locating seed index
            if (Settings.ActiveRaids.Count > 1)
            {
                await SanitizeRotationCount(token).ConfigureAwait(false);
            }
            await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await CloseGame(Hub.Config, token).ConfigureAwait(false);

            if (ready)
                await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
            else
            {
                if (Settings.ActiveRaids.Count > 1)
                {
                    RotationCount = (RotationCount + 1) % Settings.ActiveRaids.Count;
                    if (RotationCount == 0)
                    {
                        Log($"Restablecimiento del recuento de rotación a {RotationCount}");
                    }

                    Log($"Pasando a la siguiente rotación para {Settings.ActiveRaids[RotationCount].Species}.");
                    await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
                }
                else
                    await StartGame(Hub.Config, token).ConfigureAwait(false);
            }

            if (Settings.RaidSettings.KeepDaySeed)
                await OverrideTodaySeed(token).ConfigureAwait(false);
        }

        public async Task MyActionMethod(CancellationToken token)
        {
            // Let's rock 'n roll with these moves!
            switch (Settings.ActiveRaids[RotationCount].Action1)
            {
                case Action1Type.GoAllOut:
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.HangTough:
                case Action1Type.HealUp:
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    int ddownTimes = Settings.ActiveRaids[RotationCount].Action1 == Action1Type.HangTough ? 1 : 2;
                    for (int i = 0; i < ddownTimes; i++)
                    {
                        await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.Move1:
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.Move2:
                case Action1Type.Move3:
                case Action1Type.Move4:
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    int moveDdownTimes = Settings.ActiveRaids[RotationCount].Action1 == Action1Type.Move2 ? 1 : Settings.ActiveRaids[RotationCount].Action1 == Action1Type.Move3 ? 2 : 3;
                    for (int i = 0; i < moveDdownTimes; i++)
                    {
                        await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                default:
                    Console.WriteLine("Acción desconocida, ¿cuál es el movimiento?");
                    throw new InvalidOperationException("¡Tipo de acción desconocido!");
            }
        }

        private async Task<uint> ReadAreaId(int raidIndex, CancellationToken token)
        {
            List<long> pointer = CalculateDirectPointer(raidIndex);
            int areaIdOffset = 20;

            return await ReadValue("Area ID", 4, AdjustPointer(pointer, areaIdOffset), token);
        }

        private async Task CountRaids(List<(ulong, RaidMyStatus)>? trainers, CancellationToken token)
        {
            if (trainers is not null)
            {
                Log("De vuelta en el mundo exterior, comprobando si ganamos o perdimos.");

                int currentRaidIndex = SeedIndexToReplace;
                uint areaId = await ReadAreaId(currentRaidIndex, token);

                if (areaId == 0)
                {
                    Log("¡Yay! ¡Derrotamos la incursión!");
                    WinCount++;
                }
                else
                {
                    Log("Vaya, perdimos la incursión");
                    LossCount++;
                }
            }
            else
            {
                Log("No hay entrenadores disponibles para verificar el estado de victorias / derrotas.");
            }
        }

        private async Task OverrideTodaySeed(CancellationToken token)
        {
            Log("Intentando anular la Semilla de Hoy");

            var todayoverride = BitConverter.GetBytes(TodaySeed);
            List<long> ptr = new(Offsets.RaidBlockPointerP);
            ptr[3] += 0x8;
            await SwitchConnection.PointerPoke(todayoverride, ptr, token).ConfigureAwait(false);

            Log("Anulación de semillas de hoy completada");
        }

        private async Task OverrideSeedIndex(int index, CancellationToken token)
        {
            if (index == -1)
            {
                Log("El índice es -1, omitiendo la anulación de semillas.");
                return;
            }

            var crystalType = Settings.ActiveRaids[RotationCount].CrystalType;
            var seed = uint.Parse(Settings.ActiveRaids[RotationCount].Seed, NumberStyles.AllowHexSpecifier);
            var speciesName = Settings.ActiveRaids[RotationCount].Species.ToString();
            var groupID = Settings.ActiveRaids[RotationCount].GroupID;
            var denLocations = LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_base.json");
            string? denIdentifier = null;

            // Check if the user is not in Paldea and adjust the crystal type accordingly
            if ((IsKitakami || IsBlueberry) && (crystalType == TeraCrystalType.Might || crystalType == TeraCrystalType.Distribution))
            {
                crystalType = TeraCrystalType.Black;
                Log("El usuario no se encuentra en Paldea. Ajustando el tipo de cristal a Negro.");
            }

            if (crystalType == TeraCrystalType.Might || crystalType == TeraCrystalType.Distribution)
            {
                uint defaultSeed = uint.Parse("000118C8", NumberStyles.AllowHexSpecifier);
                if (index != -1)
                {
                    List<long> prevPtr = DeterminePointer(index);
                    byte[] defaultSeedBytes = BitConverter.GetBytes(defaultSeed);
                    await SwitchConnection.PointerPoke(defaultSeedBytes, prevPtr, token).ConfigureAwait(false);
                    Log($"Estableciendo la semilla predeterminada {defaultSeed:X8} en el índice anterior {index}");
                    await Task.Delay(1_500, token).ConfigureAwait(false);
                }
                if (SpeciesToGroupIDMap.TryGetValue(speciesName, out var groupIDAndIndices))
                {
                    var specificIndexInfo = groupIDAndIndices.FirstOrDefault(x => x.GroupID == groupID);
                    if (specificIndexInfo != default)
                    {
                        index = specificIndexInfo.Index; // Adjusted index based on GroupID and species
                        denIdentifier = specificIndexInfo.DenIdentifier; // Capture the DenIdentifier for teleportation
                        Log($"Usar el índice específico {index} para GroupID: {groupID}, species: {speciesName} y DenIdentifier: {denIdentifier}.");
                    }
                }
                List<long> ptr = DeterminePointer(index);
                byte[] seedBytes = BitConverter.GetBytes(seed);
                await SwitchConnection.PointerPoke(seedBytes, ptr, token).ConfigureAwait(false);
                Log($"Inyecté la semilla {seed:X8} en el índice {index}");

                var crystalPtr = new List<long>(ptr);
                crystalPtr[3] += 0x08;
                byte[] crystalBytes = BitConverter.GetBytes((int)crystalType);
                await SwitchConnection.PointerPoke(crystalBytes, crystalPtr, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);

                // Teleportation logic
                if (denIdentifier != null && denLocations.TryGetValue(denIdentifier, out var coordinates))
                {
                    await TeleportToDen(coordinates[0], coordinates[1], coordinates[2], token);
                    Log($"Teletransportado con éxito a la guarida: {denIdentifier} con coordenadas {String.Join(", ", coordinates)}.");
                }
                else
                {
                    Log($"No se ha encontrado la ubicación del den para DenIdentifier: {denIdentifier}.");
                }
            }
            else
            {
                List<long> ptr = DeterminePointer(index);
                // Overriding the seed
                byte[] inj = BitConverter.GetBytes(seed);
                var currseed = await SwitchConnection.PointerPeek(4, ptr, token).ConfigureAwait(false);

                // Reverse the byte array of the current seed for logging purposes if necessary
                byte[] currSeedForLogging = (byte[])currseed.Clone();
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(currSeedForLogging);
                }

                // Reverse the byte array of the new seed for logging purposes if necessary
                byte[] injForLogging = (byte[])inj.Clone();
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(injForLogging);
                }

                // Convert byte arrays to hexadecimal strings for logging
                string currSeedHex = BitConverter.ToString(currSeedForLogging).Replace("-", "");
                string newSeedHex = BitConverter.ToString(injForLogging).Replace("-", "");

                Log($"Reemplazando {currSeedHex} por {newSeedHex}.");
                await SwitchConnection.PointerPoke(inj, ptr, token).ConfigureAwait(false);

                // Overriding the crystal type
                var ptr2 = new List<long>(ptr);
                ptr2[3] += 0x08;
                var crystal = BitConverter.GetBytes((int)crystalType);
                var currcrystal = await SwitchConnection.PointerPeek(1, ptr2, token).ConfigureAwait(false);
                if (currcrystal != crystal)
                    await SwitchConnection.PointerPoke(crystal, ptr2, token).ConfigureAwait(false);
            }
        }

        private async Task CreateMysteryRaidAsync()
        {
            await Task.Yield();

            try
            {
                CreateMysteryRaid();
            }
            catch (Exception ex)
            {
                Log($"Error en CreateMysteryRaid: {ex.Message}");
            }
        }

        private void CreateMysteryRaid()
        {
            uint randomSeed = GenerateRandomShinySeed();
            Random random = new();
            var mysteryRaidsSettings = Settings.RaidSettings.MysteryRaidsSettings;

            // Check if any Mystery Raid setting is enabled
            if (!(mysteryRaidsSettings.Unlocked3StarSettings.Enabled || mysteryRaidsSettings.Unlocked4StarSettings.Enabled ||
                  mysteryRaidsSettings.Unlocked5StarSettings.Enabled || mysteryRaidsSettings.Unlocked6StarSettings.Enabled))
            {
                Log("Todas las opciones de incursiones misteriosas están desactivadas. Las incursiones misteriosas se desactivarán.");
                Settings.RaidSettings.MysteryRaids = false;
                return;
            }

            // Create a list of enabled StoryProgressLevels
            var enabledLevels = new List<GameProgress>();
            if (mysteryRaidsSettings.Unlocked3StarSettings.Enabled) enabledLevels.Add(GameProgress.Unlocked3Stars);
            if (mysteryRaidsSettings.Unlocked4StarSettings.Enabled) enabledLevels.Add(GameProgress.Unlocked4Stars);
            if (mysteryRaidsSettings.Unlocked5StarSettings.Enabled) enabledLevels.Add(GameProgress.Unlocked5Stars);
            if (mysteryRaidsSettings.Unlocked6StarSettings.Enabled) enabledLevels.Add(GameProgress.Unlocked6Stars);

            // Randomly pick a StoryProgressLevel from the enabled levels
            GameProgress gameProgress = enabledLevels[random.Next(enabledLevels.Count)];

            // Initialize a list to store possible difficulties
            List<int> possibleDifficulties = [];

            // Determine possible difficulties based on the selected GameProgress
            switch (gameProgress)
            {
                case GameProgress.Unlocked3Stars:
                    if (mysteryRaidsSettings.Unlocked3StarSettings.Allow1StarRaids) possibleDifficulties.Add(1);
                    if (mysteryRaidsSettings.Unlocked3StarSettings.Allow2StarRaids) possibleDifficulties.Add(2);
                    if (mysteryRaidsSettings.Unlocked3StarSettings.Allow3StarRaids) possibleDifficulties.Add(3);
                    break;

                case GameProgress.Unlocked4Stars:
                    if (mysteryRaidsSettings.Unlocked4StarSettings.Allow1StarRaids) possibleDifficulties.Add(1);
                    if (mysteryRaidsSettings.Unlocked4StarSettings.Allow2StarRaids) possibleDifficulties.Add(2);
                    if (mysteryRaidsSettings.Unlocked4StarSettings.Allow3StarRaids) possibleDifficulties.Add(3);
                    if (mysteryRaidsSettings.Unlocked4StarSettings.Allow4StarRaids) possibleDifficulties.Add(4);
                    break;

                case GameProgress.Unlocked5Stars:
                    if (mysteryRaidsSettings.Unlocked5StarSettings.Allow3StarRaids) possibleDifficulties.Add(3);
                    if (mysteryRaidsSettings.Unlocked5StarSettings.Allow4StarRaids) possibleDifficulties.Add(4);
                    if (mysteryRaidsSettings.Unlocked5StarSettings.Allow5StarRaids) possibleDifficulties.Add(5);
                    break;

                case GameProgress.Unlocked6Stars:
                    if (mysteryRaidsSettings.Unlocked6StarSettings.Allow3StarRaids) possibleDifficulties.Add(3);
                    if (mysteryRaidsSettings.Unlocked6StarSettings.Allow4StarRaids) possibleDifficulties.Add(4);
                    if (mysteryRaidsSettings.Unlocked6StarSettings.Allow5StarRaids) possibleDifficulties.Add(5);
                    if (mysteryRaidsSettings.Unlocked6StarSettings.Allow6StarRaids) possibleDifficulties.Add(6);
                    break;
            }

            // Check if there are any enabled difficulty levels
            if (possibleDifficulties.Count == 0)
            {
                Log("No hay niveles de dificultad habilitados para el progreso de la historia seleccionado. Las incursiones misteriosas se desactivarán.");
                Settings.RaidSettings.MysteryRaids = false; // Disable Mystery Raids
                return; // Exit the method
            }

            // Randomly pick a difficulty level from the possible difficulties
            int randomDifficultyLevel = possibleDifficulties[random.Next(possibleDifficulties.Count)];

            // Determine the crystal type based on difficulty level
            var crystalType = randomDifficultyLevel switch
            {
                >= 1 and <= 5 => TeraCrystalType.Base,
                6 => TeraCrystalType.Black,
                _ => throw new ArgumentException("Nivel de dificultad no válido.")
            };

            string seedValue = randomSeed.ToString("X8");
            int contentType = randomDifficultyLevel == 6 ? 1 : 0;
            TeraRaidMapParent map;
            if (!IsBlueberry && !IsKitakami)
            {
                map = TeraRaidMapParent.Paldea;
            }
            else if (IsKitakami)
            {
                map = TeraRaidMapParent.Kitakami;
            }
            else
            {
                map = TeraRaidMapParent.Blueberry;
            }

            int raidDeliveryGroupID = 0;
            List<string> emptyRewardsToShow = new List<string>();
            bool defaultMoveTypeEmojis = false;
            List<MoveTypeEmojiInfo> emptyCustomTypeEmojis = new List<MoveTypeEmojiInfo>();
            int defaultQueuePosition = 0;
            bool defaultIsEvent = false;
            (PK9 pk, Embed embed) = RaidInfoCommand(seedValue, contentType, map, (int)gameProgress, raidDeliveryGroupID,
                                                        emptyRewardsToShow, defaultMoveTypeEmojis, emptyCustomTypeEmojis,
                                                        defaultQueuePosition, defaultIsEvent);

            string teraType = ExtractTeraTypeFromEmbed(embed);
            string[] battlers = GetBattlerForTeraType(teraType);
            RotatingRaidParameters newRandomShinyRaid = new()
            {
                Seed = seedValue,
                Species = Species.None,
                SpeciesForm = pk.Form,
                Title = $"✨ Incursion Shiny Misteriosa ✨",
                AddedByRACommand = true,
                DifficultyLevel = randomDifficultyLevel,
                StoryProgress = (GameProgressEnum)gameProgress,
                CrystalType = crystalType,
                IsShiny = pk.IsShiny,
                PartyPK = battlers.Length > 0 ? battlers : [""]
            };

            // Find the last position of a raid added by the RA command
            int lastRaCommandRaidIndex = Settings.ActiveRaids.FindLastIndex(raid => raid.AddedByRACommand);
            int insertPosition = lastRaCommandRaidIndex != -1 ? lastRaCommandRaidIndex + 1 : RotationCount + 1;

            // Insert the new raid at the determined position
            Settings.ActiveRaids.Insert(insertPosition, newRandomShinyRaid);

            Log($"Añadida Incursión misteriosa - Especies: {(Species)pk.Species}, Semilla: {seedValue}.");
        }

        private static uint GenerateRandomShinySeed()
        {
            Random random = new();
            uint seed;

            do
            {
                // Generate a random uint
                byte[] buffer = new byte[4];
                random.NextBytes(buffer);
                seed = BitConverter.ToUInt32(buffer, 0);
            }
            while (Raidshiny(seed) == 0);

            return seed;
        }

        private static int Raidshiny(uint Seed)
        {
            Xoroshiro128Plus xoroshiro128Plus = new(Seed);
            _ = (uint)xoroshiro128Plus.NextInt(4294967295uL);
            uint num2 = (uint)xoroshiro128Plus.NextInt(4294967295uL);
            uint num3 = (uint)xoroshiro128Plus.NextInt(4294967295uL);
            return (((num3 >> 16) ^ (num3 & 0xFFFF)) >> 4 == ((num2 >> 16) ^ (num2 & 0xFFFF)) >> 4) ? 1 : 0;
        }

        private static string ExtractTeraTypeFromEmbed(Embed embed)
        {
            var statsField = embed.Fields.FirstOrDefault(f => f.Name == "**__Estadísticas__**");
            if (statsField != null)
            {
                var lines = statsField.Value.Split('\n');
                var teraTypeLine = lines.FirstOrDefault(l => l.StartsWith("**Tera Tipo:**"));
                if (teraTypeLine != null)
                {
                    var teraType = teraTypeLine.Split(':')[1].Trim();
                    teraType = teraType.Replace("*", "").Trim();
                    return teraType;
                }
            }
            return "Fairy";
        }

        private string[] GetBattlerForTeraType(string teraType)
        {
            var battlers = Settings.RaidSettings.MysteryRaidsSettings.TeraTypeBattlers;
            return teraType switch
            {
                "Bug" => battlers.BugBattler,
                "Dark" => battlers.DarkBattler,
                "Dragon" => battlers.DragonBattler,
                "Electric" => battlers.ElectricBattler,
                "Fairy" => battlers.FairyBattler,
                "Fighting" => battlers.FightingBattler,
                "Fire" => battlers.FireBattler,
                "Flying" => battlers.FlyingBattler,
                "Ghost" => battlers.GhostBattler,
                "Grass" => battlers.GrassBattler,
                "Ground" => battlers.GroundBattler,
                "Ice" => battlers.IceBattler,
                "Normal" => battlers.NormalBattler,
                "Poison" => battlers.PoisonBattler,
                "Psychic" => battlers.PsychicBattler,
                "Rock" => battlers.RockBattler,
                "Steel" => battlers.SteelBattler,
                "Water" => battlers.WaterBattler,
                _ => []
            };
        }

        private async Task<uint> ReadValue(string fieldName, int size, List<long> pointer, CancellationToken token)
        {
            byte[] valueBytes = await SwitchConnection.PointerPeek(size, pointer, token).ConfigureAwait(false);
            //  Log($"{fieldName} - Read Value: {BitConverter.ToString(valueBytes)}");

            // Determine the byte order based on the field name
            bool isBigEndian = fieldName.Equals("Den ID");

            if (isBigEndian)
            {
                // If the value is in big-endian format, reverse the byte array
                Array.Reverse(valueBytes);
            }

            // Convert the byte array to uint (now in little-endian format)
            return BitConverter.ToUInt32(valueBytes, 0);
        }

        private async Task LogAndUpdateValue(string fieldName, uint value, int size, List<long> pointer, CancellationToken token)
        {
            _ = await SwitchConnection.PointerPeek(size, pointer, token).ConfigureAwait(false);
            // Log($"{fieldName} - Current Value: {BitConverter.ToString(currentValue)}");

            // Determine the byte order based on the field name
            bool isBigEndian = fieldName.Equals("Den ID");

            // Create a new byte array for the new value
            byte[] newValue = new byte[4]; // Assuming uint is 4 bytes
            if (isBigEndian)
            {
                newValue[0] = (byte)(value >> 24); // Most significant byte
                newValue[1] = (byte)(value >> 16);
                newValue[2] = (byte)(value >> 8);
                newValue[3] = (byte)(value);       // Least significant byte
            }
            else
            {
                newValue[0] = (byte)(value);       // Least significant byte
                newValue[1] = (byte)(value >> 8);
                newValue[2] = (byte)(value >> 16);
                newValue[3] = (byte)(value >> 24); // Most significant byte
            }

            await SwitchConnection.PointerPoke(newValue, pointer, token).ConfigureAwait(false);
            _ = await SwitchConnection.PointerPeek(size, pointer, token).ConfigureAwait(false);
            //  Log($"{fieldName} - Updated Value: {BitConverter.ToString(updatedValue)}");
        }

        private static List<long> AdjustPointer(List<long> basePointer, int offset)
        {
            var adjustedPointer = new List<long>(basePointer);
            adjustedPointer[3] += offset; // Adjusting the offset at the 4th index
            return adjustedPointer;
        }

        private List<long> CalculateDirectPointer(int index)
        {
            int blueberrySubtractValue = KitakamiDensCount == 25 ? 94 : 95;

            if (IsKitakami)
            {
                return new List<long>(Offsets.RaidBlockPointerK)
                {
                    [3] = 0xCE8 + ((index - 70) * 0x20)
                };
            }
            else if (IsBlueberry)
            {
                return new List<long>(Offsets.RaidBlockPointerB)
                {
                    [3] = 0x1968 + ((index - blueberrySubtractValue) * 0x20)
                };
            }
            else
            {
                return new List<long>(Offsets.RaidBlockPointerP)
                {
                    [3] = 0x40 + index * 0x20
                };
            }
        }

        private List<long> DeterminePointer(int index)
        {
            int blueberrySubtractValue = KitakamiDensCount == 25 ? 93 : 94;

            if (index < 69)
            {
                return new List<long>(Offsets.RaidBlockPointerP)
                {
                    [3] = 0x60 + index * 0x20
                };
            }
            else if (index < 94)
            {
                return new List<long>(Offsets.RaidBlockPointerK)
                {
                    [3] = 0xCE8 + ((index - 69) * 0x20)
                };
            }
            else
            {
                return new List<long>(Offsets.RaidBlockPointerB)
                {
                    [3] = 0x1968 + ((index - blueberrySubtractValue) * 0x20)
                };
            }
        }

        private async Task SanitizeRotationCount(CancellationToken token)
        {
            try
            {
                await Task.Delay(50, token).ConfigureAwait(false);

                if (Settings.ActiveRaids.Count == 0)
                {
                    Log("ActiveRaids está vacío. Saliendo de SanitizeRotationCount.");
                    RotationCount = 0;
                    return;
                }

                // Normalize RotationCount to be within the range of ActiveRaids
                RotationCount = Math.Max(0, Math.Min(RotationCount, Settings.ActiveRaids.Count - 1));

                // Update RaidUpNext for the next raid
                int nextRaidIndex = FindNextPriorityRaidIndex(RotationCount, Settings.ActiveRaids);
                for (int i = 0; i < Settings.ActiveRaids.Count; i++)
                {
                    Settings.ActiveRaids[i].RaidUpNext = i == nextRaidIndex;
                }

                // Process RA command raids
                if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
                {
                    bool isMysteryRaid = Settings.ActiveRaids[RotationCount].Title.Contains("✨ Incursion Shiny Misteriosa ✨");
                    bool isUserRequestedRaid = !isMysteryRaid && Settings.ActiveRaids[RotationCount].Title.Contains("'s Incursión solicitada");

                    if (isUserRequestedRaid || isMysteryRaid)
                    {
                        Log($"Raid para {Settings.ActiveRaids[RotationCount].Species} se agregó a través del comando RA y se eliminará de la lista de rotación.");
                        Settings.ActiveRaids.RemoveAt(RotationCount);
                        // Adjust RotationCount after removal
                        if (RotationCount >= Settings.ActiveRaids.Count)
                        {
                            RotationCount = 0;
                        }

                        // After a raid is removed, find the new next priority raid and update RaidUpNext
                        nextRaidIndex = FindNextPriorityRaidIndex(RotationCount, Settings.ActiveRaids);
                        for (int i = 0; i < Settings.ActiveRaids.Count; i++)
                        {
                            Settings.ActiveRaids[i].RaidUpNext = i == nextRaidIndex;
                        }
                    }
                    else if (!firstRun)
                    {
                        RotationCount = (RotationCount + 1) % Settings.ActiveRaids.Count;
                    }
                }
                else if (!firstRun)
                {
                    RotationCount = (RotationCount + 1) % Settings.ActiveRaids.Count;
                }

                if (firstRun)
                {
                    firstRun = false;
                }

                if (Settings.RaidSettings.RandomRotation)
                {
                    ProcessRandomRotation();
                    return;
                }

                // Find next priority raid
                int nextPriorityIndex = FindNextPriorityRaidIndex(RotationCount, Settings.ActiveRaids);
                if (nextPriorityIndex != -1)
                {
                    RotationCount = nextPriorityIndex;
                }
                Log($"Siguiente incursión en la lista: {Settings.ActiveRaids[RotationCount].Species}.");
            }
            catch (Exception ex)
            {
                Log($"El índice estaba fuera de rango. Restablecimiento de RotationCount a 0. {ex.Message}");
                RotationCount = 0;
            }
        }

        private int FindNextPriorityRaidIndex(int currentRotationCount, List<RotatingRaidParameters> raids)
        {
            if (raids == null || raids.Count == 0)
            {
                // Handle edge case where raids list is empty or null
                return currentRotationCount;
            }

            int count = raids.Count;

            // First, check for user-requested RA command raids
            for (int i = 0; i < count; i++)
            {
                int index = (currentRotationCount + i) % count;
                RotatingRaidParameters raid = raids[index];

                if (raid.AddedByRACommand && !raid.Title.Contains("✨ Incursion Shiny Misteriosa ✨"))
                {
                    return index; // Prioritize user-requested raids
                }
            }

            // Next, check for Mystery Shiny Raids if enabled
            if (Settings.RaidSettings.MysteryRaids)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = (currentRotationCount + i) % count;
                    RotatingRaidParameters raid = raids[index];

                    if (raid.Title.Contains("✨ Incursion Shiny Misteriosa ✨"))
                    {
                        return index; // Only consider Mystery Shiny Raids after user-requested raids
                    }
                }
            }

            // Return current rotation count if no priority raids are found
            return -1;
        }

        private void ProcessRandomRotation()
        {
            // Turn off RandomRotation if both RandomRotation and MysteryRaid are true
            if (Settings.RaidSettings.RandomRotation && Settings.RaidSettings.MysteryRaids)
            {
                Settings.RaidSettings.RandomRotation = false;
                Log("RandomRotation desactivado debido a que MysteryRaids está activo.");
                return;  // Exit the method as RandomRotation is now turned off
            }

            // Check the remaining raids for any added by the RA command
            for (var i = RotationCount; i < Settings.ActiveRaids.Count; i++)
            {
                if (Settings.ActiveRaids[i].AddedByRACommand)
                {
                    RotationCount = i;
                    Log($"Establecer Recuento de rotación en {RotationCount}");
                    return;  // Exit method as a raid added by RA command was found
                }
            }

            // If no raid added by RA command was found, select a random raid
            var random = new Random();
            RotationCount = random.Next(Settings.ActiveRaids.Count);
            Log($"Establecer Recuento de rotación en {RotationCount}");
        }

        private async Task InjectPartyPk(string battlepk, CancellationToken token)
        {
            var set = new ShowdownSet(battlepk);
            var template = AutoLegalityWrapper.GetTemplate(set);
            PK9 pk = (PK9)HostSAV.GetLegal(template, out _);
            pk.ResetPartyStats();
            var offset = await SwitchConnection.PointerAll(Offsets.BoxStartPokemonPointer, token).ConfigureAwait(false);
            await SwitchConnection.WriteBytesAbsoluteAsync(pk.EncryptedBoxData, offset, token).ConfigureAwait(false);
        }

        private async Task<int> PrepareForRaid(CancellationToken token)
        {
            if (shouldRefreshMap)
            {
                Log("Iniciando el proceso de actualización de mapas...");
                await HardStop().ConfigureAwait(false);
                await Task.Delay(2_000, token).ConfigureAwait(false);
                await Click(B, 3_000, token).ConfigureAwait(false);
                await Click(B, 3_000, token).ConfigureAwait(false);
                await GoHome(Hub.Config, token).ConfigureAwait(false);
                await AdvanceDaySV(token).ConfigureAwait(false);
                await SaveGame(Hub.Config, token).ConfigureAwait(false);
                await RecoverToOverworld(token).ConfigureAwait(false);
                shouldRefreshMap = false;
                if (!token.IsCancellationRequested)
                {
                    Log("Actualización del mapa completada. Reiniciando el bucle principal...");
                    await MainLoop(token).ConfigureAwait(false);
                }
            }

            _ = Settings.ActiveRaids[RotationCount];
            var currentSeed = Settings.ActiveRaids[RotationCount].Seed.ToUpper();

            if (!denHexSeed.Equals(currentSeed, StringComparison.CurrentCultureIgnoreCase))
            {
                seedMismatchCount++;
                Log($"La Raid Den y la semilla actual no coinciden. Recuento de discrepancias: {seedMismatchCount}");

                if (seedMismatchCount >= 2)
                {
                    Log("Las semillas no han coincidido 2 veces seguidas. Actualizando el mapa.");
                    shouldRefreshMap = true;
                    seedMismatchCount = 0;
                    return 2;
                }

                await Task.Delay(4_000, token).ConfigureAwait(false);
                Log("Inyectando la semilla correcta");
                await CloseGame(Hub.Config, token).ConfigureAwait(false);
                await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
                Log("Semilla inyectada con éxito!");
                return 2;
            }
            else
            {
                seedMismatchCount = 0;
            }

            if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
            {
                var user = Settings.ActiveRaids[RotationCount].User;
                var mentionedUsers = Settings.ActiveRaids[RotationCount].MentionedUsers;

                // Determine if the raid is a "Free For All"
                bool isFreeForAll = !Settings.ActiveRaids[RotationCount].IsCoded || EmptyRaid >= Settings.LobbyOptions.EmptyRaidLimit;

                if (!isFreeForAll)
                {
                    try
                    {
                        // Only send the message if it's not a "Free For All"
                        if (user != null)
                        {
                            await user.SendMessageAsync("¡Prepárate! ¡Tu incursión se está preparando ahora!").ConfigureAwait(false);
                        }

                        foreach (var mentionedUser in mentionedUsers)
                        {
                            await mentionedUser.SendMessageAsync($"¡Prepárate! ¡La raid a la que fuiste invitado por {user?.Username ?? "el anfitrión"} está a punto de comenzar!").ConfigureAwait(false);
                        }
                    }
                    catch (Discord.Net.HttpException ex)
                    {
                        // Handle exception (e.g., log the error or send a message to a logging channel)
                        Log($"No se pudo enviar DM al usuario o a los usuarios mencionados. Es posible que tengan los DM desactivados. Excepción: {ex.Message}");
                    }
                }
            }

            Log("Preparando el lobby...");

            if (!await ConnectToOnline(Hub.Config, token))
            {
                return 0;
            }

            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SwitchPartyPokemon(token).ConfigureAwait(false);
            await Task.Delay(1_500, token).ConfigureAwait(false);

            if (!await RecoverToOverworld(token).ConfigureAwait(false))
                return 0;

            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);

            if (!Settings.ActiveRaids[RotationCount].IsCoded || (Settings.ActiveRaids[RotationCount].IsCoded && EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby))
            {
                if (Settings.ActiveRaids[RotationCount].IsCoded && EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                    Log($"Tuvimos {Settings.LobbyOptions.EmptyRaidLimit} incursiones vacías ¡Abriendo esta incursión para todos!");
                await Click(DDOWN, 1_000, token).ConfigureAwait(false);
            }

            await Click(A, 4_000, token).ConfigureAwait(false);
            return 1;
        }

        private async Task SwitchPartyPokemon(CancellationToken token)
        {
            LobbyFiltersCategory settings = new();
            var len = string.Empty;
            foreach (var l in Settings.ActiveRaids[RotationCount].PartyPK)
                len += l;
            if (len.Length > 1 && EmptyRaid == 0)
            {
                Log("Preparando PartyPK Espera un momento");
                await Task.Delay(2_500 + settings.ExtraTimePartyPK, token).ConfigureAwait(false);
                await SetCurrentBox(0, token).ConfigureAwait(false);
                var res = string.Join("\n", Settings.ActiveRaids[RotationCount].PartyPK);
                if (res.Length > 4096)
                    res = res[..4096];
                await InjectPartyPk(res, token).ConfigureAwait(false);

                await Click(X, 2_000, token).ConfigureAwait(false);
                await Click(DRIGHT, 0_500, token).ConfigureAwait(false);
                await SetStick(SwitchStick.LEFT, 0, -32000, 1_000, token).ConfigureAwait(false);
                await SetStick(SwitchStick.LEFT, 0, 0, 0, token).ConfigureAwait(false);
                for (int i = 0; i < 2; i++)
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                await Click(A, 3_500, token).ConfigureAwait(false);
                await Click(Y, 0_500, token).ConfigureAwait(false);
                await Click(DLEFT, 0_800, token).ConfigureAwait(false);
                await Click(Y, 0_500, token).ConfigureAwait(false);
                Log("El cambio del PartyPK se ha realizado correctamente.");
            }
        }

        private async Task<bool> RecoverToOverworld(CancellationToken token)
        {
            if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                return true;

            var attempts = 0;
            while (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
            {
                attempts++;
                if (attempts >= 30)
                    break;
                for (int i = 0; i < 20; i++)
                {
                    await Click(B, 1_000, token).ConfigureAwait(false);
                    if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                        return true;
                }
            }

            // We didn't make it for some reason.
            if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
            {
                Log("No pude volver al mundo exterior, reiniciando el juego");
                return false; // Return false instead of rebooting here
            }
            await Task.Delay(1_000, token).ConfigureAwait(false);
            return true;
        }

        private async Task RollBackHour(CancellationToken token)
        {
            for (int i = 0; i < 2; i++)
                await Click(B, 0_150, token).ConfigureAwait(false);
            Log("Configurando la hora");
            for (int i = 0; i < 2; i++)
                await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(A, 1_250, token).ConfigureAwait(false); // Enter settings

            await PressAndHold(DDOWN, 2_000, 0_250, token).ConfigureAwait(false); // Scroll to system settings
            await Click(A, 1_250, token).ConfigureAwait(false);

            if (Settings.MiscSettings.UseOvershoot)
            {
                await PressAndHold(DDOWN, Settings.MiscSettings.HoldTimeForRollover, 1_000, token).ConfigureAwait(false);
                await Click(DUP, 0_500, token).ConfigureAwait(false);
            }
            else if (!Settings.MiscSettings.UseOvershoot)
            {
                for (int i = 0; i < 39; i++)
                    await Click(DDOWN, 0_100, token).ConfigureAwait(false);
            }

            await Click(A, 1_250, token).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
                await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);

            for (int i = 0; i < 3; i++) // Navigate to the hour setting
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);
            Log("Retrocediendo el tiempo 1 hora");
            for (int i = 0; i < 1; i++) // Roll back the hour by 1
                await Click(DDOWN, 0_200, token).ConfigureAwait(false);

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen
        }

        private async Task RollBackTime(CancellationToken token)
        {
            for (int i = 0; i < 2; i++)
                await Click(B, 0_150, token).ConfigureAwait(false);

            for (int i = 0; i < 2; i++)
                await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(A, 1_250, token).ConfigureAwait(false); // Enter settings

            await PressAndHold(DDOWN, 2_000, 0_250, token).ConfigureAwait(false); // Scroll to system settings
            await Click(A, 1_250, token).ConfigureAwait(false);

            if (Settings.MiscSettings.UseOvershoot)
            {
                await PressAndHold(DDOWN, Settings.MiscSettings.HoldTimeForRollover, 1_000, token).ConfigureAwait(false);
                await Click(DUP, 0_500, token).ConfigureAwait(false);
            }
            else if (!Settings.MiscSettings.UseOvershoot)
            {
                for (int i = 0; i < 39; i++)
                    await Click(DDOWN, 0_100, token).ConfigureAwait(false);
            }

            await Click(A, 1_250, token).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
                await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);

            for (int i = 0; i < 3; i++) // Navigate to the hour setting
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            for (int i = 0; i < 5; i++) // Roll back the hour by 5
                await Click(DDOWN, 0_200, token).ConfigureAwait(false);

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen
        }

        private async Task<bool> GetLobbyReady(bool recovery, CancellationToken token)
        {
            var x = 0;
            Log("Conectando con el lobby...");
            while (!await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                await Click(A, 1_000, token).ConfigureAwait(false);
                x++;
                if (x == 15 && recovery)
                {
                    Log("¡No hay den aquí! Rotando de nuevo.");
                    return false;
                }
                if (x == 45)
                {
                    Log("Falló la conexión con el lobby, reiniciando el juego por si estábamos en batalla/mala conexión.");
                    LobbyError++;
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    Log("¡Intentando reiniciar rutina!");
                    return false;
                }
            }
            return true;
        }

        private async Task<string> GetRaidCode(CancellationToken token)
        {
            var data = await SwitchConnection.PointerPeek(6, Offsets.TeraRaidCodePointer, token).ConfigureAwait(false);
            TeraRaidCode = Encoding.ASCII.GetString(data); // Convert to lowercase for easier reading
            return $"{TeraRaidCode}";
        }

        private async Task<bool> CheckIfTrainerBanned(RaidMyStatus trainer, ulong nid, int player, CancellationToken token)
        {
            RaidTracker.TryAdd(nid, 0);
            var msg = string.Empty;
            var banResultCFW = RaiderBanList.List.FirstOrDefault(x => x.ID == nid);
            bool isBanned = banResultCFW != default;

            if (isBanned)
            {
                msg = $"{banResultCFW!.Name} fue encontrado en la lista de baneos del host.\n{banResultCFW.Comment}";
                Log(msg);
                await CurrentRaidInfo(null, "", false, true, false, false, null, false, token).ConfigureAwait(false);
                await EnqueueEmbed(null, msg, false, true, false, false, token).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private async Task<(bool, List<(ulong, RaidMyStatus)>)> ReadTrainers(CancellationToken token)
        {
            if (!await IsConnectedToLobby(token))
                return (false, new List<(ulong, RaidMyStatus)>());

            Task? delayTask = null;
            TimeSpan wait;

            if (Settings.ActiveRaids[RotationCount].AddedByRACommand &&
                Settings.ActiveRaids[RotationCount].Title != "✨ Incursion Shiny Misteriosa ✨")
            {
                delayTask = Task.Delay(Settings.EmbedToggles.RequestEmbedTime * 1000, token)
                    .ContinueWith(async _ => await EnqueueEmbed(null, "", false, false, false, false, token), token);
                wait = TimeSpan.FromSeconds(160);
            }
            else
            {
                await EnqueueEmbed(null, "", false, false, false, false, token).ConfigureAwait(false);
                wait = TimeSpan.FromSeconds(160);
            }

            List<(ulong, RaidMyStatus)> lobbyTrainers = [];

            var endTime = DateTime.Now + wait;
            bool full = false;

            while (!full && DateTime.Now < endTime)
            {
                if (!await IsConnectedToLobby(token))
                    return (false, lobbyTrainers);

                for (int i = 0; i < 3; i++)
                {
                    var player = i + 2;
                    Log($"Esperando a que cargue el jugador {player}...");

                    if (!await IsConnectedToLobby(token))
                        return (false, lobbyTrainers);

                    var nidOfs = TeraNIDOffsets[i];
                    var data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                    var nid = BitConverter.ToUInt64(data, 0);
                    while (nid == 0 && DateTime.Now < endTime)
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);

                        if (!await IsConnectedToLobby(token))
                            return (false, lobbyTrainers);

                        data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                        nid = BitConverter.ToUInt64(data, 0);
                    }

                    List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                    ptr[2] += i * 0x30;
                    var trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);

                    while (trainer.OT.Length == 0 && DateTime.Now < endTime)
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);

                        if (!await IsConnectedToLobby(token))
                            return (false, lobbyTrainers);

                        trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);
                    }

                    if (nid != 0 && !string.IsNullOrWhiteSpace(trainer.OT))
                    {
                        if (await CheckIfTrainerBanned(trainer, nid, player, token).ConfigureAwait(false))
                            return (false, lobbyTrainers);
                    }

                    if (lobbyTrainers.Any(x => x.Item1 == nid))
                    {
                        Log($"NID duplicado detectado: {nid}. Omitiendo...");
                        continue;
                    }

                    if (nid > 0 && trainer.OT.Length > 0)
                        lobbyTrainers.Add((nid, trainer));

                    full = lobbyTrainers.Count == 3;
                    if (full)
                    {
                        List<string> trainerNames = lobbyTrainers.Select(t => t.Item2.OT).ToList();
                        await CurrentRaidInfo(trainerNames, "", false, false, false, false, null, true, token).ConfigureAwait(false);
                    }

                    if (full || DateTime.Now >= endTime)
                        break;
                }
            }


            if (delayTask != null)
            {
                await delayTask;
            }

            if (lobbyTrainers.Count == 0)
            {
                EmptyRaid++;
                LostRaid++;
                Log($"Nadie se unió a la incursión, recuperando...");
                if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                    Log($"Recuento de incursiones vacías #{EmptyRaid}");
                if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipRaid)
                    Log($"Lobbies perdidos/vacíos: {LostRaid}/{Settings.LobbyOptions.SkipRaidLimit}");

                return (false, lobbyTrainers);
            }

            RaidCount++;
            Log($"¡La incursión #{RaidCount} está comenzando!");
            if (EmptyRaid != 0)
                EmptyRaid = 0;
            return (true, lobbyTrainers);
        }

        private async Task<bool> IsConnectedToLobby(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesMainAsync(Offsets.TeraLobbyIsConnected, 1, token).ConfigureAwait(false);
            return data[0] != 0x00; // 0 when in lobby but not connected
        }

        private async Task<bool> IsInRaid(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesMainAsync(Offsets.LoadedIntoDesiredState, 1, token).ConfigureAwait(false);
            return data[0] == 0x02; // 2 when in raid, 1 when not
        }

        private async Task AdvanceDaySV(CancellationToken token)
        {
            var scrollroll = Settings.MiscSettings.DateTimeFormat switch
            {
                DTFormat.DDMMYY => 0,
                DTFormat.YYMMDD => 2,
                _ => 1,
            };

            for (int i = 0; i < 2; i++)
                await Click(B, 0_150, token).ConfigureAwait(false);

            for (int i = 0; i < 2; i++)
                await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(A, 1_250, token).ConfigureAwait(false); // Enter settings

            await PressAndHold(DDOWN, 2_000, 0_250, token).ConfigureAwait(false); // Scroll to system settings
            await Click(A, 1_250, token).ConfigureAwait(false);

            if (Settings.MiscSettings.UseOvershoot)
            {
                await PressAndHold(DDOWN, Settings.MiscSettings.HoldTimeForRollover, 1_000, token).ConfigureAwait(false);
                await Click(DUP, 0_500, token).ConfigureAwait(false);
            }
            else if (!Settings.MiscSettings.UseOvershoot)
            {
                for (int i = 0; i < 39; i++)
                    await Click(DDOWN, 0_100, token).ConfigureAwait(false);
            }

            await Click(A, 1_250, token).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
                await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);
            for (int i = 0; i < scrollroll; i++) // 0 to roll day for DDMMYY, 1 to roll day for MMDDYY, 3 to roll hour
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(DUP, 0_200, token).ConfigureAwait(false); // Advance a day

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen

            await Click(A, 0_200, token).ConfigureAwait(false); // Back in Game
        }

        private async Task RolloverCorrectionSV(CancellationToken token)
        {
            var scrollroll = Settings.MiscSettings.DateTimeFormat switch
            {
                DTFormat.DDMMYY => 0,
                DTFormat.YYMMDD => 2,
                _ => 1,
            };

            for (int i = 0; i < 2; i++)
                await Click(B, 0_150, token).ConfigureAwait(false);

            for (int i = 0; i < 2; i++)
                await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(A, 1_250, token).ConfigureAwait(false); // Enter settings

            await PressAndHold(DDOWN, 2_000, 0_250, token).ConfigureAwait(false); // Scroll to system settings
            await Click(A, 1_250, token).ConfigureAwait(false);

            if (Settings.MiscSettings.UseOvershoot)
            {
                await PressAndHold(DDOWN, Settings.MiscSettings.HoldTimeForRollover, 1_000, token).ConfigureAwait(false);
                await Click(DUP, 0_500, token).ConfigureAwait(false);
            }
            else if (!Settings.MiscSettings.UseOvershoot)
            {
                for (int i = 0; i < 39; i++)
                    await Click(DDOWN, 0_100, token).ConfigureAwait(false);
            }

            await Click(A, 1_250, token).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
                await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);
            for (int i = 0; i < scrollroll; i++) // 0 to roll day for DDMMYY, 1 to roll day for MMDDYY, 3 to roll hour
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(DDOWN, 0_200, token).ConfigureAwait(false);

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen
        }

        private async Task RegroupFromBannedUser(CancellationToken token)
        {
            Log("Intentando rehacer el lobby..");
            await Click(B, 2_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(B, 1_000, token).ConfigureAwait(false);
            while (!await IsOnOverworld(OverworldOffset, token))
            {
                for (int i = 0; i < 8; i++)
                    await Click(B, 1000, token);
            }
        }

        private async Task InitializeSessionOffsets(CancellationToken token)
        {
            Log("Caching session offsets...");
            OverworldOffset = await SwitchConnection.PointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
            ConnectedOffset = await SwitchConnection.PointerAll(Offsets.IsConnectedPointer, token).ConfigureAwait(false);
            RaidBlockPointerP = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerP, token).ConfigureAwait(false);
            RaidBlockPointerK = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerK, token).ConfigureAwait(false);
            RaidBlockPointerB = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerB, token).ConfigureAwait(false);
            sbyte FieldID = await ReadEncryptedBlockByte(RaidDataBlocks.KPlayerCurrentFieldID, token).ConfigureAwait(false);
            string regionName = FieldID switch
            {
                0 => "Paldea",
                1 => "Kitakami",
                2 => "Blueberry",
                _ => "Unknown"
            };
            Log($"Jugador en la región: {regionName}");
            if (regionName == "Kitakami")
            {
                IsKitakami = true;
            }
            else if (regionName == "Blueberry")
            {
                IsBlueberry = true;
            }
            if (firstRun)
            {
                GameProgress = await ReadGameProgress(token).ConfigureAwait(false);
                Log($"Progreso actual del juego identificado como {GameProgress}.");
                currentSpawnsEnabled = (bool?)await ReadBlock(RaidDataBlocks.KWildSpawnsEnabled, CancellationToken.None);
            }

            var nidPointer = new long[] { Offsets.LinkTradePartnerNIDPointer[0], Offsets.LinkTradePartnerNIDPointer[1], Offsets.LinkTradePartnerNIDPointer[2] };
            for (int p = 0; p < TeraNIDOffsets.Length; p++)
            {
                nidPointer[2] = Offsets.LinkTradePartnerNIDPointer[2] + p * 0x8;
                TeraNIDOffsets[p] = await SwitchConnection.PointerAll(nidPointer, token).ConfigureAwait(false);
            }
            Log("¡Offsets de caché completos!");
        }

        private static async Task<bool> IsValidImageUrlAsync(string url)
        {
            using var httpClient = new HttpClient();
            try
            {
                var response = await httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException ex) when (ex.InnerException is WebException webEx && webEx.Status == WebExceptionStatus.TrustFailure)
            {
            }
            catch (Exception)
            {
            }
            return false;
        }

        private readonly Dictionary<string, string> TypeAdvantages = new()
        {
            { "normal", "<:Fighting:1134573062881300551>Lucha" },
            { "fire", "<:Water:1134575004038742156>Agua, <:Ground:1134573701766058095>Tierra, <:Rock:1134574024542912572>Roca" },
            { "water", "<:Electric:1134576561991995442>Eléctrico, <:Grass:1134574800057139331>Planta" },
            { "grass", "<:Flying:1134573296734711918>Volador, <:Poison:1134575188403564624>Veneno, <:Bug:1134574602908073984>Bicho, <:Fire:1134576993799766197>Fuego, <:Ice:1134576183787409531>Hielo" },
            { "electric", "<:Ground:1134573701766058095>Tierra" },
            { "ice", "<:Fighting:1134573062881300551>Lucha, <:Rock:1134574024542912572>Roca, <:Steel:1134576384191254599>Acero, <:Fire:1134576993799766197>Fuego" },
            { "fighting", "<:Flying:1134573296734711918>Volador, <:Psychic:1134576746298089575>Psíquico, <:Fairy:1134575841523814470>Hada" },
            { "poison", "<:Ground:1134573701766058095>Tierra, <:Psychic:1134576746298089575>Psíquico" },
            { "ground", "<:Water:1134575004038742156>Agua, <:Ice:1134576183787409531>Hielo, <:Grass:1134574800057139331>Planta" },
            { "flying", "<:Rock:1134574024542912572>Roca, <:Electric:1134576561991995442>Eléctrico, <:Ice:1134576183787409531>Hielo" },
            { "psychic", "<:Bug:1134574602908073984>Bicho, <:Ghost:1134574276628975626>Fantasma, <:Dark:1134575488598294578>Siniestro" },
            { "bug", "<:Flying:1134573296734711918>Volador, <:Rock:1134574024542912572>Roca, <:Fire:1134576993799766197>Fuego" },
            { "rock", "<:Fighting:1134573062881300551>Lucha, <:Ground:1134573701766058095>Tierra, <:Steel:1134576384191254599>Acero, <:Water:1134575004038742156>Agua, <:Grass:1134574800057139331>Planta" },
            { "ghost", "<:Ghost:1134574276628975626>Fantasma, <:Dark:1134575488598294578>Siniestro" },
            { "dragon", "<:Ice:1134576183787409531>Hielo, <:Dragon:1134576015973294221>Dragón, <:Fairy:1134575841523814470>Hada" },
            { "dark", "<:Fighting:1134573062881300551>Lucha, <:Bug:1134574602908073984>Bicho, <:Fairy:1134575841523814470>Hada" },
            { "steel", "<:Fighting:1134573062881300551>Lucha, <:Ground:1134573701766058095>Tierra, <:Fire:1134576993799766197>Fuego" },
            { "fairy", "<:Poison:1134575188403564624>Veneno, <:Steel:1134576384191254599>Acero" }
        };
        private static readonly char[] separator = [','];
        private static readonly char[] separatorArray = ['-'];

        private string GetTypeAdvantage(string teraType)
        {
            // Check if the type exists in the dictionary and return the corresponding advantage
            if (TypeAdvantages.TryGetValue(teraType.ToLower(), out string advantage))
            {
                return advantage;
            }
            return "Tipo desconocido";  // Return "Unknown Type" if the type doesn't exist in our dictionary
        }

        private async Task<byte[]?> CaptureGifScreenshotsAsync(CancellationToken token)
        {
            var frameCount = Settings.EmbedToggles.Frames;
            var gifFrames = new List<System.Drawing.Image>();
            var gifWidth = 450;
            var gifHeight = 270;
            var gifQuality = (AnimatedGif.GifQuality)Settings.EmbedToggles.GifQuality;
            var frameDelay = 180;

            for (int i = 0; i < frameCount; i++)
            {
                byte[] bytes;
                try
                {
                    bytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                }
                catch (Exception ex)
                {
                    Log($"Error al obtener píxeles: {ex.Message}");
                    return null;
                }

                if (bytes.Length == 0)
                {
                    Log("No se recibieron datos de cuadro.");
                    return null;
                }

                using var ms = new MemoryStream(bytes);
                using var bitmap = new Bitmap(ms);
                var resizedFrame = bitmap.GetThumbnailImage(gifWidth, gifHeight, null, IntPtr.Zero);
                var frame = ((Bitmap)resizedFrame).Clone(new Rectangle(0, 0, resizedFrame.Width, resizedFrame.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                gifFrames.Add(frame);
                resizedFrame.Dispose();

                await Task.Delay(50, token);
            }

            using var outputMs = new MemoryStream();
            using (var gif = new AnimatedGifCreator(outputMs, frameDelay))
            {
                foreach (var frame in gifFrames)
                {
                    gif.AddFrame(frame, quality: (AnimatedGif.GifQuality)(int)gifQuality);
                    frame.Dispose();
                }
            }

            return outputMs.ToArray();
        }

        private async Task EnqueueEmbed(List<string>? names, string message, bool hatTrick, bool disband, bool upnext, bool raidstart, CancellationToken token)
        {
            string code = string.Empty;

            // Determine if the raid is a "Free For All" based on the settings and conditions
            if (Settings.ActiveRaids[RotationCount].IsCoded && EmptyRaid < Settings.LobbyOptions.EmptyRaidLimit)
            {
                // If it's not a "Free For All", retrieve the raid code
                code = await GetRaidCode(token).ConfigureAwait(false);
            }
            else
            {
                // If it's a "Free For All", set the code as such
                code = "Libre para todos";
            }

            // Description can only be up to 4096 characters.
            //var description = Settings.ActiveRaids[RotationCount].Description.Length > 0 ? string.Join("\n", Settings.ActiveRaids[RotationCount].Description) : "";
            var description = Settings.EmbedToggles.RaidEmbedDescription.Length > 0 ? string.Join("\n", Settings.EmbedToggles.RaidEmbedDescription) : "";
            if (description.Length > 4096) description = description[..4096];

            if (EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                EmptyRaid = 0;

            if (disband) // Wait for trainer to load before disband
                await Task.Delay(5_000, token).ConfigureAwait(false);

            byte[]? imageBytes = null;
            string fileName = string.Empty;

            if (!disband && names is not null && !upnext && Settings.EmbedToggles.TakeScreenshot)
            {
                try
                {
                    if (Settings.EmbedToggles.AnimatedScreenshot)
                    {
                        try
                        {
                            imageBytes = await Task.Run(() => CaptureGifScreenshotsAsync(token)).ConfigureAwait(false);
                            fileName = $"raidecho{RotationCount}.gif";
                        }
                        catch (Exception ex)
                        {
                            Log($"Error al capturar capturas de pantalla de GIF: {ex.Message}");
                            Log("Volviendo a la captura de pantalla JPG estándar.");

                            imageBytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                            fileName = $"raidecho{RotationCount}.jpg";
                        }
                    }
                    else
                    {
                        imageBytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                        fileName = $"raidecho{RotationCount}.jpg";
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error al capturar capturas de pantalla: {ex.Message}");
                }
            }
            else if (Settings.EmbedToggles.TakeScreenshot && !upnext)
            {
                try
                {
                    imageBytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                    fileName = $"raidecho{RotationCount}.jpg";
                }
                catch (Exception ex)
                {
                    Log($"Error al obtener píxeles: {ex.Message}");
                }
            }

            string disclaimer = Settings.ActiveRaids.Count > 1
                                ? Settings.EmbedToggles.CustomRaidRotationMessage
                                : "";

            var turl = string.Empty;
            var form = string.Empty;

            Log($"Recuento de rotaciones: {RotationCount} | La especie es {Settings.ActiveRaids[RotationCount].Species}");
            if (!disband && !upnext && !raidstart)
                Log($"El código de incursión es: {code}");
            PK9 pk = new()
            {
                Species = (ushort)Settings.ActiveRaids[RotationCount].Species,
                Form = (byte)Settings.ActiveRaids[RotationCount].SpeciesForm
            };
            if (pk.Form != 0)
                form = $"-{pk.Form}";
            if (Settings.ActiveRaids[RotationCount].IsShiny == true)
                pk.SetIsShiny(true);
            else
                pk.SetIsShiny(false);

            if (Settings.ActiveRaids[RotationCount].SpriteAlternateArt && Settings.ActiveRaids[RotationCount].IsShiny)
            {
                var altUrl = AltPokeImg(pk);

                try
                {
                    // Check if AltPokeImg URL is valid
                    if (await IsValidImageUrlAsync(altUrl))
                    {
                        turl = altUrl;
                    }
                    else
                    {
                        Settings.ActiveRaids[RotationCount].SpriteAlternateArt = false;  // Set SpriteAlternateArt to false if no img found
                        turl = RaidExtensions<PK9>.PokeImg(pk, false, false);
                        Log($"La URL de AltPokeImg no era válida. Establecer SpriteAlternateArt a false.");
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception and use the default sprite
                    Log($"Error al validar la URL de la imagen alternativa: {ex.Message}");
                    Settings.ActiveRaids[RotationCount].SpriteAlternateArt = false;  // Set SpriteAlternateArt to false due to error
                    turl = RaidExtensions<PK9>.PokeImg(pk, false, false);
                }
            }
            else
            {
                turl = RaidExtensions<PK9>.PokeImg(pk, false, false);
            }

            if (Settings.ActiveRaids[RotationCount].Species is 0)
                turl = "https://i.imgur.com/477FEAI.png";

            // Fetch the dominant color from the image only AFTER turl is assigned
            (int R, int G, int B) dominantColor = Task.Run(() => RaidExtensions<PK9>.GetDominantColorAsync(turl)).Result;

            // Use the dominant color, unless it's a disband or hatTrick situation
            var embedColor = disband ? Discord.Color.Red : hatTrick ? Discord.Color.Purple : new Discord.Color(dominantColor.R, dominantColor.G, dominantColor.B);

            TimeSpan duration = new(0, 2, 31);

            // Calculate the future time by adding the duration to the current time
            DateTimeOffset futureTime = DateTimeOffset.Now.Add(duration);

            // Convert the future time to Unix timestamp
            long futureUnixTime = futureTime.ToUnixTimeSeconds();

            // Create the future time message using Discord's timestamp formatting
            string futureTimeMessage = $"**Publicando la proxima Raid en: <t:{futureUnixTime}:R>**";

            // Initialize the EmbedBuilder object
            var embed = new EmbedBuilder()
            {
                Title = disband ? $"**Incursión cancelada: [{TeraRaidCode}]**" : upnext && Settings.RaidSettings.TotalRaidsToHost != 0 ? $"Incursión finalizada - Preparando la siguiente!" : upnext && Settings.RaidSettings.TotalRaidsToHost == 0 ? $"Incursión finalizada - Preparando la siguiente!" : "",
                Color = embedColor,
                Description = disband ? message : upnext ? Settings.RaidSettings.TotalRaidsToHost == 0 ? $"# {Settings.ActiveRaids[RotationCount].Title}\n\n{futureTimeMessage}" : $"# {Settings.ActiveRaids[RotationCount].Title}\n\n{futureTimeMessage}" : raidstart ? "" : description,
                ThumbnailUrl = upnext ? turl : (imageBytes == null ? turl : null), // Set ThumbnailUrl based on upnext and imageBytes
                ImageUrl = imageBytes != null ? $"attachment://{fileName}" : null, // Set ImageUrl based on imageBytes
            };

            if (!raidstart && !upnext && code != "Free For All")
                await CurrentRaidInfo(null, code, false, false, false, false, turl, false, token).ConfigureAwait(false);

            // Only include footer if not posting 'upnext' embed with the 'Preparing Raid' title
            if (!(upnext && Settings.RaidSettings.TotalRaidsToHost == 0))
            {
                string programIconUrl = $"https://i.imgur.com/MdimOfV.png";
                int raidsInRotationCount = Hub.Config.RotatingRaidSV.ActiveRaids.Count(r => !r.AddedByRACommand);
                // Calculate uptime
                TimeSpan uptime = DateTime.Now - StartTime;

                // Check for singular or plural days/hours
                string dayLabel = uptime.Days == 1 ? "dia" : "dias";
                string hourLabel = uptime.Hours == 1 ? "hora" : "horas";
                string minuteLabel = uptime.Minutes == 1 ? "minuto" : "minutos";

                // Format the uptime string, omitting the part if the value is 0
                string uptimeFormatted = "";
                if (uptime.Days > 0)
                {
                    uptimeFormatted += $"{uptime.Days} {dayLabel} ";
                }
                if (uptime.Hours > 0 || uptime.Days > 0) // Show hours if there are any hours, or if there are days even if hours are 0
                {
                    uptimeFormatted += $"{uptime.Hours} {hourLabel} ";
                }
                if (uptime.Minutes > 0 || uptime.Hours > 0 || uptime.Days > 0) // Show minutes if there are any minutes, or if there are hours/days even if minutes are 0
                {
                    uptimeFormatted += $"{uptime.Minutes} {minuteLabel}";
                }

                // Trim any excess whitespace from the string
                uptimeFormatted = uptimeFormatted.Trim();
                embed.WithFooter(new EmbedFooterBuilder()
                {
                    Text = $"Incursiones Completadas: {RaidCount} (Ganadas: {WinCount} | Perdidas: {LossCount})\nRaids Activas:: {raidsInRotationCount} | Tiempo de actividad: {uptimeFormatted}\n" + disclaimer,
                    IconUrl = programIconUrl
                });
            }

            // Prepare the tera icon URL
            string teraType = RaidEmbedInfoHelpers.RaidSpeciesTeraType.ToLower();
            string folderName = Settings.EmbedToggles.SelectedTeraIconType == TeraIconType.Icon1 ? "icon1" : "icon2"; // Add more conditions for more icon types
            string teraIconUrl = $"https://raw.githubusercontent.com/bdawg1989/sprites/main/teraicons/{folderName}/{teraType}.png";

            // Only include author (header) if not posting 'upnext' embed with the 'Preparing Raid' title
            if (!(upnext && Settings.RaidSettings.TotalRaidsToHost == 0))
            {
                // Set the author (header) of the embed with the tera icon
                embed.WithAuthor(new EmbedAuthorBuilder()
                {
                    Name = RaidEmbedInfoHelpers.RaidEmbedTitle,
                    IconUrl = teraIconUrl
                });
            }
            if (!disband && !upnext && !raidstart)
            {
                StringBuilder statsField = new();
                statsField.AppendLine($"**Nivel**: {RaidEmbedInfoHelpers.RaidLevel}");
                statsField.AppendLine($"**Genero**: {RaidEmbedInfoHelpers.RaidSpeciesGender}");
                statsField.AppendLine($"**Naturaleza**: {RaidEmbedInfoHelpers.RaidSpeciesNature}");
                statsField.AppendLine($"**Habilidad**: {RaidEmbedInfoHelpers.RaidSpeciesAbility}");
                statsField.AppendLine($"**IVs**: {RaidEmbedInfoHelpers.RaidSpeciesIVs}");
                statsField.AppendLine($"**Tamaño**: {RaidEmbedInfoHelpers.ScaleText}({RaidEmbedInfoHelpers.ScaleNumber})");

                if (Settings.EmbedToggles.IncludeSeed)
                {
                    var storyProgressValue = Settings.ActiveRaids[RotationCount].StoryProgress switch
                    {
                        GameProgressEnum.Unlocked6Stars => 6,
                        GameProgressEnum.Unlocked5Stars => 5,
                        GameProgressEnum.Unlocked4Stars => 4,
                        GameProgressEnum.Unlocked3Stars => 3,
                        _ => 6,
                    };
                    statsField.AppendLine($"**Semilla**: `{Settings.ActiveRaids[RotationCount].Seed} {Settings.ActiveRaids[RotationCount].DifficultyLevel} {storyProgressValue}`");
                }

                embed.AddField("**__Estadísticas__**", statsField.ToString(), true);
                embed.AddField("\u200b", "\u200b", true);
            }

            if (!disband && !upnext && !raidstart && Settings.EmbedToggles.IncludeMoves)
            {
                embed.AddField("**__Movimientos__**", string.IsNullOrEmpty($"{RaidEmbedInfoHelpers.ExtraMoves}") ? string.IsNullOrEmpty($"{RaidEmbedInfoHelpers.Moves}") ? "No hay movimientos para mostrar" : $"{RaidEmbedInfoHelpers.Moves}" : $"{RaidEmbedInfoHelpers.Moves}\n**Movimientos Extras:**\n{RaidEmbedInfoHelpers.ExtraMoves}", true);
                RaidEmbedInfoHelpers.ExtraMoves = string.Empty;
            }

            if (!disband && !upnext && !raidstart && !Settings.EmbedToggles.IncludeMoves)
            {
                embed.AddField(" **__Recompenzas Especiales__**", string.IsNullOrEmpty($"{RaidEmbedInfoHelpers.SpecialRewards}") ? "No Rewards To Display" : $"{RaidEmbedInfoHelpers.SpecialRewards}", true);
                RaidEmbedInfoHelpers.SpecialRewards = string.Empty;
            }
            // Fetch the type advantage using the static RaidSpeciesTeraType from RaidEmbedInfo
            string typeAdvantage = GetTypeAdvantage(RaidEmbedInfoHelpers.RaidSpeciesTeraType);

            // Only include the Type Advantage if not posting 'upnext' embed with the 'Preparing Raid' title and if the raid isn't starting or disbanding
            if (!disband && !upnext && !raidstart && Settings.EmbedToggles.IncludeTypeAdvantage)
            {
                embed.AddField(" **__Ventaja por Tipo__**", typeAdvantage, true);
                embed.AddField("\u200b", "\u200b", true);
            }

            if (!disband && !upnext && !raidstart && Settings.EmbedToggles.IncludeMoves)
            {
                embed.AddField(" **__Recompenzas Especiales__**", string.IsNullOrEmpty($"{RaidEmbedInfoHelpers.SpecialRewards}") ? "No hay recompensas para mostrar" : $"{RaidEmbedInfoHelpers.SpecialRewards}", true);
                RaidEmbedInfoHelpers.SpecialRewards = string.Empty;
            }
            if (!disband && names is null && !upnext)
            {
                embed.AddField(Settings.EmbedToggles.IncludeCountdown ? $"**__Raid Comenzando__**:\n**<t:{DateTimeOffset.Now.ToUnixTimeSeconds() + 160}:R>**" : $"**Esperando en el lobby!**", $"Raid Code: **{code}**", true);
            }
            if (!disband && names is not null && !upnext)
            {
                var players = string.Empty;
                if (names.Count == 0)
                    players = "Nuestro grupo no lo logro. :/";
                else
                {
                    int i = 2;
                    names.ForEach(x =>
                    {
                        players += $"Jugador {i} - **{x}**\n";
                        i++;
                    });
                }

                embed.AddField($"**¡La incursión #{RaidCount} está comenzando!**", players);
            }
            if (imageBytes != null)
            {
                embed.ThumbnailUrl = turl;
                embed.WithImageUrl($"attachment://{fileName}");
            }
            EchoUtil.RaidEmbed(imageBytes, fileName, embed);
        }

        private static string CleanEmojiStrings(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            return Regex.Replace(input, @"<:[a-zA-Z0-9_]+:[0-9]+>", "").Trim();
        }

        private async Task CurrentRaidInfo(List<string>? names, string code, bool hatTrick, bool disband, bool upnext, bool raidstart, string? imageUrl, bool lobbyFull, CancellationToken token)
        {
            if (!Settings.RaidSettings.JoinSharedRaidsProgram)
                return;
            var raidInfo = new
            {
                RaidEmbedTitle = CleanEmojiStrings(RaidEmbedInfoHelpers.RaidEmbedTitle),
                RaidSpecies = RaidEmbedInfoHelpers.RaidSpecies.ToString(),
                RaidEmbedInfoHelpers.RaidSpeciesForm,
                RaidSpeciesGender = CleanEmojiStrings(RaidEmbedInfoHelpers.RaidSpeciesGender),
                RaidEmbedInfoHelpers.RaidLevel,
                RaidEmbedInfoHelpers.RaidSpeciesIVs,
                RaidEmbedInfoHelpers.RaidSpeciesAbility,
                RaidEmbedInfoHelpers.RaidSpeciesNature,
                RaidEmbedInfoHelpers.RaidSpeciesTeraType,
                Moves = CleanEmojiStrings(RaidEmbedInfoHelpers.Moves),
                ExtraMoves = CleanEmojiStrings(RaidEmbedInfoHelpers.ExtraMoves),
                RaidEmbedInfoHelpers.ScaleText,
                SpecialRewards = CleanEmojiStrings(RaidEmbedInfoHelpers.SpecialRewards),
                RaidEmbedInfoHelpers.ScaleNumber,
                Names = names,
                Code = code,
                HatTrick = hatTrick,
                Disband = disband,
                UpNext = upnext,
                RaidStart = raidstart,
                ImageUrl = imageUrl,
                LobbyFull = lobbyFull
            };

            try
            {
                var json = JsonConvert.SerializeObject(raidInfo, Formatting.Indented);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string raidinfo = Encoding.UTF8.GetString(Convert.FromBase64String("aHR0cHM6Ly9nZW5wa20uY29tL3JhaWRzL3JhaWRfYXBpLnBocA=="));
                var response = await httpClient.PostAsync(raidinfo, content, token);
            }
            catch
            {
            }
        }

        private async Task<bool> ConnectToOnline(PokeRaidHubConfig config, CancellationToken token)
        {
            int attemptCount = 0;
            const int maxAttempt = 5;
            const int waitTime = 10; // time in minutes to wait after max attempts

            while (true) // Loop until a successful connection is made or the task is canceled
            {
                if (token.IsCancellationRequested)
                {
                    Log("Intento de conexión cancelado.");
                    break;
                }
                try
                {
                    if (await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
                    {
                        Log("Conexión establecida con éxito.");
                        break; // Exit the loop if connected successfully
                    }

                    if (attemptCount >= maxAttempt)
                    {
                        Log($"No se ha podido conectar después de {maxAttempt} intentos. Asumiendo un softban. Iniciando espera de {waitTime} minutos antes de reintentar.");
                        // Log details about sending an embed message
                        Log("Enviando un embed para notificar dificultades técnicas.");
                        EmbedBuilder embed = new()
                        {
                            Title = "Experimentando dificultades técnicas",
                            Description = "El bot está experimentando problemas para conectarse en línea. Por favor, espere mientras intentamos resolver el problema.",
                            Color = Discord.Color.Red,
                            ThumbnailUrl = "https://i.imgur.com/ShtjFsE.png"
                        };
                        EchoUtil.RaidEmbed(null, "", embed);
                        // Waiting process
                        await Click(B, 0_500, token).ConfigureAwait(false);
                        await Click(B, 0_500, token).ConfigureAwait(false);
                        Log($"Esperando {waitTime} minutos antes de intentar reconectar.");
                        await Task.Delay(TimeSpan.FromMinutes(waitTime), token).ConfigureAwait(false);
                        Log("Intentando reabrir el juego.");
                        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                        attemptCount = 0; // Reset attempt count
                    }

                    attemptCount++;
                    Log($"Intento {attemptCount} de {maxAttempt}: Intentando conectar en línea...");

                    // Connection attempt logic
                    await Click(X, 3_000, token).ConfigureAwait(false);
                    await Click(L, 5_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);

                    // Wait a bit before rechecking the connection status
                    await Task.Delay(5000, token).ConfigureAwait(false); // Wait 5 seconds before rechecking

                    if (attemptCount < maxAttempt)
                    {
                        Log("Revisando el estado de la conexión en línea...");
                        // Wait and recheck logic
                        await Click(B, 0_500, token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Ocurrió una excepción durante el intento de conexión: {ex.Message}");
                    // Handle exceptions, like connectivity issues here

                    if (attemptCount >= maxAttempt)
                    {
                        Log($"No se ha podido conectar después de {maxAttempt} intentos debido a una excepción. Esperando {waitTime} minutos antes de reintentar.");
                        await Task.Delay(TimeSpan.FromMinutes(waitTime), token).ConfigureAwait(false);
                        Log("Intentando reabrir el juego.");
                        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                        attemptCount = 0;
                    }
                }
            }

            // Final steps after connection is established
            await Task.Delay(3_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Task.Delay(3_000, token).ConfigureAwait(false);

            return true;
        }

        public async Task StartGameRaid(PokeRaidHubConfig config, CancellationToken token)
        {
            // First, check if the time rollback feature is enabled
            if (Settings.RaidSettings.EnableTimeRollBack && DateTime.Now - TimeForRollBackCheck >= TimeSpan.FromHours(5))
            {
                Log("Retrocediendo el tiempo 5 horas.");
                // Call the RollBackTime function
                await RollBackTime(token).ConfigureAwait(false);
                await Click(A, 1_500, token).ConfigureAwait(false);
                // Reset TimeForRollBackCheck
                TimeForRollBackCheck = DateTime.Now;
            }

            var timing = config.Timings;
            var loadPro = timing.RestartGameSettings.ProfileSelectSettings.ProfileSelectionRequired ? timing.RestartGameSettings.ProfileSelectSettings.ExtraTimeLoadProfile : 0;

            await Click(A, 1_000 + loadPro, token).ConfigureAwait(false); // Initial "A" Press to start the Game + a delay if needed for profiles to load

            // Really Shouldn't keep this but we will for now
            if (timing.RestartGameSettings.AvoidSystemUpdate)
            {
                await Task.Delay(0_500, token).ConfigureAwait(false); // Delay bc why not
                await Click(DUP, 0_600, token).ConfigureAwait(false); // Highlight "Start Software"
                await Click(A, 1_000 + loadPro, token).ConfigureAwait(false); // Select "Sttart Software" + delay if Profile selection is needed
            }

            // Only send extra Presses if we need to
            if (timing.RestartGameSettings.ProfileSelectSettings.ProfileSelectionRequired)
            {
                await Click(A, 1_000, token).ConfigureAwait(false); // Now we are on the Profile Screen
                await Click(A, 1_000, token).ConfigureAwait(false); // Select the profile
            }

            // Digital game copies take longer to load
            if (timing.RestartGameSettings.CheckGameDelay)
            {
                await Task.Delay(2_000 + timing.RestartGameSettings.ExtraTimeCheckGame, token).ConfigureAwait(false);
            }

            // If they have DLC on the system and can't use it, requires an UP + A to start the game.
            if (timing.RestartGameSettings.CheckForDLC)
            {
                await Click(DUP, 0_600, token).ConfigureAwait(false);
                await Click(A, 0_600, token).ConfigureAwait(false);
            }

            Log("¡Reiniciando el juego!");

            await Task.Delay(19_000 + timing.RestartGameSettings.ExtraTimeLoadGame, token).ConfigureAwait(false); // Wait for the game to load before writing to memory
            await InitializeRaidBlockPointers(token);

            if (Settings.ActiveRaids.Count > 1)
            {
                Log($"Se ha encontrado la rotación para {Settings.ActiveRaids[RotationCount].Species}.");
                Log($"Revisando el nivel de progreso actual del juego");

                var desiredProgress = Settings.ActiveRaids[RotationCount].StoryProgress;
                if (GameProgress != (GameProgress)desiredProgress)
                {
                    Log($"Actualizando el nivel de progreso del juego a: {desiredProgress}");
                    await WriteProgressLive((GameProgress)desiredProgress).ConfigureAwait(false);
                    GameProgress = (GameProgress)desiredProgress;
                    Log($"Listo.");
                }
                else
                {
                    Log($"El nivel de progreso del juego ya es {GameProgress}. No es necesario actualizar.");
                }

                RaidDataBlocks.AdjustKWildSpawnsEnabledType(Settings.RaidSettings.DisableOverworldSpawns);

                if (Settings.RaidSettings.DisableOverworldSpawns)
                {
                    Log("Revisando el estado actual de las apariciones en el Overworld");
                    if (currentSpawnsEnabled.HasValue)
                    {
                        Log($"Estado actual de los puntos de aparición en el mundo: {currentSpawnsEnabled.Value}");

                        if (currentSpawnsEnabled.Value)
                        {
                            Log("Las apariciones en el mundo están habilitadas, intentando desactivarlas");
                            await WriteBlock(false, RaidDataBlocks.KWildSpawnsEnabled, token, currentSpawnsEnabled);
                            currentSpawnsEnabled = false;
                            Log("Desactivados con éxito los aparecimientos en el mundo");
                        }
                        else
                        {
                            Log("Las apariciones en el mundo ya están desactivadas, no se ha tomado ninguna medida");
                        }
                    }
                }
                else // When Settings.DisableOverworldSpawns is false, ensure Overworld spawns are enabled
                {
                    Log("Los ajustes indican que los Overworld Spawns deben estar activados. Comprobando estado actual.");
                    Log($"Estado actual de los puntos de aparición en el mundo: {currentSpawnsEnabled.Value}");

                    if (!currentSpawnsEnabled.Value)
                    {
                        Log("Los Overworld Spawns están desactivados, intentando activarlos.");
                        await WriteBlock(true, RaidDataBlocks.KWildSpawnsEnabled, token, currentSpawnsEnabled);
                        currentSpawnsEnabled = true;
                        Log("Overworld Spawns habilitados con éxito.");
                    }
                    else
                    {
                        Log("Los Overworld Spawns ya están activados, no es necesario hacer nada.");
                    }
                }
                Log($"Intentando anular la semilla para {Settings.ActiveRaids[RotationCount].Species}.");
                await OverrideSeedIndex(SeedIndexToReplace, token).ConfigureAwait(false);
                Log("Anulación de semilla completada");

                await Task.Delay(2_000, token).ConfigureAwait(false);
                await LogPlayerLocation(token); // Teleports user to closest Active Den
                await Task.Delay(2_000, token).ConfigureAwait(false);
            }

            for (int i = 0; i < 8; i++)
                await Click(A, 1_000, token).ConfigureAwait(false);

            var timeout = TimeSpan.FromMinutes(1);
            var delayTask = Task.Delay(timeout, token);

            while (true)
            {
                var isOnOverworldTitleTask = IsOnOverworldTitle(token);

                // Wait for either the delay task or the isOnOverworldTitle task to complete
                var completedTask = await Task.WhenAny(isOnOverworldTitleTask, delayTask).ConfigureAwait(false);

                if (completedTask == isOnOverworldTitleTask)
                {
                    // If the task that completed is the isOnOverworldTitleTask, check its result
                    if (await isOnOverworldTitleTask.ConfigureAwait(false))
                    {
                        // If we are on the overworld title, exit the loop
                        break;
                    }
                }
                else
                {
                    // If the delayTask completed first, initiate the reboot protocol
                    Log("Todavía no estoy en el juego, ¡iniciando protocolo de reinicio!");
                    await PerformRebootAndReset(token);
                    return;
                }

                // Add a small delay before the next check to avoid tight looping
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }

            await Task.Delay(5_000 + timing.ExtraTimeLoadOverworld, token).ConfigureAwait(false);
            Log("¡De vuelta en el mundo exterior!");
            LostRaid = 0;
            if (Settings.RaidSettings.MysteryRaids)
            {
                // Count the number of existing Mystery Shiny Raids
                int mysteryRaidCount = Settings.ActiveRaids.Count(raid => raid.Title.Contains("✨ Incursion Shiny Misteriosa ✨"));
                // Only create and add a new Mystery Shiny Raid if there are two or fewer in the list
                if (mysteryRaidCount <= 1)
                {
                    await CreateMysteryRaidAsync();
                }
            }
        }

        private static Dictionary<string, float[]> LoadDenLocations(string resourceName)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<Dictionary<string, float[]>>(json);
        }

        private static string FindNearestLocation((float, float, float) playerLocation, Dictionary<string, float[]> denLocations)
        {
            string? nearestDen = null;
            float minDistance = float.MaxValue;

            foreach (var den in denLocations)
            {
                var denLocation = den.Value;
                float distance = CalculateDistance(playerLocation, (denLocation[0], denLocation[1], denLocation[2]));

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestDen = den.Key;
                }
            }

            return nearestDen;
        }

        private static float CalculateDistance((float, float, float) loc1, (float, float, float) loc2)
        {
            return (float)Math.Sqrt(
                Math.Pow(loc1.Item1 - loc2.Item1, 2) +
                Math.Pow(loc1.Item2 - loc2.Item2, 2) +
                Math.Pow(loc1.Item3 - loc2.Item3, 2));
        }

        private async Task<(float, float, float)> GetPlayersLocation(CancellationToken token)
        {
            // Read the data block (automatically handles encryption)
            var data = await ReadBlock(RaidDataBlocks.KCoordinates, token) as byte[];

            // Extract coordinates
            float x = BitConverter.ToSingle(data, 0);
            float y = BitConverter.ToSingle(data, 4);
            float z = BitConverter.ToSingle(data, 8);

            return (x, y, z);
        }

        public async Task TeleportToDen(float x, float y, float z, CancellationToken token)
        {
            const float offset = 1.8f;
            x += offset;

            // Convert coordinates to byte array
            byte[] xBytes = BitConverter.GetBytes(x);
            byte[] yBytes = BitConverter.GetBytes(y);
            byte[] zBytes = BitConverter.GetBytes(z);
            byte[] coordinatesData = new byte[xBytes.Length + yBytes.Length + zBytes.Length];
            Array.Copy(xBytes, 0, coordinatesData, 0, xBytes.Length);
            Array.Copy(yBytes, 0, coordinatesData, xBytes.Length, yBytes.Length);
            Array.Copy(zBytes, 0, coordinatesData, xBytes.Length + yBytes.Length, zBytes.Length);

            // Write the coordinates
            var teleportBlock = RaidDataBlocks.KCoordinates;
            teleportBlock.Size = coordinatesData.Length;
            var currentCoordinateData = await ReadBlock(teleportBlock, token) as byte[];
            _ = await WriteEncryptedBlockSafe(teleportBlock, currentCoordinateData, coordinatesData, token);

            // Set rotation to face North
            float northRX = 0.0f;
            float northRY = -0.63828725f;
            float northRZ = 0.0f;
            float northRW = 0.7697983f;

            // Convert rotation to byte array
            byte[] rotationData = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(northRX), 0, rotationData, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(northRY), 0, rotationData, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(northRZ), 0, rotationData, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(northRW), 0, rotationData, 12, 4);

            // Write the rotation
            var rotationBlock = RaidDataBlocks.KPlayerRotation;
            rotationBlock.Size = rotationData.Length;
            var currentRotationData = await ReadBlock(rotationBlock, token) as byte[];
            _ = await WriteEncryptedBlockSafe(rotationBlock, currentRotationData, rotationData, token);
        }

        private async Task<List<(uint Area, uint LotteryGroup, uint Den, uint Seed, uint Flags, bool IsEvent)>> ExtractRaidInfo(TeraRaidMapParent mapType, CancellationToken token)
        {
            byte[] raidData = mapType switch
            {
                TeraRaidMapParent.Paldea => await ReadPaldeaRaids(token),
                TeraRaidMapParent.Kitakami => await ReadKitakamiRaids(token),
                TeraRaidMapParent.Blueberry => await ReadBlueberryRaids(token),
                _ => throw new InvalidOperationException("Región no válida"),
            };
            var raids = new List<(uint Area, uint LotteryGroup, uint Den, uint Seed, uint Flags, bool IsEvent)>();
            for (int i = 0; i < raidData.Length; i += Raid.SIZE)
            {
                var raid = new Raid(raidData.AsSpan()[i..(i + Raid.SIZE)]);
                if (raid.IsValid)
                {
                    raids.Add((raid.Area, raid.LotteryGroup, raid.Den, raid.Seed, raid.Flags, raid.IsEvent));
                }
            }

            return raids;
        }

        private async Task LogPlayerLocation(CancellationToken token)
        {
            var playerLocation = await GetPlayersLocation(token);

            // Load den locations for all regions
            var blueberryLocations = LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_blueberry.json");
            var kitakamiLocations = LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_kitakami.json");
            var baseLocations = LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_base.json");

            // Find the nearest location for each set and keep track of the overall nearest
            var nearestDen = new Dictionary<string, string>
    {
        { "Blueberry", FindNearestLocation(playerLocation, blueberryLocations) },
        { "Kitakami", FindNearestLocation(playerLocation, kitakamiLocations) },
        { "Paldea", FindNearestLocation(playerLocation, baseLocations) }
    };

            var overallNearest = nearestDen.Select(kv =>
            {
                var denLocationArray = kv.Key switch
                {
                    "Blueberry" => blueberryLocations[kv.Value],
                    "Kitakami" => kitakamiLocations[kv.Value],
                    "Paldea" => baseLocations[kv.Value],
                    _ => throw new InvalidOperationException("Región no válida")
                };

                var denLocationTuple = (denLocationArray[0], denLocationArray[1], denLocationArray[2]);
                return new { Region = kv.Key, DenIdentifier = kv.Value, Distance = CalculateDistance(playerLocation, denLocationTuple) };
            })
            .OrderBy(d => d.Distance)
            .First();

            TeraRaidMapParent mapType = overallNearest.Region switch
            {
                "Blueberry" => TeraRaidMapParent.Blueberry,
                "Kitakami" => TeraRaidMapParent.Kitakami,
                "Paldea" => TeraRaidMapParent.Paldea,
                _ => throw new InvalidOperationException("Región no válida")
            };

            var activeRaids = await GetActiveRaidLocations(mapType, token);

            // Find the nearest active raid, if any
            var nearestActiveRaid = activeRaids
                .Select(raid => new { Raid = raid, Distance = CalculateDistance(playerLocation, (raid.Coordinates[0], raid.Coordinates[1], raid.Coordinates[2])) })
                .OrderBy(raid => raid.Distance)
                .FirstOrDefault();

            if (nearestActiveRaid != null)
            {
                // Check if the player is already at the nearest active den
                float distanceToNearestActiveDen = CalculateDistance(playerLocation, (nearestActiveRaid.Raid.Coordinates[0], nearestActiveRaid.Raid.Coordinates[1], nearestActiveRaid.Raid.Coordinates[2]));

                // Define a threshold for how close the player needs to be to be considered "at" the den
                const float threshold = 2.0f;

                uint denSeed = nearestActiveRaid.Raid.Seed;
                string hexDenSeed = denSeed.ToString("X8");
                denHexSeed = hexDenSeed;
                Log($"Semilla: {hexDenSeed} Den activa más cercana: {nearestActiveRaid.Raid.DenIdentifier}");

                bool onOverworld = await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false);
                if (!onOverworld)
                {
                    if (distanceToNearestActiveDen > threshold)
                    {
                        uint seedOfNearestDen = nearestActiveRaid.Raid.Seed;

                        // Player is not at the den, so teleport
                        await TeleportToDen(nearestActiveRaid.Raid.Coordinates[0], nearestActiveRaid.Raid.Coordinates[1], nearestActiveRaid.Raid.Coordinates[2], token);
                        Log($"Teletransportado a la den activa más cercana: {nearestActiveRaid.Raid.DenIdentifier} Semilla: {nearestActiveRaid.Raid.Seed:X8} en {overallNearest.Region}.");
                    }
                }
                else
                {
                    // Player is already at the den
                    //  Log($"Already at the nearest active den: {nearestActiveRaid.Raid.DenIdentifier}");
                }
            }
            else
            {
                Log($"No se encontraron dens activas en {overallNearest.Region}");
            }
            bool IsKitakami = overallNearest.Region == "Kitakami";
            bool IsBlueberry = overallNearest.Region == "Blueberry";
        }

        private static bool IsRaidActive((uint Area, uint LotteryGroup, uint Den) raid)
        {
            return true;
        }

        private async Task<List<(string DenIdentifier, float[] Coordinates, int Index, uint Seed, uint Flags, bool IsEvent)>> GetActiveRaidLocations(TeraRaidMapParent mapType, CancellationToken token)
        {
            var raidInfo = await ExtractRaidInfo(mapType, token);
            Dictionary<string, float[]> denLocations = mapType switch
            {
                TeraRaidMapParent.Paldea => LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_base.json"),
                TeraRaidMapParent.Kitakami => LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_kitakami.json"),
                TeraRaidMapParent.Blueberry => LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_blueberry.json"),
                _ => throw new InvalidOperationException("Región no válida")
            };

            var activeRaids = new List<(string DenIdentifier, float[] Coordinates, int Index, uint Seed, uint Flags, bool IsEvent)>();
            int index = 0;
            foreach (var (Area, LotteryGroup, Den, Seed, Flags, IsEvent) in raidInfo)
            {
                string raidIdentifier = $"{Area}-{LotteryGroup}-{Den}";
                if (denLocations.TryGetValue(raidIdentifier, out var coordinates) && IsRaidActive((Area, LotteryGroup, Den)))
                {
                    activeRaids.Add((raidIdentifier, coordinates, index, Seed, Flags, IsEvent));
                }
                index++;
            }

            return activeRaids;
        }

        private async Task WriteProgressLive(GameProgress progress)
        {
            if (Connection is null)
                return;

            if (progress >= GameProgress.Unlocked3Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None, toexpect);
            }

            if (progress >= GameProgress.Unlocked4Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None, toexpect);
            }

            if (progress >= GameProgress.Unlocked5Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None, toexpect);
            }

            if (progress >= GameProgress.Unlocked6Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None, toexpect);
            }
        }

        private async Task SkipRaidOnLosses(CancellationToken token)
        {
            Log($"Tuvimos {Settings.LobbyOptions.SkipRaidLimit} incursiones perdidas/vacías... Continuemos!");

            await SanitizeRotationCount(token).ConfigureAwait(false);
            // Prepare and send an embed to inform users
            await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
            await CloseGame(Hub.Config, token).ConfigureAwait(false);
            await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
        }

        private static string AltPokeImg(PKM pkm)
        {
            string pkmform = string.Empty;
            if (pkm.Form != 0)
                pkmform = $"-{pkm.Form}";

            return _ = $"https://raw.githubusercontent.com/zyro670/PokeTextures/main/Placeholder_Sprites/scaled_up_sprites/Shiny/AlternateArt/" + $"{pkm.Species}{pkmform}" + ".png";
        }

        private async Task ReadRaids(CancellationToken token)
        {
            Log("Obteniendo datos de Raid...");
            await InitializeRaidBlockPointers(token);
            if (firstRun)
            {
                await LogPlayerLocation(token); // Get seed from current den for processing
            }
            string game = await DetermineGame(token);
            container = new(game);
            container.SetGame(game);

            await SetStoryAndEventProgress(token);

            var allRaids = new List<Raid>();
            var allEncounters = new List<ITeraRaid>();
            var allRewards = new List<List<(int, int, int)>>();

            if (IsBlueberry)
            {
                // Process only Blueberry raids
                var dataB = await ReadBlueberryRaids(token);
                Log("Leyendo Blueberry Raids...");
                var (blueberryRaids, blueberryEncounters, blueberryRewards) = await ProcessRaids(dataB, TeraRaidMapParent.Blueberry, token);
                allRaids.AddRange(blueberryRaids);
                allEncounters.AddRange(blueberryEncounters);
                allRewards.AddRange(blueberryRewards);
            }
            else if (IsKitakami)
            {
                // Process only Kitakami raids
                var dataK = await ReadKitakamiRaids(token);
                Log("Leyendo Kitakami Raids...");
                var (kitakamiRaids, kitakamiEncounters, kitakamiRewards) = await ProcessRaids(dataK, TeraRaidMapParent.Kitakami, token);
                allRaids.AddRange(kitakamiRaids);
                allEncounters.AddRange(kitakamiEncounters);
                allRewards.AddRange(kitakamiRewards);
            }
            else
            {
                // Default to processing Paldea raids
                var dataP = await ReadPaldeaRaids(token);
                Log("Leyendo Paldea Raids...");
                var (paldeaRaids, paldeaEncounters, paldeaRewards) = await ProcessRaids(dataP, TeraRaidMapParent.Paldea, token);
                allRaids.AddRange(paldeaRaids);
                allEncounters.AddRange(paldeaEncounters);
                allRewards.AddRange(paldeaRewards);
            }

            // Set combined data to container and process all raids
            container.SetRaids(allRaids);
            container.SetEncounters(allEncounters);
            container.SetRewards(allRewards);
            await ProcessAllRaids(token);
        }

        private async Task<(List<Raid>, List<ITeraRaid>, List<List<(int, int, int)>>)> ProcessRaids(byte[] data, TeraRaidMapParent mapType, CancellationToken token)
        {
            int delivery, enc;
            var tempContainer = new RaidContainer(container.Game);
            tempContainer.SetGame(container.Game);

            Log("Leyendo estado de incursión de evento");
            // Read event raids into tempContainer
            var BaseBlockKeyPointer = await SwitchConnection.PointerAll(Offsets.BlockKeyPointer, token).ConfigureAwait(false);
            await ReadEventRaids(BaseBlockKeyPointer, tempContainer, token).ConfigureAwait(false);
            await ReadEventRaids(BaseBlockKeyPointer, container, token).ConfigureAwait(false);

            (delivery, enc) = tempContainer.ReadAllRaids(data, StoryProgress, EventProgress, 0, mapType);

            var raidsList = tempContainer.Raids.ToList();
            var encountersList = tempContainer.Encounters.ToList();
            var rewardsList = tempContainer.Rewards.Select(r => r.ToList()).ToList();

            return (raidsList, encountersList, rewardsList);
        }

        private async Task InitializeRaidBlockPointers(CancellationToken token)
        {
            RaidBlockPointerP = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerP, token).ConfigureAwait(false);
            RaidBlockPointerK = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerK, token).ConfigureAwait(false);
            RaidBlockPointerB = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerB, token).ConfigureAwait(false);
        }

        private async Task<string> DetermineGame(CancellationToken token)
        {
            string id = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
            return id switch
            {
                RaidCrawler.Core.Structures.Offsets.ScarletID => "Scarlet",
                RaidCrawler.Core.Structures.Offsets.VioletID => "Violet",
                _ => "",
            };
        }

        private async Task SetStoryAndEventProgress(CancellationToken token)
        {
            var BaseBlockKeyPointer = await SwitchConnection.PointerAll(Offsets.BlockKeyPointer, token).ConfigureAwait(false);
            StoryProgress = await GetStoryProgress(BaseBlockKeyPointer, token).ConfigureAwait(false);
            EventProgress = Math.Min(StoryProgress, 3);
        }

        private async Task<byte[]> ReadPaldeaRaids(CancellationToken token)
        {
            var dataP = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP + RaidBlock.HEADER_SIZE, (int)RaidBlock.SIZE_BASE, token).ConfigureAwait(false);
            return dataP;
        }

        private async Task<byte[]> ReadKitakamiRaids(CancellationToken token)
        {
            var dataK = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK, (int)RaidBlock.SIZE_KITAKAMI, token).ConfigureAwait(false);
            return dataK;
        }

        private async Task<byte[]> ReadBlueberryRaids(CancellationToken token)
        {
            var dataB = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerB, (int)RaidBlock.SIZE_BLUEBERRY, token).ConfigureAwait(false);
            return dataB;
        }

        private static (List<int> distGroupIDs, List<int> mightGroupIDs) GetPossibleGroups(RaidContainer container)
        {
            List<int> distGroupIDs = [];
            List<int> mightGroupIDs = [];

            if (container.DistTeraRaids != null)
            {
                foreach (TeraDistribution e in container.DistTeraRaids)
                {
                    if (TeraDistribution.AvailableInGame(e.Entity, container.Game) && !distGroupIDs.Contains(e.DeliveryGroupID))
                        distGroupIDs.Add(e.DeliveryGroupID);
                }
            }

            if (container.MightTeraRaids != null)
            {
                foreach (TeraMight e in container.MightTeraRaids)
                {
                    if (TeraMight.AvailableInGame(e.Entity, container.Game) && !mightGroupIDs.Contains(e.DeliveryGroupID))
                        mightGroupIDs.Add(e.DeliveryGroupID);
                }
            }

            return (distGroupIDs, mightGroupIDs);
        }

        private async Task<List<(uint Area, uint LotteryGroup, uint Den, uint Seed, uint Flags, bool IsEvent)>> ExtractPaldeaRaidInfo(CancellationToken token)
        {
            byte[] raidData = await ReadPaldeaRaids(token);
            var activeRaids = new List<(uint Area, uint LotteryGroup, uint Den, uint Seed, uint Flags, bool IsEvent)>();

            for (int i = 0; i < raidData.Length; i += Raid.SIZE)
            {
                var raid = new Raid(raidData.AsSpan()[i..(i + Raid.SIZE)]);
                if (raid.IsValid && IsRaidActive((raid.Area, raid.LotteryGroup, raid.Den)))
                {
                    activeRaids.Add((raid.Area, raid.LotteryGroup, raid.Den, raid.Seed, raid.Flags, raid.IsEvent));
                }
            }

            return activeRaids;
        }

        private async Task ProcessAllRaids(CancellationToken token)
        {
            var allRaids = container.Raids;
            var allEncounters = container.Encounters;
            var allRewards = container.Rewards;
            uint denHexSeedUInt;
            denHexSeedUInt = uint.Parse(denHexSeed, NumberStyles.AllowHexSpecifier);
            await FindSeedIndexInRaids(denHexSeedUInt, token);
            var raidInfoList = await ExtractPaldeaRaidInfo(token);
            bool newEventSpeciesFound = false;
            var (distGroupIDs, mightGroupIDs) = GetPossibleGroups(container);

            int raidsToCheck = Math.Min(5, allRaids.Count);

            if (!IsKitakami || !IsBlueberry)
            {
                // check if new event species is found
                for (int i = 0; i < raidsToCheck; i++)
                {
                    var raid = allRaids[i];
                    var encounter = allEncounters[i];
                    bool isEventRaid = raid.Flags == 2 || raid.Flags == 3;

                    if (isEventRaid)
                    {
                        string speciesName = SpeciesName.GetSpeciesName(encounter.Species, 2);
                        if (!SpeciesToGroupIDMap.ContainsKey(speciesName))
                        {
                            newEventSpeciesFound = true;
                            SpeciesToGroupIDMap.Clear(); // Clear the map as we've found a new event species
                            break; // No need to check further
                        }
                    }
                }
            }

            for (int i = 0; i < allRaids.Count; i++)
            {
                if (newEventSpeciesFound)
                {
                    // stuff for paldea events
                    var raid = allRaids[i];
                    var encounter1 = allEncounters[i];
                    bool isDistributionRaid = raid.Flags == 2;
                    bool isMightRaid = raid.Flags == 3;
                    var (Area, LotteryGroup, Den, Seed, Flags, IsEvent) = raidInfoList.FirstOrDefault(r =>
                    r.Seed == raid.Seed &&
                    r.Flags == raid.Flags &&
                    r.Area == raid.Area &&
                    r.LotteryGroup == raid.LotteryGroup &&
                    r.Den == raid.Den);

                    string denIdentifier = $"{Area}-{LotteryGroup}-{Den}";

                    if (isDistributionRaid || isMightRaid)
                    {
                        string speciesName = SpeciesName.GetSpeciesName(encounter1.Species, 2);
                        string speciesKey = string.Join("", speciesName.Split(' '));
                        int groupID = -1;

                        if (isDistributionRaid)
                        {
                            var distRaid = container.DistTeraRaids.FirstOrDefault(d => d.Species == encounter1.Species && d.Form == encounter1.Form);
                            if (distRaid != null)
                            {
                                groupID = distRaid.DeliveryGroupID;
                            }
                        }
                        else if (isMightRaid)
                        {
                            var mightRaid = container.MightTeraRaids.FirstOrDefault(m => m.Species == encounter1.Species && m.Form == encounter1.Form);
                            if (mightRaid != null)
                            {
                                groupID = mightRaid.DeliveryGroupID;
                            }
                        }

                        if (groupID != -1)
                        {
                            if (!SpeciesToGroupIDMap.ContainsKey(speciesKey))
                            {
                                SpeciesToGroupIDMap[speciesKey] = [(groupID, i, denIdentifier)];
                            }
                            else
                            {
                                SpeciesToGroupIDMap[speciesKey].Add((groupID, i, denIdentifier));
                            }
                        }
                    }
                }

                var (pk, seed) = IsSeedReturned(allEncounters[i], allRaids[i]);

                for (int a = 0; a < Settings.ActiveRaids.Count; a++)
                {
                    uint set;
                    try
                    {
                        set = uint.Parse(Settings.ActiveRaids[a].Seed, NumberStyles.AllowHexSpecifier);
                    }
                    catch (FormatException)
                    {
                        Log($"Se ha detectado un formato de semilla no válido. Eliminando {Settings.ActiveRaids[a].Seed} de la lista.");
                        Settings.ActiveRaids.RemoveAt(a);
                        a--;  // Decrement the index so that it does not skip the next element.
                        continue;  // Skip to the next iteration.
                    }
                    if (seed == set)
                    {
                        // Species and Form
                        RaidEmbedInfoHelpers.RaidSpecies = (Species)allEncounters[i].Species;
                        RaidEmbedInfoHelpers.RaidSpeciesForm = allEncounters[i].Form;

                        // Update Species and SpeciesForm in ActiveRaids
                        if (!Settings.ActiveRaids[a].ForceSpecificSpecies)
                        {
                            Settings.ActiveRaids[a].Species = (Species)allEncounters[i].Species;
                            Settings.ActiveRaids[a].SpeciesForm = allEncounters[i].Form;
                        }

                        // Encounter Info
                        int raid_delivery_group_id = Settings.ActiveRaids[a].GroupID;
                        var encounter = allRaids[i].GetTeraEncounter(container, allRaids[i].IsEvent ? 3 : StoryProgress, raid_delivery_group_id);
                        if (encounter != null)
                        {
                            RaidEmbedInfoHelpers.RaidLevel = encounter.Level;
                        }
                        else
                        {
                            RaidEmbedInfoHelpers.RaidLevel = 75;
                        }

                        // Translations
                        string natureName = GameInfo.Strings.Natures[(int)pk.Nature];
                        string abilityName = ((Ability)pk.Ability).ToString();
                        string translatedAbilityName = AbilityTranslationDictionary.AbilityTranslation.TryGetValue(abilityName, out var translation) ? translation : abilityName;
                        bool areAllIVsMax = pk.IV_HP == 31 && pk.IV_ATK == 31 && pk.IV_DEF == 31 && pk.IV_SPA == 31 && pk.IV_SPD == 31 && pk.IV_SPE == 31;

                        // Star Rating
                        var stars = allRaids[i].IsEvent ? allEncounters[i].Stars : allRaids[i].GetStarCount(allRaids[i].Difficulty, StoryProgress, allRaids[i].IsBlack);

                        // Raid Title
                        var pkinfo = RaidExtensions<PK9>.GetRaidPrintName(pk);
                        var titlePrefix = allRaids[i].IsShiny ? "Shiny" : "";
                        RaidEmbedInfoHelpers.RaidEmbedTitle = $"{stars} ★ {titlePrefix} {(Species)allEncounters[i].Species}{pkinfo}";

                        // Gender
                        var maleEmoji = Settings.EmbedToggles.MaleEmoji.EmojiString;
                        var femaleEmoji = Settings.EmbedToggles.FemaleEmoji.EmojiString;
                        RaidEmbedInfoHelpers.RaidSpeciesGender = pk.Gender switch
                        {
                            0 when !string.IsNullOrEmpty(maleEmoji) => $"{maleEmoji} Masculino",
                            1 when !string.IsNullOrEmpty(femaleEmoji) => $"{femaleEmoji} Femenino",
                            _ => pk.Gender == 0 ? "Male" : pk.Gender == 1 ? "Female" : "Sin género"
                        };

                        // Nature
                        RaidEmbedInfoHelpers.RaidSpeciesNature = TraduccionesNaturalezas.ContainsKey(natureName) ? TraduccionesNaturalezas[natureName] : natureName;

                        // Ability
                        RaidEmbedInfoHelpers.RaidSpeciesAbility = translatedAbilityName;

                        // IVs
                        RaidEmbedInfoHelpers.RaidSpeciesIVs = areAllIVsMax ? "Máximos" : $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";

                        // Tera Type
                        RaidEmbedInfoHelpers.RaidSpeciesTeraType = $"{(MoveType)allRaids[i].GetTeraType(encounter)}";

                        // Moves
                        var strings = GameInfo.GetStrings(1);
                        var moves = new ushort[4] { allEncounters[i].Move1, allEncounters[i].Move2, allEncounters[i].Move3, allEncounters[i].Move4 };
                        var moveNames = new List<string>();
                        var useTypeEmojis = Settings.EmbedToggles.MoveTypeEmojis;
                        var typeEmojis = Settings.EmbedToggles.CustomTypeEmojis
                           .Where(e => !string.IsNullOrEmpty(e.EmojiCode))
                           .ToDictionary(
                               e => e.MoveType,
                               e => $"{e.EmojiCode}"
                           );

                        for (int j = 0; j < moves.Length; j++)
                        {
                            if (moves[j] != 0)
                            {
                                string moveName = strings.Move[moves[j]];
                                string translatedMoveName = MovesTranslation.ContainsKey(moveName) ? MovesTranslation[moveName] : moveName;
                                byte moveTypeId = MoveInfo.GetType(moves[j], pk.Context);
                                MoveType moveType = (MoveType)moveTypeId;

                                if (useTypeEmojis && typeEmojis.TryGetValue(moveType, out var moveEmoji))
                                {
                                    moveNames.Add($"{moveEmoji} {translatedMoveName}");
                                }
                                else
                                {
                                    moveNames.Add($"\\- {translatedMoveName}");
                                }
                            }
                        }
                        RaidEmbedInfoHelpers.Moves = string.Join("\n", moveNames);

                        // Extra Moves
                        var extraMoveNames = new List<string>();
                        if (allEncounters[i].ExtraMoves.Length != 0)
                        {
                            for (int j = 0; j < allEncounters[i].ExtraMoves.Length; j++)
                            {
                                if (allEncounters[i].ExtraMoves[j] != 0)
                                {
                                    string moveName = strings.Move[allEncounters[i].ExtraMoves[j]];
                                    string translatedMoveName = MovesTranslation.ContainsKey(moveName) ? MovesTranslation[moveName] : moveName;
                                    byte moveTypeId = MoveInfo.GetType(allEncounters[i].ExtraMoves[j], pk.Context);
                                    MoveType moveType = (MoveType)moveTypeId;

                                    if (useTypeEmojis && typeEmojis.TryGetValue(moveType, out var moveEmoji))
                                    {
                                        extraMoveNames.Add($"{moveEmoji} {translatedMoveName}");
                                    }
                                    else
                                    {
                                        extraMoveNames.Add($"\\- {translatedMoveName}");
                                    }
                                }
                            }
                            RaidEmbedInfoHelpers.ExtraMoves = string.Join("\n", extraMoveNames);
                        }

                        // Scale Text and Number
                        RaidEmbedInfoHelpers.ScaleText = $"{PokeSizeDetailedUtil.GetSizeRating(pk.Scale)}";
                        RaidEmbedInfoHelpers.ScaleNumber = pk.Scale;

                        // Special Rewards
                        var res = GetSpecialRewards(allRewards[i], Settings.EmbedToggles.RewardsToShow);
                        RaidEmbedInfoHelpers.SpecialRewards = res;
                        if (string.IsNullOrEmpty(res))
                            res = string.Empty;
                        else
                            res = "**Recompensas Especiales:**\n" + res;

                        // Area Text
                        var areaText = $"{Areas.GetArea((int)(allRaids[i].Area - 1), allRaids[i].MapParent)} - Den {allRaids[i].Den}";
                        Log($"Semilla {seed:X8} encontrada para {(Species)allEncounters[i].Species}  en {areaText}");
                    }
                }
            }
        }

        private async Task FindSeedIndexInRaids(uint denHexSeedUInt, CancellationToken token)
        {
            var upperBound = KitakamiDensCount == 25 ? 94 : 95;
            var startIndex = KitakamiDensCount == 25 ? 94 : 95;

            // Search in Paldea region
            var dataP = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 2304, token).ConfigureAwait(false);
            for (int i = 0; i < 69; i++)
            {
                var seed = BitConverter.ToUInt32(dataP.AsSpan(0x20 + i * 0x20, 4));
                if (seed == denHexSeedUInt)
                {
                    SeedIndexToReplace = i;
                    return;
                }
            }

            // Search in Kitakami region
            var dataK = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK + 0x10, 0xC80, token).ConfigureAwait(false);
            for (int i = 0; i < upperBound; i++)
            {
                var seed = BitConverter.ToUInt32(dataK.AsSpan(i * 0x20, 4));
                if (seed == denHexSeedUInt)
                {
                    SeedIndexToReplace = i + 69;
                    return;
                }
            }

            // Search in Blueberry region
            var dataB = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerB + 0x10, 0xA00, token).ConfigureAwait(false);
            for (int i = startIndex; i < 118; i++)
            {
                var seed = BitConverter.ToUInt32(dataB.AsSpan((i - startIndex) * 0x20, 4));
                if (seed == denHexSeedUInt)
                {
                    SeedIndexToReplace = i - 1;
                    return;
                }
            }

            Log($"Semilla {denHexSeedUInt:X8} no encontrada en ninguna región.");
        }

        public static (PK9, Embed) RaidInfoCommand(string seedValue, int contentType, TeraRaidMapParent map, int storyProgressLevel, int raidDeliveryGroupID, List<string> rewardsToShow, bool moveTypeEmojis, List<MoveTypeEmojiInfo> customTypeEmojis, int queuePosition = 0, bool isEvent = false)
        {
            byte[] enabled = StringToByteArray("00000001");
            byte[] area = StringToByteArray("00000001");
            byte[] displaytype = StringToByteArray("00000001");
            byte[] spawnpoint = StringToByteArray("00000001");
            byte[] thisseed = StringToByteArray(seedValue);
            byte[] unused = StringToByteArray("00000000");
            byte[] content = StringToByteArray($"0000000{contentType}"); // change this to 1 for 6-Star, 2 for 1-6 Star Events, 3 for Mighty 7-Star Raids
            byte[] leaguepoints = StringToByteArray("00000000");
            byte[] raidbyte = enabled.Concat(area).ToArray().Concat(displaytype).ToArray().Concat(spawnpoint).ToArray().Concat(thisseed).ToArray().Concat(unused).ToArray().Concat(content).ToArray().Concat(leaguepoints).ToArray();

            storyProgressLevel = storyProgressLevel switch
            {
                3 => 1,
                4 => 2,
                5 => 3,
                6 => 4,
                0 => 0,
                _ => 4 // default 6Unlocked
            };

            var raid = new Raid(raidbyte, map);
            var progress = storyProgressLevel;
            var raid_delivery_group_id = raidDeliveryGroupID;
            var encounter = raid.GetTeraEncounter(container, raid.IsEvent ? 3 : progress, contentType == 3 ? 1 : raid_delivery_group_id);
            var reward = encounter.GetRewards(container, raid, 0);
            var stars = raid.IsEvent ? encounter.Stars : raid.GetStarCount(raid.Difficulty, storyProgressLevel, raid.IsBlack);
            var teraType = raid.GetTeraType(encounter);
            var form = encounter.Form;
            var level = encounter.Level;

            var param = encounter.GetParam();
            var pk = new PK9
            {
                Species = encounter.Species,
                Form = encounter.Form,
                Move1 = encounter.Move1,
                Move2 = encounter.Move2,
                Move3 = encounter.Move3,
                Move4 = encounter.Move4,
            };
            if (raid.IsShiny) pk.SetIsShiny(true);
            Encounter9RNG.GenerateData(pk, param, EncounterCriteria.Unrestricted, raid.Seed);
            var strings = GameInfo.GetStrings(1);
            var useTypeEmojis = moveTypeEmojis;
            var typeEmojis = customTypeEmojis
                .Where(e => !string.IsNullOrEmpty(e.EmojiCode))
                .ToDictionary(
                    e => e.MoveType,
                    e => $"{e.EmojiCode}"
                );

            var movesList = "";
            bool hasMoves = false;
            for (int i = 0; i < pk.Moves.Length; i++)
            {
                if (pk.Moves[i] != 0)
                {
                    string moveName = strings.Move[pk.Moves[i]];
                    string translatedMoveName = MovesTranslation.ContainsKey(moveName) ? MovesTranslation[moveName] : moveName;
                    byte moveTypeId = MoveInfo.GetType(pk.Moves[i], pk.Context);
                    MoveType moveType = (MoveType)moveTypeId;

                    if (useTypeEmojis && typeEmojis.TryGetValue(moveType, out var moveEmoji))
                    {
                        movesList += $"{moveEmoji} {translatedMoveName}\n";
                    }
                    else
                    {
                        movesList += $"\\- {translatedMoveName}\n";
                    }
                    hasMoves = true;
                }
            }

            var extraMoves = "";
            for (int i = 0; i < encounter.ExtraMoves.Length; i++)
            {
                if (encounter.ExtraMoves[i] != 0)
                {
                    string moveName = strings.Move[encounter.ExtraMoves[i]];
                    string translatedMoveName = MovesTranslation.ContainsKey(moveName) ? MovesTranslation[moveName] : moveName;
                    byte moveTypeId = MoveInfo.GetType(encounter.ExtraMoves[i], pk.Context);
                    MoveType moveType = (MoveType)moveTypeId;

                    if (useTypeEmojis && typeEmojis.TryGetValue(moveType, out var moveEmoji))
                    {
                        extraMoves += $"{moveEmoji} {translatedMoveName}\n";
                    }
                    else
                    {
                        extraMoves += $"\\- {translatedMoveName}\n";
                    }
                    hasMoves = true;
                }
            }
            if (!string.IsNullOrEmpty(extraMoves))
            {
                movesList += $"**Movimientos Extras:**\n{extraMoves}";
            }
            var specialRewards = string.Empty;

            try
            {
                specialRewards = GetSpecialRewards(reward, rewardsToShow);
            }
            catch
            {
                specialRewards = "No hay recompensas válidas para mostrar.";
            }
            string sizeRating = PokeSizeDetailedUtil.GetSizeRating(pk.Scale).ToString();
            if (!ScaleEmojisDictionary.ScaleEmojis.TryGetValue(sizeRating, out var emoji1))
            {
                // Si no se encuentra el emoji, asigna un string vacío o un valor por defecto
                emoji1 = ""; // O puedes asignar un emoji genérico o de placeholder si lo prefieres.
            }
            string abilityName = strings.Ability[pk.Ability];
            string abilityTranslation;
            if (AbilitySpaceTranslationDictionary.AbilitySpaceTranslation.TryGetValue(abilityName, out abilityTranslation))
            {
                // Si se encuentra la traducción, la variable abilityTranslation contiene el valor traducido
            }
            else
            {
                // Si no se encuentra, se usa el nombre original
                abilityTranslation = abilityName;
            }
            string teraTypeName = strings.Types[teraType];
            string translatedTeraTypeName = TeraTypeDictionaries.TeraTranslations.TryGetValue(teraTypeName, out var translation) ? translation : teraTypeName;
            string natureName = ((Nature)pk.Nature).ToString();
            string traduccionNature = TraduccionesNaturalezas.ContainsKey(natureName) ? TraduccionesNaturalezas[natureName] : natureName;
            bool areAllIVsMax = pk.IV_HP == 31 && pk.IV_ATK == 31 && pk.IV_DEF == 31 && pk.IV_SPA == 31 && pk.IV_SPD == 31 && pk.IV_SPE == 31;
            string ivsText = areAllIVsMax ? "Máximos" : $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
            var teraTypeLower = strings.Types[teraType].ToLower();
            var teraIconUrl = $"https://raw.githubusercontent.com/bdawg1989/sprites/main/teraicons/icon1/{teraTypeLower}.png";
            var disclaimer = $"Posición actual: {queuePosition}";
            var titlePrefix = raid.IsShiny ? "Shiny " : "";
            var formName = ShowdownParsing.GetStringFromForm(pk.Form, strings, pk.Species, pk.Context);
            var authorName = $"{stars} ★ {titlePrefix}{(Species)encounter.Species}{(pk.Form != 0 ? $"-{formName}" : "")}{(isEvent ? " (Event Raid)" : "")}";

            (int R, int G, int B) = Task.Run(() => RaidExtensions<PK9>.GetDominantColorAsync(RaidExtensions<PK9>.PokeImg(pk, false, false))).Result;
            var embedColor = new Discord.Color(R, G, B);

            var embed = new EmbedBuilder
            {
                Color = embedColor,
                ThumbnailUrl = RaidExtensions<PK9>.PokeImg(pk, false, false),
            };
            embed.AddField(x =>
            {
                x.Name = "**__Stats__**";
                x.Value = $"{Format.Bold($"Tera Tipo:")} {translatedTeraTypeName} \n" +
                          $"{Format.Bold($"Nivel:")} {level}\n" +
                          $"{Format.Bold($"Habilidad:")} {abilityTranslation}\n" +
                          $"{Format.Bold("Nature:")} {traduccionNature}\n" +
                          $"{Format.Bold("IVs:")} {ivsText}\n" +
                          $"{Format.Bold($"Tamaño:")} {emoji1} {sizeRating}";
                x.IsInline = true;
            });

            if (hasMoves)
            {
                embed.AddField("**__Movimientos__**", movesList, true);
            }
            else
            {
                embed.AddField("**__Movimientos__**", "No hay movimientos disponibles", true);  // Default message
            }

            if (!string.IsNullOrEmpty(specialRewards))
            {
                embed.AddField("**__Recompenzas Especiales__**", specialRewards, true);
            }
            else
            {
                embed.AddField("**__Recompenzas Especiales__**", "No hay recompensas especiales disponibles", true);
            }

            var programIconUrl = "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/icon4.png";
            embed.WithFooter(new EmbedFooterBuilder()
            {
                Text = $"" + disclaimer,
                IconUrl = programIconUrl
            });

            embed.WithAuthor(auth =>
            {
                auth.Name = authorName;
                auth.IconUrl = teraIconUrl;
            });

            return (pk, embed.Build());
        }

        public static byte[] StringToByteArray(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            Array.Reverse(bytes);
            return bytes;
        }

        private async Task<bool> SaveGame(PokeRaidHubConfig config, CancellationToken token)
        {
            Log("Guardando el juego.");
            await Click(B, 3_000, token).ConfigureAwait(false);
            await Click(B, 3_000, token).ConfigureAwait(false);
            await Click(X, 3_000, token).ConfigureAwait(false);
            await Click(L, 5_000 + Hub.Config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            return true;
        }
    }
}