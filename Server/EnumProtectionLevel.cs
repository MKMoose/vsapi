
#nullable disable
namespace Vintagestory.API.Server
{
    /// <summary>
    /// How strongly the Server should protect against hacking
    /// </summary>
    public enum EnumProtectionLevel
    {
        /// <summary>
        /// No verification or protection of any kind
        /// </summary>
        Off = 0,
        /// <summary>
        /// Verify, but only log the violations, no action will be taken
        /// </summary>
        LogOnly = 3,
        /// <summary>
        /// Server will: verify the players picking range when placing, removing or interacting with blocks
        /// </summary>
        Basic = 1,
        /// <summary>
        /// Not used yet
        /// </summary>
        Pedantic = 2,
        /// <summary>
        /// Off in SinglePlayer
        /// LogOnly on OpenToLan or Dedicated servers that are whitelisted or password protected
        /// Basic on dedicated servers that are neither whitelisted nor password protected
        /// Default as of 1.22.8
        /// </summary>
        Auto = 4
    }

    public static class EnumProtectionLevelExtension
    {
        /// <summary>
        /// Should moderation action take place like preventing interactions, kicking or banning
        /// </summary>
        /// <param name="state"></param>
        /// <returns>true for Basic, Pedantic</returns>
        public static bool ShouldModerate(this EnumProtectionLevel state)
        {
            return state == EnumProtectionLevel.Basic || state == EnumProtectionLevel.Pedantic;
        }

        /// <summary>
        /// If a violation is detected, should the server log it
        /// </summary>
        /// <param name="state"></param>
        /// <returns>true for LogOnly,Basic,Pedantic</returns>
        public static bool ShouldLog(this EnumProtectionLevel state)
        {
            return state == EnumProtectionLevel.LogOnly || state == EnumProtectionLevel.Basic || state == EnumProtectionLevel.Pedantic;
        }
    }
}
