using RT.Common;
using Server.Common;

namespace RT.Models
{
    /// <summary>
    /// Sent as request to retrieve version string of current connected Medius Server
    /// </summary>
    [MediusMessage(NetMessageClass.MessageClassLobby, MediusLobbyMessageIds.VersionServer)]
    public class MediusVersionServerRequest : BaseLobbyMessage, IMediusRequest
    {
        public override byte PacketType => (byte)MediusLobbyMessageIds.VersionServer;

        /// <summary>
        /// Message ID
        /// </summary>
        public MessageId MessageID { get; set; }
        /// <summary>
        /// Session Key
        /// </summary>
        public string SessionKey; // SESSIONKEY_MAXLEN

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            MessageID = reader.Read<MessageId>();

            // 
            SessionKey = reader.ReadString(Constants.SESSIONKEY_MAXLEN);
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            // 
            base.Serialize(writer);

            //
            writer.Write(MessageID ?? MessageId.Empty);

            // 
            writer.Write(SessionKey, Constants.SESSIONKEY_MAXLEN);
        }

        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID: {MessageID} " +
                $"SessionKey: {SessionKey}";
        }
    }
}