using System;
using System.Collections.Generic;
using Telegram.Bot.Types;

namespace FlypackBot.Application.Commands
{
    public static class L10nCommands
    {
        private const string ENGLISH_START = "start";
        private const string ENGLISH_PACKAGES = "packages";
        private const string ENGLISH_CHANGE_PASSWORD = "change_password";
        private const string ENGLISH_STOP = "stop";

        private const string SPANISH_START = "iniciar";
        private const string SPANISH_PACKAGES = "paquetes";
        private const string SPANISH_CHANGE_PASSWORD = "cambiar_clave";
        private const string SPANISH_STOP = "detener";

        private const string FRENCH_START = "debut";
        private const string FRENCH_PACKAGES = "paquets";
        private const string FRENCH_CHANGE_PASSWORD = "changer_le_passe";
        private const string FRENCH_STOP = "arret";

        public static IEnumerable<BotCommand> English
        {
            get
            {
                return new[]
                {
                    new BotCommand
                    {
                        Command = ENGLISH_START,
                        Description = "Starts bot's operations, login into Flypack's platform"
                    },
                    new BotCommand
                    {
                        Command = ENGLISH_PACKAGES,
                        Description = "Returns an updated list of packages pending delivery"
                    },
                    new BotCommand
                    {
                        Command = ENGLISH_CHANGE_PASSWORD,
                        Description = "Allows you to update your Flypack's password"
                    },
                    new BotCommand
                    {
                        Command = ENGLISH_STOP,
                        Description = "Stops bot's operations, deleting any data associated with your Flypack's user"
                    }
                };
            }
        }

        public static IEnumerable<BotCommand> Spanish
        {
            get
            {
                return new[]
                {
                    new BotCommand
                    {
                        Command = SPANISH_START,
                        Description = "Inicia las operaciones del bot, iniciando sesión en la plataforma de Flypack"
                    },
                    new BotCommand
                    {
                        Command = SPANISH_PACKAGES,
                        Description = "Retorna una lista con todos tus paquetes pendientes de entrega"
                    },
                    new BotCommand
                    {
                        Command = SPANISH_CHANGE_PASSWORD,
                        Description = "Permite actualizar la contraseña del usuario con el que bot está logueado en la plataforma de Flypack"
                    },
                    new BotCommand
                    {
                        Command = SPANISH_STOP,
                        Description = "Detiene las operaciones del bot, eliminando a su vez todos los datos asociados con tu usuario"
                    }
                };
            }
        }

        public static IEnumerable<BotCommand> French
        {
            get
            {
                return new[]
                {
                    new BotCommand
                    {
                        Command = FRENCH_START,
                        Description = "Démarre les opérations du bot, connectez-vous à la plateforme Flypack"
                    },
                    new BotCommand
                    {
                        Command = FRENCH_PACKAGES,
                        Description = "Renvoie une liste mise à jour des colis en attente de livraison"
                    },
                    new BotCommand
                    {
                        Command = FRENCH_CHANGE_PASSWORD,
                        Description = "Permet de mettre à jour le mot de passe de votre Flypack"
                    },
                    new BotCommand
                    {
                        Command = FRENCH_STOP,
                        Description = "Arrête les opérations du bot, supprimant toutes les données associées à l'utilisateur de votre Flypack"
                    }
                };
            }
        }

        public static string Normalize(string text)
        {
            switch (text)
            {
                case ENGLISH_START: case FRENCH_START: case SPANISH_START:
                    return "/start";

                case ENGLISH_STOP: case FRENCH_STOP: case SPANISH_STOP:
                    return "/stop";

                case ENGLISH_PACKAGES: case FRENCH_PACKAGES: case SPANISH_PACKAGES:
                    return "/packages";

                case ENGLISH_CHANGE_PASSWORD: case FRENCH_CHANGE_PASSWORD: case SPANISH_CHANGE_PASSWORD:
                    return "/change_password";

                default: return "";
            }
        }
    }
}

