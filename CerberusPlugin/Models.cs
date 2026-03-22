using System;
using System.Collections.Generic;

namespace CerberusPlugin
{
    public class SharedState
    {
        public List<VerifiedUser> Verified { get; set; } = new();
        public List<PendingToken> Pending { get; set; } = new();
    }

    public class VerifiedUser
    {
        public ulong DiscordID { get; set; }
        public int TsAccountID { get; set; }
        public string Role { get; set; } = string.Empty;
    }

    public class PendingToken
    {
        public string TsName { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public ulong DiscordID { get; set; }
        public DateTime Expiry { get; set; }
    }
}