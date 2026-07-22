using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Personify.Editor
{
    /// <summary>
    /// One avatar layer in the editable project. Exactly one of <see cref="Path"/> (an existing in-game layer) or
    /// <see cref="Source"/> (a project-relative custom PNG, e.g. "sources/grin.png") is used. Colours are hex strings.
    /// </summary>
    public sealed class LayerDraft
    {
        [JsonProperty("path")] public string Path { get; set; } = "";
        [JsonProperty("source")] public string Source { get; set; } = "";
        [JsonProperty("tint")] public string Tint { get; set; } = "#FFFFFF";
        /// <summary>Editor-only visibility toggle (the "eye"). Hidden layers are skipped in the live preview and left
        /// out of the exported pack. Defaults on; omitted-from-JSON stays true.</summary>
        [JsonProperty("visible")] public bool Visible { get; set; } = true;
    }

    /// <summary>One bone's (or mesh's) extreme distortion: non-uniform scale, or fully hidden. Keyed in
    /// <see cref="AppearanceDraft.Distortion"/> by bone name (see DooDesch.AvatarKit.AvatarDistortion.BoneKeys) or
    /// mesh pseudo-key ("FaceMesh", "BodyMesh0", ...). Not expressible in vanilla AvatarSettings - applied as a
    /// separate pass after the normal appearance load.</summary>
    public sealed class BoneDistortionDraft
    {
        [JsonProperty("scaleX")] public float ScaleX { get; set; } = 1f;
        [JsonProperty("scaleY")] public float ScaleY { get; set; } = 1f;
        [JsonProperty("scaleZ")] public float ScaleZ { get; set; } = 1f;
        [JsonProperty("hide")] public bool Hide { get; set; } = false;
    }

    /// <summary>The full editable avatar look (mirror of the vanilla AvatarSettings knobs). Colours are hex strings.</summary>
    public sealed class AppearanceDraft
    {
        [JsonProperty("gender")] public float Gender { get; set; } = 0f;
        [JsonProperty("height")] public float Height { get; set; } = 0.98f;
        [JsonProperty("weight")] public float Weight { get; set; } = 0.4f;
        [JsonProperty("skinColor")] public string SkinColor { get; set; } = "#96785F";
        [JsonProperty("hairPath")] public string HairPath { get; set; } = "";
        [JsonProperty("hairColor")] public string HairColor { get; set; } = "#101014";
        [JsonProperty("eyebrowScale")] public float EyebrowScale { get; set; } = 1f;
        [JsonProperty("eyebrowThickness")] public float EyebrowThickness { get; set; } = 1f;
        [JsonProperty("eyebrowRestingHeight")] public float EyebrowRestingHeight { get; set; } = 0f;
        [JsonProperty("eyebrowRestingAngle")] public float EyebrowRestingAngle { get; set; } = 0f;
        [JsonProperty("leftEyeLidColor")] public string LeftEyeLidColor { get; set; } = "#96785F";
        [JsonProperty("rightEyeLidColor")] public string RightEyeLidColor { get; set; } = "#96785F";
        [JsonProperty("leftEyeTop")] public float LeftEyeTop { get; set; } = 0.5f;
        [JsonProperty("leftEyeBottom")] public float LeftEyeBottom { get; set; } = 0.5f;
        [JsonProperty("rightEyeTop")] public float RightEyeTop { get; set; } = 0.5f;
        [JsonProperty("rightEyeBottom")] public float RightEyeBottom { get; set; } = 0.5f;
        [JsonProperty("eyeballMaterial")] public string EyeballMaterial { get; set; } = "Default";
        [JsonProperty("eyeBallTint")] public string EyeBallTint { get; set; } = "#FFFFFF";
        [JsonProperty("pupilDilation")] public float PupilDilation { get; set; } = 1f;
        [JsonProperty("faceLayers")] public List<LayerDraft> FaceLayers { get; set; } = new List<LayerDraft>();
        [JsonProperty("bodyLayers")] public List<LayerDraft> BodyLayers { get; set; } = new List<LayerDraft>();
        [JsonProperty("accessories")] public List<LayerDraft> Accessories { get; set; } = new List<LayerDraft>();
        /// <summary>Experimental-tab extreme body distortion. Keyed by bone/mesh name; sparse (untouched NPC = empty).</summary>
        [JsonProperty("distortion")] public Dictionary<string, BoneDistortionDraft> Distortion { get; set; } = new Dictionary<string, BoneDistortionDraft>();
    }

    /// <summary>S1API-expressible behaviour/stat defaults (only exported when <see cref="Enabled"/>).</summary>
    public sealed class BehaviorDraft
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = false;
        [JsonProperty("aggression")] public float Aggression { get; set; } = 0f;
        [JsonProperty("maxHealth")] public float MaxHealth { get; set; } = 100f;
        [JsonProperty("scale")] public float Scale { get; set; } = 1f;
        [JsonProperty("conversation")] public string Conversation { get; set; } = "none";
    }

    /// <summary>The Backrooms-consumer extension: exported into <c>extensions.backrooms</c> when <see cref="Enabled"/>.</summary>
    public sealed class BackroomsDraft
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = false;
        [JsonProperty("archetype")] public string Archetype { get; set; } = "faceling";
        [JsonProperty("tierMin")] public int TierMin { get; set; } = 1;
        [JsonProperty("tierMax")] public int TierMax { get; set; } = 5;
        /// <summary>Comma-separated biome ids (e.g. "L0,L1"); empty = any.</summary>
        [JsonProperty("biomes")] public string Biomes { get; set; } = "";
        [JsonProperty("weight")] public float Weight { get; set; } = 10f;
        [JsonProperty("maxAlive")] public int MaxAlive { get; set; } = 1;
        [JsonProperty("hostile")] public bool Hostile { get; set; } = false;
        [JsonProperty("ambient")] public bool Ambient { get; set; } = true;
        [JsonProperty("spawnCooldown")] public float SpawnCooldown { get; set; } = 0f;
    }

    /// <summary>One NPC being designed inside a project.</summary>
    public sealed class NpcDraft
    {
        [JsonProperty("id")] public string Id { get; set; } = "npc";
        [JsonProperty("name")] public string Name { get; set; } = "New NPC";
        [JsonProperty("appearance")] public AppearanceDraft Appearance { get; set; } = new AppearanceDraft();
        [JsonProperty("behavior")] public BehaviorDraft Behavior { get; set; } = new BehaviorDraft();
        [JsonProperty("backrooms")] public BackroomsDraft Backrooms { get; set; } = new BackroomsDraft();

        // Personnel 2.0 world-data blocks. The editor has no UI for these (authored by hand or by a
        // generator); they are carried opaquely through load/save/export so nothing gets lost.
        [JsonProperty("saveId", NullValueHandling = NullValueHandling.Ignore)] public string SaveId { get; set; }
        [JsonProperty("spawn", NullValueHandling = NullValueHandling.Ignore)] public JToken Spawn { get; set; }
        [JsonProperty("contact", NullValueHandling = NullValueHandling.Ignore)] public JToken Contact { get; set; }
        [JsonProperty("relationships", NullValueHandling = NullValueHandling.Ignore)] public JToken Relationships { get; set; }
        [JsonProperty("customer", NullValueHandling = NullValueHandling.Ignore)] public JToken Customer { get; set; }
        [JsonProperty("dealer", NullValueHandling = NullValueHandling.Ignore)] public JToken Dealer { get; set; }
        [JsonProperty("inventory", NullValueHandling = NullValueHandling.Ignore)] public JToken Inventory { get; set; }
        [JsonProperty("schedule", NullValueHandling = NullValueHandling.Ignore)] public JToken Schedule { get; set; }
    }

    /// <summary>
    /// An NPC pack in progress. Persisted as project.json; the editable source of truth. Exports (Personnel pack +
    /// Thunderstore wrapper) are derived from this.
    /// </summary>
    public sealed class NpcProject
    {
        [JsonProperty("formatVersion")] public int FormatVersion { get; set; } = 1;
        [JsonProperty("name")] public string Name { get; set; } = "My NPC Pack";
        [JsonProperty("author")] public string Author { get; set; } = "";
        [JsonProperty("modVersion")] public string ModVersion { get; set; } = "1.0.0";
        [JsonProperty("description")] public string Description { get; set; } = "";
        [JsonProperty("websiteUrl")] public string WebsiteUrl { get; set; } = "";
        [JsonProperty("license")] public string License { get; set; } = "All rights reserved";
        [JsonProperty("iconSource")] public string IconSource { get; set; } = "";

        // Personnel 2.0 pack-level fields, carried opaquely (no editor UI): stable pack identity for id
        // derivation, and the auto-register default for all NPCs in the pack.
        [JsonProperty("packId", NullValueHandling = NullValueHandling.Ignore)] public string PackId { get; set; }
        [JsonProperty("autoRegister", NullValueHandling = NullValueHandling.Ignore)] public bool? AutoRegister { get; set; }

        [JsonProperty("npcs")] public List<NpcDraft> Npcs { get; set; } = new List<NpcDraft>();

        [JsonIgnore] public string FolderName { get; set; }
    }
}
