namespace Personify.Editor
{
    /// <summary>
    /// Hand-mirrored Backrooms vocabulary for the optional "Backrooms" panel: the brain archetype ids a designed NPC
    /// can bind to, and the biome ids. Kept in sync with Backrooms' EntityCatalog by hand.
    /// Purely presentational - the exported JSON stores the chosen strings;
    /// the Backrooms importer maps them.
    /// </summary>
    public static class BackroomsPreset
    {
        /// <summary>Brain archetypes a designed NPC can reuse (from Backrooms EntityCatalog).</summary>
        public static readonly string[] Archetypes =
        {
            "faceling",        // canonical passive resident (FacelingBrain)
            "wanderer_hollow", // silent hunter that looks harmless (DoppelBrain)
            "smiler",          // darkness dweller (SmilerBrain)
            "partygoer",       // friendly-then-not (PartygoerBrain)
            "mimic_regular",   // skin-stealer (SkinStealerBrain)
            "husk",            // deep-tier bundle monster (HuskBrain)
            "brute",           // tier-5 heavyweight (HuskBrain)
        };

        /// <summary>Biome ids (deterministic decay regions). Empty selection = any biome.</summary>
        public static readonly string[] Biomes =
        {
            "L0", "L1", "L2", "L3", "L4", "L6", "Lm2",
        };
    }
}
