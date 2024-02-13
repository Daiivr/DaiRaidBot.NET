﻿using Discord;
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
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Pokemon.RotatingRaidSettingsSV;
using static SysBot.Pokemon.SV.BotRaid.RotatingRaidBotSV;

namespace SysBot.Pokemon.Discord.Commands.Bots
{
    [Summary("Generates and queues various silly trade additions")]
    public class RaidModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private readonly PokeRaidHub<T> Hub = SysCord<T>.Runner.Hub;
        private DiscordSocketClient _client => SysCord<T>.Instance.GetClient();

        [Command("raidinfo")]
        [Alias("ri", "rv")]
        [Summary("Displays basic Raid Info of the provided seed.")]
        public async Task RaidSeedInfoAsync(
            string seedValue,
            int level,
            int storyProgressLevel = 6,
            string? eventType = null)
        {
            uint seed;
            try
            {
                seed = uint.Parse(seedValue, NumberStyles.AllowHexSpecifier);
            }
            catch (FormatException)
            {
                await ReplyAsync("⚠️ Formato de semilla no válido. Por favor ingrese una semilla válida.");
                return;
            }

            // Check Compatibility of Difficulty and Story Progress Level
            var compatible = CheckProgressandLevel(level, storyProgressLevel);
            if (!compatible)
            {
                await ReplyAsync($"✘ La dificultad de la incursión requiere un {GetRequiredProgress(level)}.").ConfigureAwait(false);
                return;
            }

            var settings = Hub.Config.RotatingRaidSV;  // Get RotatingRaidSV settings

            var crystalType = level switch
            {
                >= 1 and <= 5 => eventType == "Evento" ? (TeraCrystalType)2 : (TeraCrystalType)0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("✘ Nivel de dificultad no válido.")
            };

            // Check if event type is specified but events are turned off
            if (eventType == "Evento" && !settings.EventSettings.EventActive)
            {
                await ReplyAsync("⚠️ Lo sentimos, pero la configuración de eventos está desactivada en este momento o no hay eventos activos.").ConfigureAwait(false);
                return;
            }

            var raidDeliveryGroupID = (level == 7) ? settings.EventSettings.MightyGroupID : settings.EventSettings.DistGroupID;  // Use MightyGroupID for 7 star, otherwise use DistGroupID
            var isEvent = eventType == "Evento";

            try
            {
                var selectedMap = IsBlueberry ? TeraRaidMapParent.Blueberry : (IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);
                var rewardsToShow = settings.EmbedToggles.RewardsToShow;
                var (_, embed) = RaidInfoCommand(seedValue, (int)crystalType, selectedMap, storyProgressLevel, raidDeliveryGroupID, rewardsToShow, 0, isEvent);

                var instructionMessage = await ReplyAsync("Reacciona con ✅ para agregar el raid a la cola.");
                var message = await ReplyAsync(embed: embed);
                var checkmarkEmoji = new Emoji("✅");
                await message.AddReactionAsync(checkmarkEmoji);

                SysCord<T>.ReactionService.AddReactionHandler(message.Id, async (reaction) =>
                {
                    if (reaction.UserId == Context.User.Id && reaction.Emote.Name == checkmarkEmoji.Name)
                    {
                        await AddNewRaidParamNext(seedValue, level, storyProgressLevel, eventType);

                        SysCord<T>.ReactionService.RemoveReactionHandler(reaction.MessageId);
                    }
                });
                _ = Task.Run(async () =>
                {
                    // Delay for 1 minute
                    await Task.Delay(TimeSpan.FromMinutes(1));

                    // Remove the reaction handler
                    SysCord<T>.ReactionService.RemoveReactionHandler(message.Id);

                    // Delete the messages
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
        [Summary("Bans a user with the specified OT from participating in raids.")]
        public async Task BanUserAsync(string ot)
        {
            // Load the player data from the file.
            var baseDirectory = AppContext.BaseDirectory;
            var storage = new PlayerDataStorage(baseDirectory);
            var playerData = storage.LoadPlayerData();

            // Filter out player data that matches the OT.
            var matchedPlayers = playerData.Where(pd => pd.Value.OT.Equals(ot, StringComparison.OrdinalIgnoreCase)).ToList();

            // Check if there are duplicates.
            if (matchedPlayers.Count > 1)
            {
                await ReplyAsync($"⚠️ Se encontraron varios jugadores con OT '{ot}'. La prohibición se saltó. Por favor revise manualmente.");
                return;
            }

            // If no player is found, notify and return.
            if (matchedPlayers.Count == 0)
            {
                await ReplyAsync($"✘ No se encontró ningún jugador con OT '{ot}'.");
                return;
            }

            // Get the player's NID to ban.
            var playerToBan = matchedPlayers.First();
            ulong nidToBan = playerToBan.Key;

            // Check if the NID is already in the ban list.
            if (Hub.Config.RotatingRaidSV.RaiderBanList.List.Any(x => x.ID == nidToBan))
            {
                await ReplyAsync($"⚠️ El jugador con OT '{ot}' ya está baneado.");
                return;
            }

            Hub.Config.RotatingRaidSV.RaiderBanList.AddIfNew(new[] { GetReference(nidToBan, ot, "") });

            // Optionally, save the ban list to persist the changes
            // SaveBanListMethod(Hub.Config.RaiderBanList);

            // Notify the command issuer that the ban was successful.
            await ReplyAsync($"✔ El jugador con OT '{ot}' ha sido baneado.");
        }

        [Command("banNID")]
        [Alias("ban")]
        [RequireSudo]
        [Summary("Bans a user with the specified NID from participating in raids.")]
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
                ot = "Unknown";
            }

            Hub.Config.RotatingRaidSV.RaiderBanList.AddIfNew(new[] { GetReference(nid, ot, comment) });
            await ReplyAsync("Done.").ConfigureAwait(false);
        }

        [Command("limitrequests")]
        [Alias("lr")]
        [Summary("Sets the limit on the number of requests a user can make.")]
        [RequireSudo]
        public async Task SetLimitRequestsAsync([Summary("The new limit for requests. Set to 0 to disable.")] int newLimit)
        {
            var settings = Hub.Config.RotatingRaidSV.RaidSettings;
            settings.LimitRequests = newLimit;

            await ReplyAsync($"✔ Limite de solicitudes actualizado a {newLimit}.").ConfigureAwait(false);
        }

        [Command("limitrequeststime")]
        [Alias("lrt")]
        [Summary("Sets the time users must wait once their request limit is reached.")]
        [RequireSudo]
        public async Task SetLimitRequestsTimeAsync([Summary("The new time in minutes. Set to 0 to disable.")] int newTime)
        {
            var settings = Hub.Config.RotatingRaidSV.RaidSettings;
            settings.LimitRequestsTime = newTime;

            await ReplyAsync($"✔ Limite de tiempo de las solicitudes actualizado a {newTime} minutos.").ConfigureAwait(false);
        }

        [Command("addlimitbypass")]
        [Alias("alb")]
        [Summary("Adds a user or role to the bypass list for request limits.")]
        [RequireSudo]
        public async Task AddBypassLimitAsync([Remainder] string mention)
        {
            ulong idToAdd = 0;
            string nameToAdd = "";
            string type = "";

            // Check if mention is a user
            if (MentionUtils.TryParseUser(mention, out idToAdd))
            {
                var user = Context.Guild.GetUser(idToAdd);
                nameToAdd = user?.Username ?? "Usuario desconocido";
                type = "Usuario";
            }
            // Check if mention is a role
            else if (MentionUtils.TryParseRole(mention, out idToAdd))
            {
                var role = Context.Guild.GetRole(idToAdd);
                nameToAdd = role?.Name ?? "Rol desconocido";
                type = "Role";
            }
            else
            {
                await ReplyAsync("⚠️ Usuario o rol no válido.").ConfigureAwait(false);
                return;
            }

            if (!Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.ContainsKey(idToAdd))
            {
                Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.Add(idToAdd, nameToAdd);

                await ReplyAsync($"✔ Se agregó {type} '{nameToAdd}' a la lista de omisiones.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"⚠️ {type} '{nameToAdd}' ya está en la lista de omisión.").ConfigureAwait(false);
            }
        }

        [Command("repeek")]
        [Summary("Take and send a screenshot from the currently configured Switch.")]
        [RequireOwner]
        public async Task RePeek()
        {
            string ip = GetBotIPFromJsonConfig(); // Fetch the IP from the config
            var source = new CancellationTokenSource();
            var token = source.Token;

            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"⚠️ No se encontró ningún bot con la dirección IP: ({ip}).").ConfigureAwait(false);
                return;
            }

            byte[]? bytes = Array.Empty<byte>();
            try
            {
                bytes = await bot.Bot.Connection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                await ReplyAsync($"⚠️ Error al recuperar píxeles: {ex.Message}");
                return;
            }

            if (bytes.Length == 0)
            {
                await ReplyAsync("⚠️ No se recibieron datos de captura de pantalla.");
                return;
            }

            using MemoryStream ms = new MemoryStream(bytes);
            var img = "cap.jpg";
            var embed = new EmbedBuilder { ImageUrl = $"attachment://{img}", Color = Color.Purple }
                .WithFooter(new EmbedFooterBuilder { Text = $"✔ Aquí está tu captura de pantalla." });

            await Context.Channel.SendFileAsync(ms, img, embed: embed.Build());
        }

        private string GetBotIPFromJsonConfig()
        {
            try
            {
                // Read the file and parse the JSON
                var jsonData = File.ReadAllText(NotRaidBot.ConfigPath);
                var config = JObject.Parse(jsonData);

                // Access the IP address from the first bot in the Bots array
                var ip = config["Bots"][0]["Connection"]["IP"].ToString();
                return ip;
            }
            catch (Exception ex)
            {
                // Handle any errors that occur during reading or parsing the file
                Console.WriteLine($"⚠️ Error al leer el archivo de configuración: {ex.Message}");
                return "192.168.1.1"; // Default IP if error occurs
            }
        }

        [Command("addRaidParams")]
        [Alias("arp")]
        [Summary("Adds new raid parameter.")]
        [RequireSudo]
        public async Task AddNewRaidParam(
            [Summary("Seed")] string seed,
            [Summary("Difficulty Level (1-7)")] int level,
            [Summary("Story Progress Level")] int storyProgressLevel = 6,
            [Summary("Event (Optional)")] string? eventType = null)  // New optional parameter for specifying event type
        {
            // Validate the seed for hexadecimal format
            if (seed.Length != 8 || !seed.All(c => "0123456789abcdefABCDEF".Contains(c)))
            {
                await ReplyAsync("⚠️ Formato de semilla no válido. Ingrese una semilla que consta de exactamente 8 dígitos hexadecimales.").ConfigureAwait(false);
                return;
            }
            if (level < 1 || level > 7)  // Adjusted level range to 1-7
            {
                await ReplyAsync("⚠️ Nivel de incursión no válido. Por favor introduzca un nivel entre 1 y 7.").ConfigureAwait(false);  // Adjusted message to reflect new level range
                return;
            }

            // Convert StoryProgressLevel to GameProgress enum value
            var gameProgress = ConvertToGameProgress(storyProgressLevel);

            // Get the settings object from Hub
            var settings = Hub.Config.RotatingRaidSV;

            var crystalType = level switch
            {
                >= 1 and <= 5 => eventType == "Evento" ? (TeraCrystalType)2 : (TeraCrystalType)0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("✘ Nivel de dificultad no válido.")
            };

            // Check if event type is specified but events are turned off
            if (eventType == "Event" && !settings.EventSettings.EventActive)
            {
                await ReplyAsync("⚠️ Lo sentimos, pero la configuración de eventos está desactivada en este momento o no hay eventos activos.").ConfigureAwait(false);
                return;
            }

            var selectedMap = IsBlueberry ? TeraRaidMapParent.Blueberry : (IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);
            var raidDeliveryGroupID = settings.EventSettings.MightyGroupID;
            var rewardsToShow = settings.EmbedToggles.RewardsToShow;
            var (pk, raidEmbed) = RaidInfoCommand(seed, (int)crystalType, selectedMap, storyProgressLevel, raidDeliveryGroupID, rewardsToShow);
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
                StoryProgressLevel = (int)gameProgress,
                Seed = seed,
                IsCoded = true,
                IsShiny = pk.IsShiny,
                AddedByRACommand = false,
                Title = $"{(Species)pk.Species}",
            };
            // Check if Species is Ditto and set PartyPK to Showdown template
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
            var msg = $"✔ Tu nueva incursión ha sido agregada.";
            await ReplyAsync(msg, embed: raidEmbed).ConfigureAwait(false);
        }

        [Command("addUserRaid")]
        [Alias("aur", "ra")]
        [Summary("Adds new raid parameter next in the queue.")]
        public async Task AddNewRaidParamNext(
            [Summary("Seed")] string seed,
            [Summary("Difficulty Level (1-7)")] int level,
            [Summary("Story Progress Level")] int storyProgressLevel = 6,
            [Summary("Event (Optional)")] string? eventType = null)  // New argument for specifying an event
        {
            var botPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;
            // Check if raid requests are disabled by the host
            if (Hub.Config.RotatingRaidSV.RaidSettings.DisableRequests)
            {
                await ReplyAsync("⚠️ Actualmente, el anfitrión tiene deshabilitadas las solicitudes de incursión.").ConfigureAwait(false);
                return;
            }

            // Ensure Compatible Difficulty and Story Progress level
            var compatible = CheckProgressandLevel(level, storyProgressLevel);
            if (!compatible)
            {
                await ReplyAsync($"✘ La dificultad de la incursión requiere un {GetRequiredProgress(level)}.").ConfigureAwait(false);
                return;
            }

            // Check if the user already has a request
            var userId = Context.User.Id;
            if (Hub.Config.RotatingRaidSV.ActiveRaids.Any(r => r.RequestedByUserID == userId))
            {
                await ReplyAsync("✘ Ya tienes una solicitud de incursión existente en la cola.").ConfigureAwait(false);
                return;
            }
            var userRequestManager = new UserRequestManager();
            var userRoles = (Context.User as SocketGuildUser)?.Roles.Select(r => r.Id) ?? new List<ulong>();

            if (!Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.ContainsKey(userId) &&
                !userRoles.Any(roleId => Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.ContainsKey(roleId)))
            {
                if (!userRequestManager.CanRequest(userId, Hub.Config.RotatingRaidSV.RaidSettings.LimitRequests, Hub.Config.RotatingRaidSV.RaidSettings.LimitRequestsTime, out var remainingCooldown))
                {
                    string responseMessage = $"✘ Ha alcanzado su límite de solicitudes. Espere {remainingCooldown.TotalMinutes:N0} minutos antes de realizar otra solicitud.";

                    // Append the custom LimitRequestMsg if it's set
                    if (!string.IsNullOrWhiteSpace(Hub.Config.RotatingRaidSV.RaidSettings.LimitRequestMsg))
                    {
                        responseMessage += $"\n{Hub.Config.RotatingRaidSV.RaidSettings.LimitRequestMsg}";
                    }

                    await ReplyAsync(responseMessage).ConfigureAwait(false);
                    return;
                }
            }

            // Validate the seed for hexadecimal format
            if (seed.Length != 8 || !seed.All(c => "0123456789abcdefABCDEF".Contains(c)))
            {
                await ReplyAsync("⚠️ Formato de semilla no válido. Ingrese una semilla que consta de exactamente 8 dígitos hexadecimales.").ConfigureAwait(false);
                return;
            }

            if (level < 1 || level > 7)  // Adjusted level range to 1-7
            {
                await ReplyAsync("⚠️ Nivel de incursión no válido. Por favor introduzca un nivel entre 1 y 7.").ConfigureAwait(false);  // Adjusted message to reflect new level range
                return;
            }

            // Convert StoryProgressLevel to GameProgress enum value
            var gameProgress = ConvertToGameProgress(storyProgressLevel);
            if (gameProgress == GameProgress.None)
            {
                await ReplyAsync("⚠️ Nivel de progreso de la historia no válido. Por favor introduzca un valor entre 1 y 6.").ConfigureAwait(false);
                return;
            }

            // Get the settings object from Hub
            var settings = Hub.Config.RotatingRaidSV;

            var crystalType = level switch
            {
                >= 1 and <= 5 => eventType == "Evento" ? (TeraCrystalType)2 : (TeraCrystalType)0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("✘ Nivel de dificultad no válido.")
            };

            // Determine the correct Group ID based on event type
            var raidDeliveryGroupID = (level == 7) ? settings.EventSettings.MightyGroupID : settings.EventSettings.DistGroupID;

            // Check if event type is specified but events are turned off
            if (eventType == "Evento" && !settings.EventSettings.EventActive)
            {
                await ReplyAsync("⚠️ Lo sentimos, pero la configuración de eventos está desactivada en este momento o no hay eventos activos.").ConfigureAwait(false);
                return;
            }

            // If EventActive is true, force storyProgressLevel to 6
            if (settings.EventSettings.EventActive && storyProgressLevel != 6)
            {
                await ReplyAsync("⚠️ Actualmente, solo se permite el nivel 6 de progreso de la historia (6* desbloqueado) debido a la configuración del evento activo.").ConfigureAwait(false);
                return;
            }
            int effectiveQueuePosition = 1;
            var selectedMap = IsBlueberry ? TeraRaidMapParent.Blueberry : (IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);
            var rewardsToShow = settings.EmbedToggles.RewardsToShow;
            var (pk, raidEmbed) = RaidInfoCommand(seed, (int)crystalType, selectedMap, storyProgressLevel, raidDeliveryGroupID, rewardsToShow, effectiveQueuePosition);
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
                StoryProgressLevel = (int)gameProgress,
                Seed = seed,
                IsCoded = true,
                IsShiny = pk.IsShiny,
                AddedByRACommand = true,
                RequestCommand = $"{botPrefix}ra {seed} {level} {storyProgressLevel}{(eventType != null ? $" {eventType}" : "")}",
                RequestedByUserID = Context.User.Id,
                Title = $"Incursión solicitada por {Context.User.Username} {(eventType == "Event" ? " (Event Raid)" : "")}",
                RaidUpNext = false,
                User = Context.User,
            };

            // Check if Species is Ditto and set PartyPK to Showdown template
            if (newparam.Species == Species.Ditto)
            {
                newparam.PartyPK = new string[] {
                    "Happiny",
                    "Shiny: Yes",
                    "Level: 1"
                };
            }
            // Determine the correct position to insert the new raid after the current rotation
            int insertPosition = RotationCount + 1;
            while (insertPosition < Hub.Config.RotatingRaidSV.ActiveRaids.Count && Hub.Config.RotatingRaidSV.ActiveRaids[insertPosition].AddedByRACommand)
            {
                insertPosition++;
            }
            // Set RaidUpNext to true only if the new raid is inserted immediately next in the rotation
            if (insertPosition == RotationCount + 1)
            {
                newparam.RaidUpNext = true;
            }
            // After the new raid is inserted
            Hub.Config.RotatingRaidSV.ActiveRaids.Insert(insertPosition, newparam);

            // Adjust RotationCount
            if (insertPosition <= RotationCount)
            {
                RotationCount++;
            }

            // Calculate the user's position in the queue and the estimated wait time
            effectiveQueuePosition = CalculateEffectiveQueuePosition(Context.User.Id, RotationCount);
            int etaMinutes = effectiveQueuePosition * 6;

            var queuePositionMessage = effectiveQueuePosition > 0
                ? $"Actualmente estás en {effectiveQueuePosition} en la cola con un tiempo de espera estimado de {etaMinutes} minutos."
                : "¡Tu solicitud de incursión es la siguiente!";

            var replyMsg = $"{Context.User.Mention}, ¡Agregue tu incursión a la cola! Te enviaré un mensaje de texto cuando esté por comenzar.";
            await ReplyAsync(replyMsg, embed: raidEmbed).ConfigureAwait(false);

            try
            {
                var user = Context.User as SocketGuildUser;
                if (user != null)
                {
                    await user.SendMessageAsync($"Aquí está la información de tu incursión:\n{queuePositionMessage}\nEl comando que usaste para la solicitud: `{newparam.RequestCommand}`", false, raidEmbed).ConfigureAwait(false);
                }
                else
                {
                    await ReplyAsync("⚠️ No se pudo enviar DM. Asegúrese de que sus DM estén abiertos.").ConfigureAwait(false);
                }
            }
            catch
            {
                await ReplyAsync("⚠️ No se pudo enviar DM. Asegúrese de que sus DM estén abiertos.").ConfigureAwait(false);
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
                    return level >= 1 && level <= 4;
                case 3: // Unlocked 3 Stars
                    return level >= 1 && level <= 3;
                default: return false; // No 1 or 2 Star Unlocked
            }
        }

        public string GetRequiredProgress(int level)
        {
            return level switch
            {
                6 => "6☆ Unlocked Progress",
                5 => "5☆ Unlocked Progress",
                4 => "4☆ Unlocked Progress",
                3 => "3☆ Unlocked Progress",
                2 => "2☆ and 1☆ Unlocked Progress Not Allowed",
                _ => "1☆ Unlocked Progress Not Allowed",
            };
        }

        [Command("addRaidPK")]
        [Alias("rp")]
        [Summary("Adds provided showdown set Pokémon to the users Raid in Queue.")]
        public async Task AddRaidPK([Summary("Showdown Set")][Remainder] string content)
        {
            content = ReusableActions.StripCodeBlock(content);
            var set = new ShowdownSet(content);
            var template = AutoLegalityWrapper.GetTemplate(set);
            if (set.InvalidLines.Count != 0 || set.Species <= 0)
            {
                var msg = $"⚠️ No se puede analizar el conjunto showdown:\n{string.Join("\n", set.InvalidLines)}";
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
                    var reason = result == "Timeout" ? $"Ese conjunto {spec} tardó demasiado en generarse." : $"No pude crear una {spec} a partir de ese conjunto.";
                    var imsg = $"⚠️ Oops! {reason}";
                    await ReplyAsync(imsg).ConfigureAwait(false);
                    return;
                }

                var userId = Context.User.Id;
                var raidParameters = Hub.Config.RotatingRaidSV.ActiveRaids;
                var raidToUpdate = raidParameters.FirstOrDefault(r => r.RequestedByUserID == userId);
                string[] partyPK = content.Split('\n', StringSplitOptions.RemoveEmptyEntries); // Remove empty lines
                if (raidToUpdate != null)
                {
                    raidToUpdate.PartyPK = partyPK;
                    await Context.Message.DeleteAsync().ConfigureAwait(false);
                    var embed = RPEmbed.PokeEmbed(pkm, Context.User.Username);
                    await ReplyAsync(embed: embed).ConfigureAwait(false);
                }
                else
                {
                    var msg = "You don't have a raid in queue!";
                    await ReplyAsync(msg).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(RaidModule<T>));
                var msg = $"⚠️ Oops! Ocurrió un problema inesperado con este set de showdown.:\n```{string.Join("\n", set.GetSetLines())}```";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
        }

        [Command("addRaidPK")]
        [Alias("rp")]
        [Summary("Adds provided showdown set Pokémon to the users Raid in Queue.")]
        public async Task AddRaidPK()
        {
            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment == default)
            {
                await ReplyAsync("⚠️ ¡No se proporciona ningún archivo adjunto!").ConfigureAwait(false);
                return;
            }

            var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
            var pk = GetRequest(att);
            if (pk == null)
            {
                await ReplyAsync("✘ El archivo adjunto proporcionado no es compatible con este módulo!").ConfigureAwait(false);
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
                    var embed = RPEmbed.PokeEmbed(pk, Context.User.Username);
                    await ReplyAsync(embed: embed).ConfigureAwait(false);
                }
                else
                {
                    var msg = "⚠️ No tienes una incursión en cola!";
                    await ReplyAsync(msg).ConfigureAwait(false);
                }
            }
        }

        [Command("raidQueueStatus")]
        [Alias("rqs")]
        [Summary("Checks the number of raids before the user's request and gives an ETA.")]
        public async Task CheckQueueStatus()
        {
            var userId = Context.User.Id;
            int currentPosition = RotationCount;

            // Find the index of the user's request in the queue, excluding Mystery Shiny Raids
            var userRequestIndex = Hub.Config.RotatingRaidSV.ActiveRaids.FindIndex(r => r.RequestedByUserID == userId && !r.Title.Contains("✨ Incursión Shiny Misteriosa ✨"));

            EmbedBuilder embed = new EmbedBuilder();

            if (userRequestIndex == -1)
            {
                embed.Title = "Estado de la cola";
                embed.Color = Color.Red;
                embed.Description = $"{Context.User.Mention}, no tienes una solicitud de incursión en la cola.";
            }
            else
            {
                // Calculate the effective position of the user's request in the queue
                int raidsBeforeUser = CalculateEffectiveQueuePosition(userId, currentPosition);

                if (raidsBeforeUser <= 0)
                {
                    embed.Title = "Estado de la cola";
                    embed.Color = Color.Green;
                    embed.Description = $"{Context.User.Mention}, tu solicitud de incursión es la siguiente!";
                }
                else
                {
                    // Calculate ETA assuming each raid takes 6 minutes
                    int etaMinutes = raidsBeforeUser * 6;

                    embed.Title = "Estado de la cola";
                    embed.Color = Color.Orange;
                    embed.Description = $"{Context.User.Mention}, Este es el estado de tu solicitud de incursión:";
                    embed.AddField("Incursiones antes que las tuyas", raidsBeforeUser.ToString(), true);
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
        [Summary("Removes the raid added by the user.")]
        public async Task RemoveOwnRaidParam()
        {
            var userId = Context.User.Id;
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;

            // Find the raid added by the user
            var userRaid = list.FirstOrDefault(r => r.RequestedByUserID == userId && r.AddedByRACommand);
            if (userRaid == null)
            {
                await ReplyAsync("⚠️ No tienes una incursión agregada..").ConfigureAwait(false);
                return;
            }

            // Prevent canceling if the raid is up next
            if (userRaid.RaidUpNext)
            {
                await ReplyAsync("⚠️ Tu solicitud de incursión es la siguiente y no se puede cancelar en este momento..").ConfigureAwait(false);
                return;
            }

            // Remove the raid if it's not up next
            list.Remove(userRaid);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
            var msg = $"✔ Borraste tu incursión de la cola.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("removeRaidParams")]
        [Alias("rrp")]
        [Summary("Removes a raid parameter.")]
        [RequireSudo]
        public async Task RemoveRaidParam([Summary("Seed Index")] int index)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                list.RemoveAt(index);
                var msg = $"✔ Incursión de {raid.Title} | {raid.Seed:X8} ha sido eliminada!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("✘ Índice de parámetros de raid no válido.").ConfigureAwait(false);
        }

        [Command("toggleRaidParams")]
        [Alias("trp")]
        [Summary("Toggles raid parameter.")]
        [RequireSudo]
        public async Task ToggleRaidParam([Summary("Seed Index")] int index)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.ActiveInRotation = !raid.ActiveInRotation;
                var m = raid.ActiveInRotation ? "activado" : "desactivado";
                var msg = $"Incursión de {raid.Title} | {raid.Seed:X8} ha sido {m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("✘ Índice de parámetros de raid no válido.").ConfigureAwait(false);
        }

        [Command("togglecodeRaidParams")]
        [Alias("tcrp")]
        [Summary("Toggles code raid parameter.")]
        [RequireSudo]
        public async Task ToggleCodeRaidParam([Summary("Seed Index")] int index)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.IsCoded = !raid.IsCoded;
                var m = raid.IsCoded ? "Con Codigo" : "Sin Codigo";
                var msg = $"Incursión de {raid.Title} | {raid.Seed:X8} ahora es {m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("✘ Índice de parámetros de raid no válido.").ConfigureAwait(false);
        }

        [Command("changeRaidParamTitle")]
        [Alias("crpt")]
        [Summary("Changes the title of a  raid parameter.")]
        [RequireSudo]
        public async Task ChangeRaidParamTitle([Summary("Seed Index")] int index, [Summary("Title")] string title)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.Title = title;
                var msg = $"Título de incursión de {raid.Title} | {raid.Seed:X8} se ha cambiado a: {title}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("✘ Índice de parámetros de raid no válido.").ConfigureAwait(false);
        }

        [Command("viewraidList")]
        [Alias("vrl", "rotatinglist")]
        [Summary("Prints the raids in the current collection.")]
        public async Task GetRaidListAsync()
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            int count = list.Count;
            int fields = (int)Math.Ceiling((double)count / 15);
            var embed = new EmbedBuilder
            {
                Title = "Lista de incursiones"
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
                    fieldBuilder.AppendLine($"{paramNumber}.) {raid.Title} - {raid.Seed} - Status: {(raid.ActiveInRotation ? "Active" : "Inactive")}");
                }
                embed.AddField($"Lista de incursiones - Parte {i + 1}", fieldBuilder.ToString(), false);
            }
            await ReplyAsync($"📝 Estas son las redadas actualmente en la lista (total: {count}):", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("toggleRaidPK")]
        [Alias("trpk")]
        [Summary("Toggles raid parameter.")]
        [RequireSudo]
        public async Task ToggleRaidParamPK([Summary("Seed Index")] int index, [Summary("Showdown Set")][Remainder] string content)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.PartyPK = new[] { content };
                var m = string.Join("\n", raid.PartyPK);
                var msg = $"El PK de incursión para {raid.Title} | {raid.Seed:X8} se ha actualizado a:\n{m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("✘ Índice de parámetros de raid no válido.").ConfigureAwait(false);
        }

        [Command("raidhelp")]
        [Alias("rh")]
        [Summary("Prints the raid help command list.")]
        public async Task GetRaidHelpListAsync()
        {
            var embed = new EmbedBuilder();
            List<string> cmds = new()
            {
                "$ban - Prohibir a un usuario realizar raids a través de NID. [Command] [OT] - Sudo only command.\n",
                "$vrl - Ver todas las incursiones en la lista.\n",
                "$arp - Agregue parámetro a la colección.\nEx: [Command] [Index] [Species] [Difficulty]\n",
                "$rrp - Eliminar parámetro de la colección.\nEx: [Command] [Index]\n",
                "$trp - Cambie el parámetro como Activo/Inactivo en la colección.\nEx: [Command] [Index]\n",
                "$tcrp - Cambie el parámetro como codificado/sin codificar en la colección.\nEx: [Command] [Index]\n",
                "$trpk - Establece un Party PK para el parámetro mediante un set de enfrentamiento.\nEx: [Command] [Index] [ShowdownSet]\n",
                "$crpt - Establezca el título del parámetro.\nEx: [Command] [Index]"
            };
            string msg = string.Join("", cmds.ToList());
            embed.AddField(x =>
            {
                x.Name = "Comandos de ayuda para incursiones";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("✔ ¡Aquí está tu ayuda para la incursión!", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("unbanrotatingraider")]
        [Alias("ubrr")]
        [Summary("Removes the specificed NID from the banlist for Raids in SV.")]
        [RequireSudo]
        public async Task UnbanRotatingRaider([Summary("Removes the specificed NID from the banlist for Raids in SV.")] string nid)
        {
            var list = Hub.Config.RotatingRaidSV.RaiderBanList.List.ToArray();
            string msg = $"{Context.User.Mention} no se encontró ningún usuario con ese NID.";
            for (int i = 0; i < list.Length; i++)
                if ($"{list[i].ID}".Equals(nid))
                {
                    msg = $"{Context.User.Mention} el usuario {list[i].Name} - {list[i].ID} ha sido desbaneado.";
                    Hub.Config.RotatingRaidSV.RaiderBanList.List.ToList().Remove(list[i]);
                }
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        private RemoteControlAccess GetReference(ulong id, string name, string comment) => new()
        {
            ID = id,
            Name = name,
            Comment = "Baneado el " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" + $"({comment})"
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
    }
}