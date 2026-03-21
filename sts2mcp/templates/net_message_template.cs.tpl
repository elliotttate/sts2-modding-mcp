using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace {namespace}.Networking;

/// <summary>
/// Multiplayer network message. Register handler in ModEntry.Init():
///   RunManager.Instance.NetService?.RegisterMessageHandler<{class_name}>(OnReceived);
/// </summary>
public sealed class {class_name} : INetMessage, IPacketSerializable
{{
    public bool ShouldBroadcast => {should_broadcast};
    public NetTransferMode Mode => NetTransferMode.{transfer_mode};
    public LogLevel LogLevel => LogLevel.VeryDebug;

{fields_declarations}

    public void Serialize(PacketWriter writer)
    {{
{serialize_body}
    }}

    public void Deserialize(PacketReader reader)
    {{
{deserialize_body}
    }}
}}
