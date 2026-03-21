using System.Collections.Generic;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;

namespace {namespace}.Characters;

public sealed class {class_name} : CustomCharacterModel
{{
    // Pool models must be static readonly and instantiated here
    public static readonly {class_name}CardPool CardPool = new();
    public static readonly {class_name}RelicPool RelicPool = new();
    public static readonly {class_name}PotionPool PotionPool = new();

    public override CardPoolModel CardPoolModel => CardPool;

    // Visual Assets
    public override string VisualsPath => "res://{mod_name}/Characters/{class_name}/{snake_name}.tscn";
    public override string SelectScreenBgPath => "res://{mod_name}/Characters/{class_name}/select_bg.png";
    public override string EnergyCounterPath => "res://{mod_name}/Characters/{class_name}/energy_counter.tscn";

    // Character Info
    public override int StartingMaxHp => {starting_hp};
    public override int StartingGold => {starting_gold};
    public override int OrbSlots => {orb_slots};

    // Override these for custom animations/SFX:
    // public override string AttackSfx => "res://...";
    // public override string CastSfx => "res://...";
    // public override string DeathSfx => "res://...";

    protected override IReadOnlyList<CardModel> StarterDeck()
    {{
        var deck = new List<CardModel>();
        // TODO: Add starter cards
        // deck.Add(ModelDb.Card<YourStarterCard>().ToMutable());
        return deck;
    }}

    protected override IReadOnlyList<RelicModel> StarterRelics()
    {{
        var relics = new List<RelicModel>();
        // TODO: Add starter relics
        return relics;
    }}
}}

// Card Pool
public sealed class {class_name}CardPool : CustomCardPoolModel
{{
    // Override for custom card frames, energy icons, etc.
}}

// Relic Pool
public sealed class {class_name}RelicPool : CustomRelicPoolModel {{ }}

// Potion Pool
public sealed class {class_name}PotionPool : CustomPotionPoolModel {{ }}
