﻿using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon
{
    public abstract class PokeRoutineExecutor<T> : PokeRoutineExecutorBase where T : PKM, new()
    {
        protected PokeRoutineExecutor(IConsoleBotManaged<IConsoleConnection, IConsoleConnectionAsync> cfg) : base(cfg)
        {
        }

        public abstract Task<T> ReadPokemon(ulong offset, CancellationToken token);

        public abstract Task<T> ReadPokemon(ulong offset, int size, CancellationToken token);

        protected async Task<(bool, ulong)> ValidatePointerAll(IEnumerable<long> jumps, CancellationToken token)
        {
            var solved = await SwitchConnection.PointerAll(jumps, token).ConfigureAwait(false);
            return (solved != 0, solved);
        }

        public async Task VerifyBotbaseVersion(CancellationToken token)
        {
            var data = await SwitchConnection.GetBotbaseVersion(token).ConfigureAwait(false);
            var version = decimal.TryParse(data, CultureInfo.InvariantCulture, out var v) ? v : 0;
            if (version < BotbaseVersion)
            {
                var protocol = Config.Connection.Protocol;
                var msg = protocol is SwitchProtocol.WiFi ? "sys-botbase" : "usb-botbase";
                msg += $" La versión no es compatible. Versión esperada {BotbaseVersion} o mayor, y la versión actual es {version}. Descargue la última versión desde: ";
                if (protocol is SwitchProtocol.WiFi)
                    msg += "https://github.com/olliz0r/sys-botbase/releases/latest";
                else
                    msg += "https://github.com/Koi-3088/usb-botbase/releases/latest";
                throw new Exception(msg);
            }
        }

        // Check if either Tesla or dmnt are active if the sanity check for Trainer Data fails, as these are common culprits.
        private const ulong ovlloaderID = 0x420000000007e51a; // Tesla Menu

        private const ulong dmntID = 0x010000000000000d;      // dmnt used for cheats

        public async Task CheckForRAMShiftingApps(CancellationToken token)
        {
            Log("Los datos del entrenador no son válidos.");

            bool found = false;
            var msg = "";
            if (await SwitchConnection.IsProgramRunning(ovlloaderID, token).ConfigureAwait(false))
            {
                msg += "Menú Tesla encontrado";
                found = true;
            }

            if (await SwitchConnection.IsProgramRunning(dmntID, token).ConfigureAwait(false))
            {
                if (found)
                    msg += " y ";
                msg += "dmnt (¿códigos de trucos?)";
                found = true;
            }
            if (found)
            {
                msg += ".";
                Log(msg);
                Log("Por favor, elimine las aplicaciones que interfieren y reinicie la cansola.");
            }
        }
    }
}