﻿using RT.Common;
using Server.Common;

namespace RT.Models
{
    [MediusMessage(NetMessageClass.MessageClassLobby, MediusLobbyMessageIds.ConfirmClanTeamChallenge)]
    public class MediusConfirmClanTeamChallengeRequest : BaseLobbyMessage, IMediusRequest
    {
        public override byte PacketType => (byte)MediusLobbyMessageIds.ConfirmClanTeamChallenge;



        public MessageId MessageID { get; set; }
        public string SessionKey; // SESSIONKEY_MAXLEN
        public int ClanChallengeID;

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            // 
            base.Deserialize(reader);

            // 
            MessageID = reader.Read<MessageId>();
            SessionKey = reader.ReadString(Constants.SESSIONKEY_MAXLEN);
            reader.ReadBytes(2);
            ClanChallengeID = reader.ReadInt32();
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            // 
            base.Serialize(writer);

            // 
            writer.Write(MessageID ?? MessageId.Empty);
            writer.Write(SessionKey, Constants.SESSIONKEY_MAXLEN);
            writer.Write(new byte[2]);
            writer.Write(ClanChallengeID);
        }


        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID:{MessageID}" + " " +
                $"SessionKey:{SessionKey}" + " " +
                $"ClanChallengeID:{ClanChallengeID}";
        }
    }
}