// FILE: src/Solitaire.Cli/CliInfo.cs
using System;
using System.Reflection;

namespace Solitaire.Cli
{
    internal static class CliInfo
    {
        
        public static string Version()
        {
            try
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v != null ? v.ToString() : "1.2.0.0a";
            }
            catch
            {
                return "1.2.0.0e";
            }
        }
    }
}