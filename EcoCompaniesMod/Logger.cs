using System;

namespace Eco.Mods.Companies
{
    using Shared.Localization;
    using Shared.Utils;

    public static class Logger
    {
        public static void Debug(string message)
        {
            Log.Write(new LocString("[Companies] DEBUG: " + message + "\n"));
        }

        public static void Info(string message)
        {
            Log.Write(new LocString("[Companies] " + message + "\n"));
        }

        public static void Error(string message)
        {
            Log.Write(new LocString("[Companies] ERROR: " + message + "\n"));
        }
    }
}