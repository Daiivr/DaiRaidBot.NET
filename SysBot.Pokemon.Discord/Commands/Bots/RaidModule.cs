using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.SV.BotRaid.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Pokemon.RotatingRaidSettingsSV;
using static SysBot.Pokemon.SV.BotRaid.RotatingRaidBotSV;

namespace SysBot.Pokemon.Discord.Commands.Bots
{
    [Summary("Generates and queues various silly trade additions")]
    public partial class RaidModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private readonly PokeRaidHub<T> Hub = SysCord<T>.Runner.Hub;
        private static DiscordSocketClient _client => SysCord<T>.Instance.GetClient();

        [Command("raidinfo")]
        [Alias("ri", "rv")]
        [Summary("Muestra información básica de la incursión para la semilla proporcionada.")]
        public async Task RaidSeedInfoAsync(
            string seedValue,
            int level,
            int storyProgressLevel = 6,
            string? speciesName = null)
        {
            uint seed;
            try
            {
                seed = uint.Parse(seedValue, NumberStyles.AllowHexSpecifier);
            }
            catch (FormatException)
            {
                await ReplyAsync("Formato de semilla inválido. Por favor, introduce una semilla válida.");
                return;
            }
            if (level == 7 && storyProgressLevel == 6 && string.IsNullOrEmpty(speciesName))
            {
                var availableSpecies = string.Join(", ", SpeciesToGroupIDMap.Keys);
                await ReplyAsync($"Para incursiones de 7★, por favor especifica el nombre de la especie. Especies disponibles: {availableSpecies}").ConfigureAwait(false);
                return;
            }
            // Comprobar compatibilidad de dificultad y nivel de progreso de la historia
            var compatible = CheckProgressandLevel(level, storyProgressLevel);
            if (!compatible)
            {
                string requiredProgress = GetRequiredProgress(level);
                await ReplyAsync($"El nivel de dificultad seleccionado para la incursión ({level}★) no es compatible con tu progreso actual en la historia. " +
                                 $"Para acceder a incursiones de {level}★, necesitas tener al menos {requiredProgress} en la historia del juego.").ConfigureAwait(false);
                return;
            }

            var settings = Hub.Config.RotatingRaidSV;  // Obtener configuraciones de RotatingRaidSV

            bool isEvent = !string.IsNullOrEmpty(speciesName);

            var selectedMap = IsBlueberry ? TeraRaidMapParent.Blueberry : (IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);

            if (isEvent && selectedMap != TeraRaidMapParent.Paldea)
            {
                await ReplyAsync("Los eventos solo se pueden ejecutar en el mapa de Paldea.");
                return;
            }

            int raidDeliveryGroupID = -1;
            if (!string.IsNullOrEmpty(speciesName) && SpeciesToGroupIDMap.TryGetValue(speciesName, out var groupIDAndIndices))
            {
                var firstRaidGroupID = groupIDAndIndices.First().GroupID;
                raidDeliveryGroupID = firstRaidGroupID;
            }
            else if (!string.IsNullOrEmpty(speciesName))
            {
                await ReplyAsync("Nombre de la especie no reconocido o no asociado con un evento activo. Por favor, verifica el nombre y vuelve a intentarlo.");
                return;
            }

            var crystalType = level switch
            {
                >= 1 and <= 5 => isEvent ? (TeraCrystalType)2 : 0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("Nivel de dificultad inválido.")
            };

            try
            {
                var rewardsToShow = settings.EmbedToggles.RewardsToShow;
                var (_, embed) = RaidInfoCommand(seedValue, (int)crystalType, selectedMap, storyProgressLevel, raidDeliveryGroupID, rewardsToShow, settings.EmbedToggles.MoveTypeEmojis, settings.EmbedToggles.CustomTypeEmojis, 0, isEvent);

                var instructionMessage = await ReplyAsync("Reacciona con ✅ para agregar la incursión a la cola.");
                var message = await ReplyAsync(embed: embed);
                var checkmarkEmoji = new Emoji("✅");
                await message.AddReactionAsync(checkmarkEmoji);

                SysCord<T>.ReactionService.AddReactionHandler(message.Id, async (reaction) =>
                {
                    if (reaction.UserId == Context.User.Id && reaction.Emote.Name == checkmarkEmoji.Name)
                    {
                        await AddNewRaidParamNext(seedValue, level, storyProgressLevel, speciesName);

                        SysCord<T>.ReactionService.RemoveReactionHandler(reaction.MessageId);
                    }
                });
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));
                    SysCord<T>.ReactionService.RemoveReactionHandler(message.Id);
                    await message.DeleteAsync();
                    await instructionMessage.DeleteAsync();
                });
            }
            catch (Exception ex)
            {
                await ReplyAsync(ex.Message);
            }
            await Context.Message.DeleteAsync().ConfigureAwait(false);
        }

        [Command("banOT")]
        [Alias("ban")]
        [RequireSudo]
        [Summary("Prohíbe a un usuario con el OT especificado de participar en incursiones.")]
        public async Task BanUserAsync(string ot)
        {
            // Cargar los datos del jugador desde el archivo.
            var baseDirectory = AppContext.BaseDirectory;
            var storage = new PlayerDataStorage(baseDirectory);
            var playerData = storage.LoadPlayerData();

            // Filtrar los datos del jugador que coinciden con el OT.
            var matchedPlayers = playerData.Where(pd => pd.Value.OT.Equals(ot, StringComparison.OrdinalIgnoreCase)).ToList();

            // Verificar si hay duplicados.
            if (matchedPlayers.Count > 1)
            {
                await ReplyAsync($"Se encontraron múltiples jugadores con el OT '{ot}'. Prohibición omitida. Por favor, revisa manualmente.");
                return;
            }

            // Si no se encuentra ningún jugador, notificar y regresar.
            if (matchedPlayers.Count == 0)
            {
                await ReplyAsync($"No se encontró ningún jugador con el OT '{ot}'.");
                return;
            }

            // Obtener el NID del jugador para prohibir.
            var playerToBan = matchedPlayers.First();
            ulong nidToBan = playerToBan.Key;

            // Verificar si el NID ya está en la lista de prohibidos.
            if (Hub.Config.RotatingRaidSV.RaiderBanList.List.Any(x => x.ID == nidToBan))
            {
                await ReplyAsync($"El jugador con el OT '{ot}' ya está prohibido.");
                return;
            }

            Hub.Config.RotatingRaidSV.RaiderBanList.AddIfNew(new[] { GetReference(nidToBan, ot, "") });

            await ReplyAsync($"El jugador con el OT '{ot}' ha sido prohibido.");
        }

        [Command("banNID")]
        [Alias("ban")]
        [RequireSudo]
        [Summary("Prohíbe a un usuario con el NID especificado de participar en incursiones.")]
        public async Task BanUserAsync(ulong nid, [Remainder] string comment)
        {
            var ot = string.Empty;
            try
            {
                var baseDirectory = AppContext.BaseDirectory;
                var storage = new PlayerDataStorage(baseDirectory);
                var playerData = storage.LoadPlayerData();
                var matchedNID = playerData.Where(pd => pd.Key.Equals(nid));
                ot = matchedNID.First().Value.OT;
            }
            catch
            {
                ot = "Desconocido";
            }

            Hub.Config.RotatingRaidSV.RaiderBanList.AddIfNew(new[] { GetReference(nid, ot, comment) });
            await ReplyAsync("Hecho.").ConfigureAwait(false);
        }

        [Command("limitrequests")]
        [Alias("lr")]
        [Summary("Establece el límite en el número de solicitudes que un usuario puede hacer.")]
        [RequireSudo]
        public async Task SetLimitRequestsAsync([Summary("El nuevo límite para solicitudes. Establecer en 0 para desactivar.")] int newLimit)
        {
            var settings = Hub.Config.RotatingRaidSV.RaidSettings;
            settings.LimitRequests = newLimit;

            await ReplyAsync($"LimitRequests actualizado a {newLimit}.").ConfigureAwait(false);
        }

        [Command("limitrequeststime")]
        [Alias("lrt")]
        [Summary("Establece el tiempo que los usuarios deben esperar una vez alcanzado su límite de solicitudes.")]
        [RequireSudo]
        public async Task SetLimitRequestsTimeAsync([Summary("El nuevo tiempo en minutos. Establecer en 0 para desactivar.")] int newTime)
        {
            var settings = Hub.Config.RotatingRaidSV.RaidSettings;
            settings.LimitRequestsTime = newTime;

            await ReplyAsync($"LimitRequestsTime actualizado a {newTime} minutos.").ConfigureAwait(false);
        }

        [Command("addlimitbypass")]
        [Alias("alb")]
        [Summary("Agrega un usuario o rol a la lista de excepción para los límites de solicitudes.")]
        [RequireSudo]
        public async Task AddBypassLimitAsync([Remainder] string mention)
        {
            string type;
            string nameToAdd;
            if (MentionUtils.TryParseUser(mention, out ulong idToAdd))
            {
                var user = Context.Guild.GetUser(idToAdd);
                nameToAdd = user?.Username ?? "Usuario Desconocido";
                type = "Usuario";
            }
            // Verificar si la mención es un rol
            else if (MentionUtils.TryParseRole(mention, out idToAdd))
            {
                var role = Context.Guild.GetRole(idToAdd);
                nameToAdd = role?.Name ?? "Rol Desconocido";
                type = "Rol";
            }
            else
            {
                await ReplyAsync("Usuario o rol inválido.").ConfigureAwait(false);
                return;
            }

            if (Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.TryAdd(idToAdd, nameToAdd))
            {
                await ReplyAsync($"Agregado {type} '{nameToAdd}' a la lista de excepción.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"{type} '{nameToAdd}' ya está en la lista de excepción.").ConfigureAwait(false);
            }
        }

        [Command("repeek")]
        [Summary("Toma y envía una captura de pantalla de la Switch actualmente configurada.")]
        [RequireOwner]
        public async Task RePeek()
        {
            string ip = RaidModule<T>.GetBotIPFromJsonConfig(); // Obtener la IP desde la configuración
            var source = new CancellationTokenSource();
            var token = source.Token;

            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No se encontró ningún bot con la dirección IP especificada ({ip}).").ConfigureAwait(false);
                return;
            }

            _ = Array.Empty<byte>();
            byte[]? bytes;
            try
            {
                bytes = await bot.Bot.Connection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error al obtener los píxeles: {ex.Message}");
                return;
            }

            if (bytes.Length == 0)
            {
                await ReplyAsync("No se recibieron datos de captura de pantalla.");
                return;
            }

            using MemoryStream ms = new(bytes);
            var img = "cap.jpg";
            var embed = new EmbedBuilder { ImageUrl = $"attachment://{img}", Color = Color.Purple }
                .WithFooter(new EmbedFooterBuilder { Text = "Aquí está tu captura de pantalla." });

            await Context.Channel.SendFileAsync(ms, img, embed: embed.Build());
        }

        private static string GetBotIPFromJsonConfig()
        {
            try
            {
                // Read the file and parse the JSON
                var jsonData = File.ReadAllText(DaiRaidBot.ConfigPath);
                var config = JObject.Parse(jsonData);

                // Access the IP address from the first bot in the Bots array
                var ip = config["Bots"][0]["Connection"]["IP"].ToString();
                return ip;
            }
            catch (Exception ex)
            {
                // Handle any errors that occur during reading or parsing the file
                Console.WriteLine($"Error reading config file: {ex.Message}");
                return "192.168.1.1"; // Default IP if error occurs
            }
        }

        [Command("addRaidParams")]
        [Alias("arp")]
        [Summary("Añade un nuevo parámetro de incursión.")]
        [RequireSudo]
        public async Task AddNewRaidParam(
            [Summary("Semilla")] string seed,
            [Summary("Nivel de Dificultad (1-7)")] int level,
            [Summary("Nivel de Progreso de la Historia")] int storyProgressLevel = 6,
            [Summary("Nombre de la Especie (Opcional)")] string? speciesName = null)
        {
            if (seed.Length != 8 || !seed.All(c => "0123456789abcdefABCDEF".Contains(c)))
            {
                await ReplyAsync("Formato de semilla inválido. Por favor, introduce una semilla que consista exactamente en 8 dígitos hexadecimales.").ConfigureAwait(false);
                return;
            }
            if (level < 1 || level > 7)
            {
                await ReplyAsync("Nivel de incursión inválido. Por favor, introduce un nivel entre 1 y 7.").ConfigureAwait(false);  // Mensaje ajustado para reflejar el nuevo rango de niveles
                return;
            }

            var gameProgress = ConvertToGameProgress(storyProgressLevel);

            var settings = Hub.Config.RotatingRaidSV;
            bool isEvent = !string.IsNullOrEmpty(speciesName);

            var crystalType = level switch
            {
                >= 1 and <= 5 => isEvent ? (TeraCrystalType)2 : 0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("Nivel de dificultad inválido.")
            };

            int raidDeliveryGroupID = -1;

            if (!string.IsNullOrEmpty(speciesName) && SpeciesToGroupIDMap.TryGetValue(speciesName, out var groupIDAndIndices))
            {
                var firstRaidGroupID = groupIDAndIndices.First().GroupID;
                raidDeliveryGroupID = firstRaidGroupID;
            }
            else if (!string.IsNullOrEmpty(speciesName))
            {
                await ReplyAsync("Nombre de la especie no reconocido o no asociado con un evento activo. Por favor, verifica el nombre e inténtalo de nuevo.");
                return;
            }

            var selectedMap = IsBlueberry ? TeraRaidMapParent.Blueberry : (IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);
            var rewardsToShow = settings.EmbedToggles.RewardsToShow;
            var (pk, raidEmbed) = RaidInfoCommand(seed, (int)crystalType, selectedMap, storyProgressLevel, raidDeliveryGroupID, rewardsToShow, settings.EmbedToggles.MoveTypeEmojis, settings.EmbedToggles.CustomTypeEmojis);
            var description = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "RaidFilesSV\\bodyparam.txt";
            if (File.Exists(prevpath))
                Directory.Move(filepath, prevpath + Path.GetFileName(filepath));

            if (File.Exists(filepath))
                description = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "RaidFilesSV\\pkparam.txt";
            if (File.Exists(prevpk))
                Directory.Move(pkpath, prevpk + Path.GetFileName(pkpath));

            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            RotatingRaidParameters newparam = new()
            {
                CrystalType = crystalType,
                DifficultyLevel = level,
                Description = new[] { description },
                PartyPK = new[] { "" },
                Species = (Species)pk.Species,
                SpeciesForm = pk.Form,
                StoryProgress = (GameProgressEnum)gameProgress,
                Seed = seed,
                IsCoded = true,
                IsShiny = pk.IsShiny,
                AddedByRACommand = false,
                Title = $"{(Species)pk.Species}",
            };
            // Comprobar si la especie es Ditto y establecer PartyPK en la plantilla de Showdown
            if (newparam.Species == Species.Ditto)
            {
                newparam.PartyPK = new string[] {
            "Happiny",
            "Shiny: Yes",
            "Level: 1"
        };
            }
            Hub.Config.RotatingRaidSV.ActiveRaids.Add(newparam);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
            var msg = "Tu nueva incursión ha sido añadida.";
            await ReplyAsync(msg, embed: raidEmbed).ConfigureAwait(false);
        }

        [Command("addUserRaid")]
        [Alias("aur", "ra")]
        [Summary("Añade un nuevo parámetro de incursión siguiente en la cola.")]
        public async Task AddNewRaidParamNext(
            [Summary("Semilla")] string seed,
            [Summary("Nivel de Dificultad (1-7)")] int level,
            [Summary("Nivel de Progreso de la Historia")] int storyProgressLevel = 6,
            [Summary("Nombre de la Especie o Mención del Usuario (Opcional)")] string? speciesNameOrUserMention = null,
            [Summary("Mención del Usuario 2 (Opcional)")] SocketGuildUser? user2 = null,
            [Summary("Mención del Usuario 3 (Opcional)")] SocketGuildUser? user3 = null)
        {
            var botPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;
            if (Hub.Config.RotatingRaidSV.RaidSettings.DisableRequests)
            {
                await ReplyAsync("Las solicitudes de incursión están actualmente deshabilitadas por el anfitrión.").ConfigureAwait(false);
                return;
            }
            var compatible = CheckProgressandLevel(level, storyProgressLevel);
            if (!compatible)
            {
                string requiredProgress = GetRequiredProgress(level);
                await ReplyAsync($"El nivel de dificultad de incursión seleccionado ({level}★) no es compatible con tu progreso actual en la historia. " +
                                 $"Para acceder a incursiones de {level}★, necesitas tener al menos {requiredProgress} en la historia del juego.").ConfigureAwait(false);
                return;
            }

            // Comprobar si el primer parámetro después del nivel de progreso de la historia es una mención de usuario
            bool isUserMention = speciesNameOrUserMention != null && MyRegex1().IsMatch(speciesNameOrUserMention);
            SocketGuildUser? user1 = null;
            string? speciesName = null;

            if (isUserMention)
            {
                // Extraer el ID del usuario de la mención y recuperar el usuario
                var userId2 = ulong.Parse(Regex.Match(speciesNameOrUserMention, @"\d+").Value);
                user1 = Context.Guild.GetUser(userId2);
            }
            else
            {
                speciesName = speciesNameOrUserMention;
            }

            // Comprobar si las incursiones privadas están habilitadas
            if (!Hub.Config.RotatingRaidSV.RaidSettings.PrivateRaidsEnabled && (user1 != null || user2 != null || user3 != null))
            {
                await ReplyAsync("Las incursiones privadas están actualmente deshabilitadas por el anfitrión.").ConfigureAwait(false);
                return;
            }
            // Comprobar si el número de menciones de usuario excede el límite
            int mentionCount = (user1 != null ? 1 : 0) + (user2 != null ? 1 : 0) + (user3 != null ? 1 : 0);
            if (mentionCount > 3)
            {
                await ReplyAsync("Solo puedes mencionar hasta 3 usuarios para una incursión privada.").ConfigureAwait(false);
                return;
            }
            var userId = Context.User.Id;
            if (Hub.Config.RotatingRaidSV.ActiveRaids.Any(r => r.RequestedByUserID == userId))
            {
                await ReplyAsync("Ya tienes una solicitud de incursión existente en la cola.").ConfigureAwait(false);
                return;
            }
            var userRequestManager = new UserRequestManager();
            var userRoles = (Context.User as SocketGuildUser)?.Roles.Select(r => r.Id) ?? new List<ulong>();

            if (!Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.ContainsKey(userId) &&
                !userRoles.Any(Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.ContainsKey))
            {
                if (!userRequestManager.CanRequest(userId, Hub.Config.RotatingRaidSV.RaidSettings.LimitRequests, Hub.Config.RotatingRaidSV.RaidSettings.LimitRequestsTime, out var remainingCooldown))
                {
                    string responseMessage = $"Has alcanzado tu límite de solicitudes. Por favor, espera {remainingCooldown.TotalMinutes:N0} minutos antes de hacer otra solicitud.";

                    if (!string.IsNullOrWhiteSpace(Hub.Config.RotatingRaidSV.RaidSettings.LimitRequestMsg))
                    {
                        responseMessage += $"\n{Hub.Config.RotatingRaidSV.RaidSettings.LimitRequestMsg}";
                    }

                    await ReplyAsync(responseMessage).ConfigureAwait(false);
                    return;
                }
            }

            if (seed.Length != 8 || !seed.All(c => "0123456789abcdefABCDEF".Contains(c)))
            {
                await ReplyAsync("Formato de semilla inválido. Por favor, introduce una semilla que consista exactamente en 8 dígitos hexadecimales.").ConfigureAwait(false);
                return;
            }
            if (level == 7 && storyProgressLevel == 6 && string.IsNullOrEmpty(speciesName))
            {
                var availableSpecies = string.Join(", ", SpeciesToGroupIDMap.Keys);
                await ReplyAsync($"Para incursiones de 7★, por favor especifica el nombre de la especie. Especies disponibles: {availableSpecies}").ConfigureAwait(false);
                return;
            }
            if (level < 1 || level > 7)
            {
                await ReplyAsync("Nivel de incursión inválido. Por favor, introduce un nivel entre 1 y 7.").ConfigureAwait(false);
                return;
            }
            var gameProgress = ConvertToGameProgress(storyProgressLevel);
            if (gameProgress == GameProgress.None)
            {
                await ReplyAsync("Nivel de Progreso de la Historia inválido. Por favor, introduce un valor entre 1 y 6.").ConfigureAwait(false);
                return;
            }
            var settings = Hub.Config.RotatingRaidSV;
            bool isEvent = !string.IsNullOrEmpty(speciesName);
            var selectedMap = IsBlueberry ? TeraRaidMapParent.Blueberry : (IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);

            if (isEvent && selectedMap != TeraRaidMapParent.Paldea)
            {
                await ReplyAsync("Los eventos solo se pueden ejecutar en el mapa de Paldea.");
                return;
            }
            var crystalType = level switch
            {
                >= 1 and <= 5 => isEvent ? (TeraCrystalType)2 : 0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("Nivel de dificultad inválido.")
            };

            int raidDeliveryGroupID = -1;

            if (isEvent && SpeciesToGroupIDMap.TryGetValue(speciesName, out var groupIDAndIndices))
            {
                var firstRaidGroupID = groupIDAndIndices.First().GroupID;
                raidDeliveryGroupID = firstRaidGroupID;
            }
            else if (isEvent)
            {
                await ReplyAsync("Nombre de la especie no reconocido o no asociado con un evento activo. Por favor, verifica el nombre e inténtalo de nuevo.");
                return;
            }

            int effectiveQueuePosition = 1;
            var rewardsToShow = settings.EmbedToggles.RewardsToShow;
            var (pk, raidEmbed) = RaidInfoCommand(seed, (int)crystalType, selectedMap, storyProgressLevel, raidDeliveryGroupID, rewardsToShow, settings.EmbedToggles.MoveTypeEmojis, settings.EmbedToggles.CustomTypeEmojis, effectiveQueuePosition);
            var description = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "RaidFilesSV\\bodyparam.txt";
            if (File.Exists(prevpath))
                Directory.Move(filepath, prevpath + Path.GetFileName(filepath));

            if (File.Exists(filepath))
                description = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "RaidFilesSV\\pkparam.txt";
            if (File.Exists(prevpk))
                Directory.Move(pkpath, prevpk + Path.GetFileName(pkpath));

            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            RotatingRaidParameters newparam = new()
            {
                CrystalType = crystalType,
                Description = new[] { description },
                PartyPK = new[] { "" },
                Species = (Species)pk.Species,
                DifficultyLevel = level,
                SpeciesForm = pk.Form,
                StoryProgress = (GameProgressEnum)gameProgress,
                Seed = seed,
                IsCoded = true,
                IsShiny = pk.IsShiny,
                GroupID = raidDeliveryGroupID,
                AddedByRACommand = true,
                RequestCommand = $"{botPrefix}ra {seed} {level} {storyProgressLevel}{(isEvent ? $" {speciesName}" : "")}",
                RequestedByUserID = Context.User.Id,
                Title = $"Incursión Solicitada por {Context.User.Username}{(isEvent ? $" (Incursión de Evento {speciesName})" : "")}",
                RaidUpNext = false,
                User = Context.User,
                MentionedUsers = new List<SocketUser> { user1, user2, user3 }.Where(u => u != null).ToList(),
            };

            // Comprobar si la especie es Ditto y establecer PartyPK en la plantilla de Showdown
            if (newparam.Species == Species.Ditto)
            {
                newparam.PartyPK = new string[] {
                    "Happiny",
                    "Shiny: Yes",
                    "Level: 1"
                };
            }
            // Determinar la posición correcta para insertar la nueva incursión después de la rotación actual
            int insertPosition = RotationCount + 1;
            while (insertPosition < Hub.Config.RotatingRaidSV.ActiveRaids.Count && Hub.Config.RotatingRaidSV.ActiveRaids[insertPosition].AddedByRACommand)
            {
                insertPosition++;
            }
            // Establecer RaidUpNext en verdadero solo si la nueva incursión se inserta inmediatamente siguiente en la rotación
            if (insertPosition == RotationCount + 1)
            {
                newparam.RaidUpNext = true;
            }
            // Después de insertar la nueva incursión
            Hub.Config.RotatingRaidSV.ActiveRaids.Insert(insertPosition, newparam);

            // Ajustar RotationCount
            if (insertPosition <= RotationCount)
            {
                RotationCount++;
            }

            // Calcular la posición efectiva del usuario en la cola y el tiempo de espera estimado
            effectiveQueuePosition = CalculateEffectiveQueuePosition(Context.User.Id, RotationCount);
            int etaMinutes = effectiveQueuePosition * 6;

            var queuePositionMessage = effectiveQueuePosition > 0
                ? $"Actualmente estás en la posición {effectiveQueuePosition} en la cola con un tiempo de espera estimado de {etaMinutes} minutos."
                : "¡Tu solicitud de incursión es la siguiente!";

            var replyMsg = $"{Context.User.Mention}, ¡tu incursión ha sido añadida a la cola! Te enviaré un DM cuando esté a punto de comenzar.";

            await ReplyAsync(replyMsg, embed: raidEmbed).ConfigureAwait(false);

            // Notify the mentioned users
            var mentionedUsers = new List<SocketGuildUser>();
            if (user1 != null) mentionedUsers.Add(user1);
            if (user2 != null) mentionedUsers.Add(user2);
            if (user3 != null) mentionedUsers.Add(user3);

            foreach (var user in mentionedUsers)
            {
                try
                {
                    await user.SendMessageAsync($"{Context.User.Username} te ha invitado a una incursión privada. Te enviaré el código por DM cuando esté a punto de comenzar.", false, raidEmbed).ConfigureAwait(false);
                }
                catch
                {
                    await ReplyAsync($"No se pudo enviar el DM a {user.Mention}. Por favor, asegúrate de que sus DMs estén abiertos.").ConfigureAwait(false);
                }
            }
            try
            {
                if (Context.User is SocketGuildUser guildUser)
                {
                    await guildUser.SendMessageAsync($"Aquí está la información de tu incursión:\n{queuePositionMessage}\nTu comando de solicitud: `{newparam.RequestCommand}`", false, raidEmbed).ConfigureAwait(false);
                }
                else
                {
                    await ReplyAsync("No se pudo enviar el DM. Por favor, asegúrate de que tus DMs estén abiertos.").ConfigureAwait(false);
                }
            }
            catch
            {
                await ReplyAsync("No se pudo enviar el DM. Por favor, asegúrate de que tus DMs estén abiertos.").ConfigureAwait(false);
            }
        }

        public GameProgress ConvertToGameProgress(int storyProgressLevel)
        {
            return storyProgressLevel switch
            {
                6 => GameProgress.Unlocked6Stars,
                5 => GameProgress.Unlocked5Stars,
                4 => GameProgress.Unlocked4Stars,
                3 => GameProgress.Unlocked3Stars,
                2 => GameProgress.UnlockedTeraRaids,
                1 => GameProgress.UnlockedTeraRaids,
                _ => GameProgress.Unlocked6Stars
            };
        }

        public bool CheckProgressandLevel(int level, int storyProgressLevel)
        {
            switch (storyProgressLevel)
            {
                case 6: // Unlocked 6 Stars
                    return level >= 3 && level <= 7;
                case 5: // Unlocked 5 Stars
                    return level >= 3 && level <= 5;
                case 4: // Unlocked 4 Stars
                    return level >= 3 && level <= 4;
                case 3: // Unlocked 3 Stars
                    return level == 3;
                default: return false; // No 1 or 2 Star Unlocked
            }
        }

        public string GetRequiredProgress(int level)
        {
            return level switch
            {
                7 => "Progreso Desbloqueado 6☆",
                6 => "Progreso Desbloqueado 6☆",
                5 => "Progreso Desbloqueado 5☆",
                4 => "Progreso Desbloqueado 4☆",
                3 => "Progreso Desbloqueado 3☆",
                _ => throw new ArgumentException("Nivel de Progreso de la Historia inválido... ¿de dónde estás obteniendo tus semillas?\nUsa <https://genpkm.com/seeds.html> para obtenerlas."),
            };
        }

        [Command("addRaidPK")]
        [Alias("rp")]
        [Summary("Añade el Pokémon proporcionado en el conjunto de Showdown a la incursión del usuario en la cola.")]
        public async Task AddRaidPK([Summary("Conjunto de Showdown")][Remainder] string content)
        {
            content = ReusableActions.StripCodeBlock(content);
            var set = new ShowdownSet(content);
            var template = AutoLegalityWrapper.GetTemplate(set);
            if (set.InvalidLines.Count != 0 || set.Species <= 0)
            {
                var msg = $"No se pudo analizar el conjunto de Showdown:\n{string.Join("\n", set.InvalidLines)}";
                await ReplyAsync(msg).ConfigureAwait(false);
                return;
            }

            try
            {
                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                var pkm = sav.GetLegal(template, out var result);
                var la = new LegalityAnalysis(pkm);
                var spec = GameInfo.Strings.Species[template.Species];
                pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;
                if (pkm is not T pk || !la.Valid)
                {
                    var reason = result == "Timeout" ? $"Ese conjunto de {spec} tardó demasiado en generarse." : $"No pude crear un {spec} a partir de ese conjunto.";
                    var imsg = $"¡Vaya! {reason}";
                    await ReplyAsync(imsg).ConfigureAwait(false);
                    return;
                }

                var userId = Context.User.Id;
                var raidParameters = Hub.Config.RotatingRaidSV.ActiveRaids;
                var raidToUpdate = raidParameters.FirstOrDefault(r => r.RequestedByUserID == userId);
                string[] partyPK = content.Split('\n', StringSplitOptions.RemoveEmptyEntries); // Eliminar líneas vacías
                if (raidToUpdate != null)
                {
                    raidToUpdate.PartyPK = partyPK;
                    await Context.Message.DeleteAsync().ConfigureAwait(false);
                    var embed = await RPEmbed.PokeEmbedAsync(pkm, Context.User.Username);
                    await ReplyAsync(embed: embed).ConfigureAwait(false);
                }
                else
                {
                    var msg = "¡No tienes una incursión en la cola!";
                    await ReplyAsync(msg).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(RaidModule<T>));
                var msg = $"¡Vaya! Ocurrió un problema inesperado con este conjunto de Showdown:\n```{string.Join("\n", set.GetSetLines())}```";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
        }

        [Command("addRaidPK")]
        [Alias("rp")]
        [Summary("Añade el Pokémon proporcionado en el conjunto de Showdown a la incursión del usuario en la cola.")]
        public async Task AddRaidPK()
        {
            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment == default)
            {
                await ReplyAsync("¡No se proporcionó ningún archivo adjunto!").ConfigureAwait(false);
                return;
            }

            var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
            var pk = GetRequest(att);
            if (pk == null)
            {
                await ReplyAsync("¡El archivo adjunto proporcionado no es compatible con este módulo!").ConfigureAwait(false);
                return;
            }
            else
            {
                var userId = Context.User.Id;
                var raidParameters = Hub.Config.RotatingRaidSV.ActiveRaids;
                var raidToUpdate = raidParameters.FirstOrDefault(r => r.RequestedByUserID == userId);
                var set = ShowdownParsing.GetShowdownText(pk);
                string[] partyPK = set.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (raidToUpdate != null)
                {
                    raidToUpdate.PartyPK = partyPK;
                    await Context.Message.DeleteAsync().ConfigureAwait(false);
                    var embed = await RPEmbed.PokeEmbedAsync(pk, Context.User.Username);
                    await ReplyAsync(embed: embed).ConfigureAwait(false);
                }
                else
                {
                    var msg = "¡No tienes una incursión en la cola!";
                    await ReplyAsync(msg).ConfigureAwait(false);
                }
            }
        }

        [Command("raidQueueStatus")]
        [Alias("rqs")]
        [Summary("Verifica el número de incursiones antes de la solicitud del usuario y proporciona un tiempo estimado de espera.")]
        public async Task CheckQueueStatus()
        {
            var userId = Context.User.Id;
            int currentPosition = RotationCount;

            // Encuentra el índice de la solicitud del usuario en la cola, excluyendo Incursiones Brillantes Misteriosas
            var userRequestIndex = Hub.Config.RotatingRaidSV.ActiveRaids.FindIndex(r => r.RequestedByUserID == userId && !r.Title.Contains("Mystery Shiny Raid"));

            EmbedBuilder embed = new();

            if (userRequestIndex == -1)
            {
                embed.Title = "Estado de la Cola";
                embed.Color = Color.Red;
                embed.Description = $"{Context.User.Mention}, no tienes una solicitud de incursión en la cola.";
            }
            else
            {
                // Calcular la posición efectiva de la solicitud del usuario en la cola
                int raidsBeforeUser = CalculateEffectiveQueuePosition(userId, currentPosition);

                if (raidsBeforeUser <= 0)
                {
                    embed.Title = "Estado de la Cola";
                    embed.Color = Color.Green;
                    embed.Description = $"{Context.User.Mention}, ¡tu solicitud de incursión es la siguiente!";
                }
                else
                {
                    // Calcular el tiempo estimado de espera suponiendo que cada incursión toma 6 minutos
                    int etaMinutes = raidsBeforeUser * 6;

                    embed.Title = "Estado de la Cola";
                    embed.Color = Color.Orange;
                    embed.Description = $"{Context.User.Mention}, aquí está el estado de tu solicitud de incursión:";
                    embed.AddField("Incursiones Antes de la Tuya", raidsBeforeUser.ToString(), true);
                    embed.AddField("Tiempo Estimado", $"{etaMinutes} minutos", true);
                }
            }

            await Context.Message.DeleteAsync().ConfigureAwait(false);
            await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);
        }

        private int CalculateEffectiveQueuePosition(ulong userId, int currentPosition)
        {
            int effectivePosition = 0;
            bool userRequestFound = false;

            for (int i = currentPosition; i < Hub.Config.RotatingRaidSV.ActiveRaids.Count + currentPosition; i++)
            {
                int actualIndex = i % Hub.Config.RotatingRaidSV.ActiveRaids.Count;
                var raid = Hub.Config.RotatingRaidSV.ActiveRaids[actualIndex];

                // Check if the raid is added by the RA command and is not a Mystery Shiny Raid
                if (raid.AddedByRACommand && !raid.Title.Contains("✨ Incursión Shiny Misteriosa ✨"))
                {
                    if (raid.RequestedByUserID == userId)
                    {
                        // Found the user's request
                        userRequestFound = true;
                        break;
                    }
                    else if (!userRequestFound)
                    {
                        // Count other user requested raids before the user's request
                        effectivePosition++;
                    }
                }
            }

            // If the user's request was not found after the current position, count from the beginning
            if (!userRequestFound)
            {
                for (int i = 0; i < currentPosition; i++)
                {
                    var raid = Hub.Config.RotatingRaidSV.ActiveRaids[i];
                    if (raid.AddedByRACommand && !raid.Title.Contains("✨ Incursión Shiny Misteriosa ✨"))
                    {
                        if (raid.RequestedByUserID == userId)
                        {
                            // Found the user's request
                            break;
                        }
                        else
                        {
                            effectivePosition++;
                        }
                    }
                }
            }

            return effectivePosition;
        }

        [Command("raidQueueClear")]
        [Alias("rqc")]
        [Summary("Elimina la incursión agregada por el usuario.")]
        public async Task RemoveOwnRaidParam()
        {
            var userId = Context.User.Id;
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;

            // Encontrar la incursión agregada por el usuario
            var userRaid = list.FirstOrDefault(r => r.RequestedByUserID == userId && r.AddedByRACommand);
            if (userRaid == null)
            {
                await ReplyAsync("No tienes una incursión agregada.").ConfigureAwait(false);
                return;
            }

            // Evitar cancelar si la incursión es la siguiente
            if (userRaid.RaidUpNext)
            {
                await ReplyAsync("Tu solicitud de incursión es la siguiente y no se puede cancelar en este momento.").ConfigureAwait(false);
                return;
            }

            // Eliminar la incursión si no es la siguiente
            list.Remove(userRaid);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
            var msg = $"Se ha eliminado tu incursión de la cola.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("removeRaidParams")]
        [Alias("rrp")]
        [Summary("Elimina un parámetro de incursión.")]
        [RequireSudo]
        public async Task RemoveRaidParam([Summary("Índice de Semilla")] int index)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                list.RemoveAt(index);
                var msg = $"¡La incursión para {raid.Title} | {raid.Seed:X8} ha sido eliminada!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Índice de parámetro de incursión inválido.").ConfigureAwait(false);
        }

        [Command("toggleRaidParams")]
        [Alias("trp")]
        [Summary("Alterna el parámetro de incursión.")]
        [RequireSudo]
        public async Task ToggleRaidParam([Summary("Índice de Semilla")] int index)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.ActiveInRotation = !raid.ActiveInRotation;
                var m = raid.ActiveInRotation ? "habilitado" : "deshabilitado";
                var msg = $"¡La incursión para {raid.Title} | {raid.Seed:X8} ha sido {m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Índice de parámetro de incursión inválido.").ConfigureAwait(false);
        }

        [Command("togglecodeRaidParams")]
        [Alias("tcrp")]
        [Summary("Alterna el parámetro de incursión con código.")]
        [RequireSudo]
        public async Task ToggleCodeRaidParam([Summary("Índice de Semilla")] int index)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.IsCoded = !raid.IsCoded;
                var m = raid.IsCoded ? "con código" : "sin código";
                var msg = $"¡La incursión para {raid.Title} | {raid.Seed:X8} ahora está {m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Índice de parámetro de incursión inválido.").ConfigureAwait(false);
        }

        [Command("changeRaidParamTitle")]
        [Alias("crpt")]
        [Summary("Cambia el título de un parámetro de incursión.")]
        [RequireSudo]
        public async Task ChangeRaidParamTitle([Summary("Índice de Semilla")] int index, [Summary("Título")] string title)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.Title = title;
                var msg = $"¡El título de la incursión para {raid.Title} | {raid.Seed:X8} ha sido cambiado a: {title}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Índice de parámetro de incursión inválido.").ConfigureAwait(false);
        }

        [Command("viewraidList")]
        [Alias("vrl", "rotatinglist")]
        [Summary("Muestra las incursiones en la colección actual.")]
        public async Task GetRaidListAsync()
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            int count = list.Count;
            int fields = (int)Math.Ceiling((double)count / 15);
            var embed = new EmbedBuilder
            {
                Title = "Lista de Incursiones"
            };
            for (int i = 0; i < fields; i++)
            {
                int start = i * 15;
                int end = Math.Min(start + 14, count - 1);
                var fieldBuilder = new StringBuilder();
                for (int j = start; j <= end; j++)
                {
                    var raid = list[j];
                    int paramNumber = j;
                    fieldBuilder.AppendLine($"{paramNumber}.) {raid.Title} - {raid.Seed} - Estado: {(raid.ActiveInRotation ? "Activo" : "Inactivo")}");
                }
                embed.AddField($"Lista de Incursiones - Parte {i + 1}", fieldBuilder.ToString(), false);
            }
            await ReplyAsync($"Estas son las incursiones actualmente en la lista (total: {count}):", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("toggleRaidPK")]
        [Alias("trpk")]
        [Summary("Alterna el parámetro de incursión.")]
        [RequireSudo]
        public async Task ToggleRaidParamPK([Summary("Índice de Semilla")] int index, [Summary("Conjunto de Showdown")][Remainder] string content)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.PartyPK = new[] { content };
                var m = string.Join("\n", raid.PartyPK);
                var msg = $"¡RaidPK para {raid.Title} | {raid.Seed:X8} ha sido actualizado a:\n{m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Índice de parámetro de incursión inválido.").ConfigureAwait(false);
        }

        [Command("raidhelp")]
        [Alias("rh")]
        [Summary("Muestra la lista de comandos de ayuda de incursiones.")]
        public async Task GetRaidHelpListAsync()
        {
            var embed = new EmbedBuilder();
            List<string> cmds = new()
            {
                "$ban - Prohibir a un usuario de las incursiones mediante NID. [Comando] [OT] - Comando solo para Sudo.\n",
                "$vrl - Ver todas las incursiones en la lista.\n",
                "$arp - Añadir parámetro a la colección.\nEj: [Comando] [Índice] [Especie] [Dificultad]\n",
                "$rrp - Eliminar parámetro de la colección.\nEj: [Comando] [Índice]\n",
                "$trp - Alternar el parámetro como Activo/Inactivo en la colección.\nEj: [Comando] [Índice]\n",
                "$tcrp - Alternar el parámetro como Codificado/No codificado en la colección.\nEj: [Comando] [Índice]\n",
                "$trpk - Establecer un PartyPK para el parámetro mediante un conjunto de Showdown.\nEj: [Comando] [Índice] [Conjunto de Showdown]\n",
                "$crpt - Establecer el título para el parámetro.\nEj: [Comando] [Índice]"
            };
            string msg = string.Join("", cmds.ToList());
            embed.AddField(x =>
            {
                x.Name = "Comandos de Ayuda de Incursiones";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("¡Aquí tienes tu ayuda para incursiones!", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("unbanrotatingraider")]
        [Alias("ubrr")]
        [Summary("Elimina el NID especificado de la lista de prohibidos para Incursiones en SV.")]
        [RequireSudo]
        public async Task UnbanRotatingRaider([Summary("Elimina el NID especificado de la lista de prohibidos para Incursiones en SV.")] string nid)
        {
            var list = Hub.Config.RotatingRaidSV.RaiderBanList.List.ToArray();
            string msg = $"{Context.User.Mention}, no se encontró ningún usuario con ese NID.";
            for (int i = 0; i < list.Length; i++)
            {
                if ($"{list[i].ID}".Equals(nid))
                {
                    msg = $"{Context.User.Mention}, el usuario {list[i].Name} - {list[i].ID} ha sido desbaneado.";
                    Hub.Config.RotatingRaidSV.RaiderBanList.List.ToList().Remove(list[i]);
                }
            }
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        private RemoteControlAccess GetReference(ulong id, string name, string comment) => new()
        {
            ID = id,
            Name = name,
            Comment = "Prohibido el " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" + $"({comment})"
        };

        private static T? GetRequest(Download<PKM> dl)
        {
            if (!dl.Success)
                return null;
            return dl.Data switch
            {
                null => null,
                T pk => pk,
                _ => EntityConverter.ConvertToType(dl.Data, typeof(T), out _) as T,
            };
        }

        [GeneratedRegex(@"^<@!?\d+>$")]
        private static partial Regex MyRegex1();
    }
}