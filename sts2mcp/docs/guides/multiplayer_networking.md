# Multiplayer Networking for Mods

## Match The Real Multiplayer API

Use the decompiled message shape from the game, not older `StreamPeerBuffer` examples.

Custom messages should implement both `INetMessage` and `IPacketSerializable`:

```csharp
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

public sealed class MyMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public string Data { get; set; } = string.Empty;
    public int Amount { get; set; }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(Data);
        writer.WriteInt(Amount);
    }

    public void Deserialize(PacketReader reader)
    {
        Data = reader.ReadString();
        Amount = reader.ReadInt();
    }
}
```

The important details are:

- use `Mode`, not `TransferMode`
- use `PacketWriter` / `PacketReader`, not `StreamPeerBuffer`
- include `LogLevel` so the message follows the same pattern as the game's built-in messages

`generate_net_message` now emits this shape by default.

## Sending Messages

```csharp
var netService = RunManager.Instance.NetService;
if (netService != null)
{
    var msg = new MyMessage { Data = "hello", Amount = 1 };
    netService.SendMessage(msg);
}
```

## Receiving Messages

```csharp
// Register in ModEntry.Init():
RunManager.Instance.NetService?.RegisterMessageHandler<MyMessage>(OnReceived);

private static void OnReceived(MyMessage msg, int senderNetId)
{
    Log.Info($"Received from player {senderNetId}: {msg.Data} ({msg.Amount})");
}

// Unregister when done:
RunManager.Instance.NetService?.UnregisterMessageHandler<MyMessage>();
```

## Transfer Modes

- `Reliable` — guaranteed delivery, good for gameplay state
- `Unreliable` — may drop, useful for high-frequency ephemeral data
- `ReliableOrdered` — guaranteed and ordered when sequence matters

## Lists, Enums, And Optional Data

The built-in messages use the typed helpers on `PacketWriter` / `PacketReader` heavily:

```csharp
public void Serialize(PacketWriter writer)
{
    writer.WriteList(_events, 4);
    writer.WriteBool(drawingMode.HasValue);
    if (drawingMode.HasValue)
    {
        writer.WriteEnum(drawingMode.Value);
    }
}

public void Deserialize(PacketReader reader)
{
    _events = reader.ReadList<NetMapDrawingEvent>(4);
    if (reader.ReadBool())
    {
        drawingMode = reader.ReadEnum<DrawingMode>();
    }
}
```

Use the typed helpers whenever possible instead of hand-packing lengths and bytes.

## Message Batching

For high-frequency data, batch payloads rather than sending one tiny message per event:

```csharp
var batch = new MyBatchMessage();
foreach (var item in items)
{
    if (!batch.TryAdd(item))
    {
        netService.SendMessage(batch);
        batch = new MyBatchMessage();
        batch.TryAdd(item);
    }
}
netService.SendMessage(batch);
```

This matches the pattern used by messages such as `MapDrawingMessage`.

## Key Types

- `MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService` — send/receive entry point
- `MegaCrit.Sts2.Core.Multiplayer.Serialization.IPacketSerializable` — serialization contract
- `MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter` / `PacketReader` — typed network serialization
- `MegaCrit.Sts2.Core.Multiplayer.Transport.NetTransferMode` — delivery guarantees
- `RunManager.Instance.NetService` — runtime networking access
- `Player.NetId` — sender/player identifier
