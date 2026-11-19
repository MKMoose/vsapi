using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

#nullable disable

namespace Vintagestory.API.Server
{
    [JsonObject(MemberSerialization.OptIn)]
    public class PlayerGroup
    {
        [JsonProperty]
        public int Uid;
        [JsonProperty]
        public string Name;
        [JsonProperty]
        public string CreatedDate;
        [JsonProperty]
        public string OwnerUID;
        [System.Text.Json.Serialization.JsonIgnore]
        public List<ChatLine> ChatHistory;
        [JsonProperty]
        public string Md5Identifier;
        [JsonProperty]
        public bool CreatedByPrivateMessage;
        [JsonProperty]
        public string JoinPolicy = null;
        [System.Text.Json.Serialization.JsonIgnore]
        public List<IPlayer> OnlinePlayers = new List<IPlayer>();


        public PlayerGroup()
        {
            ChatHistory = new List<ChatLine>();
            CreatedDate = DateTime.Now.ToLongDateString();
        }
    }
}
