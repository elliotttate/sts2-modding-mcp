using System.Collections.Generic;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace {namespace}.Characters;

public class {class_name} : CustomCharacterModel
{{
    public const string CharacterId = "{class_name}";

    // ── Theme Color ─────────────────────────────────────────────────────────
    public static readonly Color Color = new({color_literal});

    public override Color NameColor => Color;
    public override Color MapDrawingColor => Color;
    public override CharacterGender Gender => CharacterGender.{gender};

    // ── Stats ───────────────────────────────────────────────────────────────
    public override int StartingHp => {starting_hp};
    public override int StartingGold => {starting_gold};

    // ── Starter Deck & Relics ───────────────────────────────────────────────
    {starter_deck_block}

    {starter_relics_block}

    // ── Pool Models ─────────────────────────────────────────────────────────
    public override CardPoolModel CardPool => ModelDb.CardPool<{class_name}CardPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<{class_name}RelicPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<{class_name}PotionPool>();

    // ── Visual Asset Paths ──────────────────────────────────────────────────
    public override string CustomVisualPath => "res://{mod_name}/Characters/{class_name}/{snake_name}.tscn";
    public override string CustomTrailPath => "res://{mod_name}/Characters/{class_name}/card_trail_{snake_name}.tscn";
    public override string CustomIconPath => "res://{mod_name}/Characters/{class_name}/{snake_name}_icon.tscn";
    public override string CustomIconTexturePath => "res://{mod_name}/Characters/{class_name}/character_icon_{snake_name}.png";
    public override string CustomRestSiteAnimPath => "res://{mod_name}/Characters/{class_name}/{snake_name}_rest_site.tscn";
    public override string CustomMerchantAnimPath => "res://{mod_name}/Characters/{class_name}/{snake_name}_merchant.tscn";

    // ── Character Select ────────────────────────────────────────────────────
    public override string CustomCharacterSelectBg => "res://{mod_name}/Characters/{class_name}/char_select_bg_{snake_name}.tscn";
    public override string CustomCharacterSelectIconPath => "res://{mod_name}/Characters/{class_name}/char_select_{snake_name}.png";
    public override string CustomCharacterSelectLockedIconPath => "res://{mod_name}/Characters/{class_name}/char_select_{snake_name}_locked.png";
    public override string CustomMapMarkerPath => "res://{mod_name}/Characters/{class_name}/map_marker_{snake_name}.png";
    // public override string CustomCharacterSelectTransitionPath => "res://{mod_name}/Characters/{class_name}/{snake_name}_transition_mat.tres";

    // ── Energy Counter ──────────────────────────────────────────────────────
    // Option A: Programmatic energy counter with layer images and glow colors
    public override CustomEnergyCounter? CustomEnergyCounter =>
        new CustomEnergyCounter(
            i => $"res://{mod_name}/Characters/{class_name}/energy_counters/{snake_name}_orb_layer_" + i + ".png",
            Color, Color.Lightened(0.3f));
    // Option B: Use a scene instead (comment out Option A, uncomment this)
    // public override string? CustomEnergyCounterPath => "res://{mod_name}/Characters/{class_name}/{snake_name}_energy_counter.tscn";

    // ── Multiplayer Hand Gestures ───────────────────────────────────────────
    // public override string CustomArmPointingTexturePath => "res://{mod_name}/Characters/{class_name}/hands/multiplayer_hand_{snake_name}_point.png";
    // public override string CustomArmRockTexturePath => "res://{mod_name}/Characters/{class_name}/hands/multiplayer_hand_{snake_name}_rock.png";
    // public override string CustomArmPaperTexturePath => "res://{mod_name}/Characters/{class_name}/hands/multiplayer_hand_{snake_name}_paper.png";
    // public override string CustomArmScissorsTexturePath => "res://{mod_name}/Characters/{class_name}/hands/multiplayer_hand_{snake_name}_scissors.png";

    // -- Animation & Audio --
    public override float AttackAnimDelay => {attack_anim_delay}f;
    public override float CastAnimDelay => {cast_anim_delay}f;
    // The game auto-generates FMOD event paths from the character ID:
    //   attack: event:/sfx/characters/{snake_name}/{snake_name}_attack
    //   cast:   event:/sfx/characters/{snake_name}/{snake_name}_cast
    //   death:  event:/sfx/characters/{snake_name}/{snake_name}_die
    //   select: event:/sfx/characters/{snake_name}/{snake_name}_select
    // These won't exist in the game's banks for custom characters.
    // Use BaseLib's FmodAudio to provide custom sounds:
    //   FmodAudio.RegisterFileReplacement("event:/sfx/characters/{snake_name}/{snake_name}_attack", "path/to/attack.wav");
    // Or build an FMOD bank with events at those paths using sts2-fmod-tools.

    public override CreatureAnimator? SetupCustomAnimationStates(MegaSprite controller)
    {{
        return SetupAnimationState(controller, "Idle", hitName: "Hit");
    }}

    // ── Extra Assets (preload VFX textures, stance auras, etc.) ─────────────
    // protected override IEnumerable<string> ExtraAssetPaths => [ ];

    // ── Architect Attack VFX (for Architect encounters) ─────────────────────
    // public override List<string> GetArchitectAttackVfx() => [
    //     "vfx/vfx_attack_blunt", "vfx/vfx_heavy_blunt", "vfx/vfx_attack_slash",
    //     "vfx/vfx_bloody_impact", "vfx/vfx_rock_shatter"
    // ];
}}

// ── Card Pool ───────────────────────────────────────────────────────────────
public sealed class {class_name}CardPool : CustomCardPoolModel
{{
    public override string Title => {class_name}.CharacterId;
    public override float H => {card_hue}f;  // Hue shift (0-1, e.g. 0.75 = purple)
    public override float S => 1f;
    public override float V => 1f;
    public override Color DeckEntryCardColor => {class_name}.Color;
    public override bool IsColorless => false;
    public override string? BigEnergyIconPath => "res://{mod_name}/Characters/{class_name}/ui/{snake_name}_energy_icon.png";
    public override string? TextEnergyIconPath => "res://{mod_name}/Characters/{class_name}/ui/text_{snake_name}_energy_icon.png";
}}

// ── Relic Pool ──────────────────────────────────────────────────────────────
public class {class_name}RelicPool : CustomRelicPoolModel
{{
    public override string EnergyColorName => {class_name}.CharacterId;
    public override Color LabOutlineColor => {class_name}.Color;
}}

// ── Potion Pool ─────────────────────────────────────────────────────────────
public class {class_name}PotionPool : CustomPotionPoolModel
{{
    public override string EnergyColorName => {class_name}.CharacterId;
    public override Color LabOutlineColor => {class_name}.Color;
}}
