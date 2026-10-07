using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Customers;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Harvest;
using ScrapYardKing.Player;
using ScrapYardKing.Progression;
using ScrapYardKing.Workers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Art pass 1 — characters. Replaces every Kenney character with custom segmented "toy" characters (ArtKit/ToyRig):
// the player (yellow hard hat, orange vest, utility pack, a custom circular scrap cutter in three upgrade tiers), the five
// workers (shared body, own colours and kit) and five customers. Animation is generated clips on the existing Animator
// parameters (Speed / Cutting / Happy), so no gameplay code changes.
// Entry points: Preview (renders a lineup to a PNG, touches nothing), Build (clips, controllers, prefabs, icons).
public static class Art_Characters
{
    const string P = "Assets/_Project";
    const string PrefabDir = P + "/Prefabs";
    const string DataDir = P + "/Data";

    static readonly string[] BladePaths =
    {
        ToyRig.ForearmR + "/HandR/Cutter/Tier1/Blade", ToyRig.ForearmR + "/HandR/Cutter/Tier2/Blade", ToyRig.ForearmR + "/HandR/Cutter/Tier3/Blade",
    };

    static readonly StringBuilder Log = new();

    // =====================================================================================
    // Specs
    // =====================================================================================

    static class Pal
    {
        public static Material SkinLight => Mat("SkinLight", new Color(1f, 0.79f, 0.65f), 0.32f, 0f, Detail.Plain);
        public static Material SkinTan => Mat("SkinTan", new Color(0.93f, 0.66f, 0.47f), 0.32f, 0f, Detail.Plain);
        public static Material SkinBrown => Mat("SkinBrown", new Color(0.7f, 0.46f, 0.3f), 0.32f, 0f, Detail.Plain);
        public static Material SkinDark => Mat("SkinDark", new Color(0.45f, 0.29f, 0.2f), 0.34f, 0f, Detail.Plain);
        public static Material Eye => Mat("Eye", new Color(0.07f, 0.06f, 0.07f), 0.85f, 0f, Detail.Plain);
        public static Material White => Mat("EyeGlint", Color.white, 0.9f, 0f, Detail.Plain, 1f, new Color(0.6f, 0.6f, 0.6f));
        public static Material Mouth => Mat("Mouth", new Color(0.38f, 0.13f, 0.12f), 0.4f, 0f, Detail.Plain);
        public static Material Blush => Mat("Blush", new Color(1f, 0.6f, 0.53f), 0.2f, 0f, Detail.Plain);
        public static Material HairBrown => Mat("HairBrown", new Color(0.3f, 0.18f, 0.1f), 0.3f, 0f, Detail.Cloth, 2f);
        public static Material HairBlack => Mat("HairBlack", new Color(0.11f, 0.1f, 0.1f), 0.35f, 0f, Detail.Cloth, 2f);
        public static Material HairBlonde => Mat("HairBlonde", new Color(0.93f, 0.74f, 0.38f), 0.3f, 0f, Detail.Cloth, 2f);
        public static Material HairGinger => Mat("HairGinger", new Color(0.78f, 0.36f, 0.15f), 0.3f, 0f, Detail.Cloth, 2f);
        public static Material HairGray => Mat("HairGray", new Color(0.74f, 0.74f, 0.74f), 0.3f, 0f, Detail.Cloth, 2f);
        public static Material ShirtCream => Mat("ShirtCream", new Color(0.95f, 0.93f, 0.86f), 0.18f, 0f, Detail.Cloth, 2f);
        public static Material ShirtSky => Mat("ShirtSky", new Color(0.66f, 0.8f, 0.95f), 0.18f, 0f, Detail.Cloth, 2f);
        public static Material ShirtGrey => Mat("ShirtGrey", new Color(0.36f, 0.38f, 0.42f), 0.18f, 0f, Detail.Cloth, 2f);
        public static Material ShirtTeal => Mat("ShirtTeal", new Color(0.16f, 0.62f, 0.62f), 0.2f, 0f, Detail.Cloth, 2f);
        public static Material ShirtPurple => Mat("ShirtPurple", new Color(0.52f, 0.32f, 0.78f), 0.2f, 0f, Detail.Cloth, 2f);
        public static Material ShirtPink => Mat("ShirtPink", new Color(0.97f, 0.55f, 0.62f), 0.2f, 0f, Detail.Cloth, 2f);
        public static Material ShirtRed => Mat("ShirtRed", new Color(0.86f, 0.22f, 0.18f), 0.2f, 0f, Detail.Cloth, 2f);
        public static Material ShirtWhite => Mat("ShirtWhite", new Color(0.97f, 0.97f, 0.97f), 0.2f, 0f, Detail.Cloth, 2f);
        public static Material ShirtGreen => Mat("ShirtGreen", new Color(0.3f, 0.66f, 0.3f), 0.2f, 0f, Detail.Cloth, 2f);
        public static Material PantsNavy => Mat("PantsNavy", new Color(0.13f, 0.22f, 0.5f), 0.18f, 0f, Detail.Cloth, 2f);
        public static Material PantsDenim => Mat("PantsDenim", new Color(0.26f, 0.42f, 0.7f), 0.18f, 0f, Detail.Cloth, 2f);
        public static Material PantsKhaki => Mat("PantsKhaki", new Color(0.78f, 0.66f, 0.45f), 0.18f, 0f, Detail.Cloth, 2f);
        public static Material PantsBlack => Mat("PantsBlack", new Color(0.17f, 0.17f, 0.2f), 0.18f, 0f, Detail.Cloth, 2f);
        public static Material PantsBrown => Mat("PantsBrown", new Color(0.42f, 0.3f, 0.2f), 0.18f, 0f, Detail.Cloth, 2f);
        public static Material Boots => Mat("BootsBrown", new Color(0.46f, 0.28f, 0.15f), 0.32f, 0f, Detail.Rubber, 2f);
        public static Material Sneakers => Mat("SneakersWhite", new Color(0.95f, 0.95f, 0.95f), 0.4f, 0f, Detail.Rubber, 2f);
        public static Material SneakersRed => Mat("SneakersRed", new Color(0.88f, 0.22f, 0.18f), 0.4f, 0f, Detail.Rubber, 2f);
        public static Material Gloves => Mat("GlovesDark", new Color(0.2f, 0.2f, 0.23f), 0.25f, 0f, Detail.Rubber, 2f);
        public static Material GlovesYellow => Mat("GlovesYellow", new Color(0.95f, 0.75f, 0.2f), 0.3f, 0f, Detail.Rubber, 2f);
        public static Material GlovesLeather => Mat("GlovesLeather", new Color(0.6f, 0.38f, 0.2f), 0.28f, 0f, Detail.Rubber, 2f);
        public static Material HatYellow => Mat("HatYellow", new Color(1f, 0.78f, 0.08f), 0.6f, 0f, Detail.Paint, 1.5f);
        public static Material HatOrange => Mat("HatOrange", new Color(1f, 0.5f, 0.08f), 0.6f, 0f, Detail.Paint, 1.5f);
        public static Material HatWhite => Mat("HatWhite", new Color(0.96f, 0.96f, 0.94f), 0.6f, 0f, Detail.Paint, 1.5f);
        public static Material HatRed => Mat("HatRed", new Color(0.88f, 0.2f, 0.14f), 0.6f, 0f, Detail.Paint, 1.5f);
        public static Material HatBlue => Mat("HatBlue", new Color(0.18f, 0.45f, 0.88f), 0.4f, 0f, Detail.Cloth, 2f);
        public static Material HatGreen => Mat("HatGreen", new Color(0.24f, 0.72f, 0.3f), 0.4f, 0f, Detail.Cloth, 2f);
        public static Material VestOrange => Mat("VestOrange", new Color(1f, 0.45f, 0.08f), 0.22f, 0f, Detail.Cloth, 2f);
        public static Material VestLime => Mat("VestLime", new Color(0.78f, 0.93f, 0.16f), 0.22f, 0f, Detail.Cloth, 2f);
        public static Material VestBlue => Mat("VestBlue", new Color(0.2f, 0.5f, 0.92f), 0.22f, 0f, Detail.Cloth, 2f);
        public static Material VestRed => Mat("VestRed", new Color(0.9f, 0.24f, 0.16f), 0.22f, 0f, Detail.Cloth, 2f);
        public static Material Stripe => Mat("ReflectiveStripe", new Color(0.98f, 0.95f, 0.82f), 0.7f, 0f, Detail.Plain, 1f, new Color(0.18f, 0.17f, 0.14f));
        public static Material StripeGrey => Mat("ReflectiveGrey", new Color(0.78f, 0.8f, 0.82f), 0.7f, 0.2f, Detail.Plain, 1f, new Color(0.12f, 0.12f, 0.12f));
        public static Material PackGrey => Mat("PackGrey", new Color(0.4f, 0.44f, 0.5f), 0.25f, 0f, Detail.Cloth, 2f);
        public static Material PackDark => Mat("PackDark", new Color(0.18f, 0.19f, 0.22f), 0.25f, 0f, Detail.Cloth, 2f);
        public static Material Steel => Mat("ToolSteel", new Color(0.72f, 0.75f, 0.8f), 0.6f, 0.3f, Detail.Metal, 2f);
        public static Material Charcoal => Mat("CharcoalPlastic", new Color(0.16f, 0.17f, 0.19f), 0.35f, 0f, Detail.Rubber, 2f);
        public static Material Belt => Mat("BeltLeather", new Color(0.32f, 0.2f, 0.12f), 0.3f, 0f, Detail.Rubber, 2f);
        public static Material ApronGreen => Mat("ApronGreen", new Color(0.22f, 0.62f, 0.3f), 0.22f, 0f, Detail.Cloth, 2f);
        public static Material ApronTeal => Mat("ApronTeal", new Color(0.1f, 0.6f, 0.62f), 0.22f, 0f, Detail.Cloth, 2f);
        public static Material ApronRed => Mat("ApronRed", new Color(0.84f, 0.2f, 0.2f), 0.22f, 0f, Detail.Cloth, 2f);
        public static Material VestPurple => Mat("VestPurple", new Color(0.56f, 0.3f, 0.86f), 0.22f, 0f, Detail.Cloth, 2f);
        public static Material HatTeal => Mat("HatTeal", new Color(0.1f, 0.62f, 0.64f), 0.6f, 0f, Detail.Paint, 1.5f);
        public static Material ApronLeather => Mat("ApronLeather", new Color(0.55f, 0.33f, 0.18f), 0.3f, 0f, Detail.Rubber, 2f);
        public static Material Lens => Mat("GoggleLens", new Color(1f, 0.55f, 0.15f), 0.9f, 0f, Detail.Plain, 1f, new Color(0.9f, 0.4f, 0.08f));
    }

    static ToySpec Base(string name, Material skin, Material hair)
    {
        var s = new ToySpec { Name = name };
        s[CS.Skin] = skin;
        s[CS.Eye] = Pal.Eye;
        s[CS.White] = Pal.White;
        s[CS.Mouth] = Pal.Mouth;
        s[CS.Blush] = Pal.Blush;
        s[CS.Hair] = hair;
        s[CS.Steel] = Pal.Steel;
        s[CS.Charcoal] = Pal.Charcoal;
        s[CS.Belt] = Pal.Belt;
        s[CS.Gloves] = Pal.Gloves;
        s[CS.Boots] = Pal.Boots;
        s[CS.Pants] = Pal.PantsNavy;
        s[CS.Shirt] = Pal.ShirtCream;
        s[CS.Stripe] = Pal.Stripe;
        s[CS.Pack] = Pal.PackGrey;
        s[CS.PackDark] = Pal.PackDark;
        s[CS.Lens] = Pal.Lens;
        return s;
    }

    static ToySpec PlayerSpec()
    {
        var s = Base("Player", Pal.SkinLight, Pal.HairBrown);
        s[CS.Hat] = Pal.HatYellow;
        s[CS.HatAccent] = Pal.VestOrange;
        s[CS.Vest] = Pal.VestOrange;
        s.Back = BackStyle.UtilityPack;
        s.Bulk = 1.08f;
        return s;
    }

    static Dictionary<string, ToySpec> WorkerSpecs()
    {
        var porter = Base("Porter", Pal.SkinTan, Pal.HairBlack);
        porter[CS.Hat] = Pal.HatOrange;
        porter[CS.HatAccent] = Pal.PackDark;
        porter[CS.Vest] = Pal.VestLime;
        porter[CS.Stripe] = Pal.StripeGrey;
        porter[CS.Shirt] = Pal.ShirtSky;
        porter[CS.Gloves] = Pal.GlovesYellow;
        porter.Back = BackStyle.CarryRack;

        var helper = Base("Helper", Pal.SkinBrown, Pal.HairBlack);
        helper.Hat = HatStyle.Cap;
        helper[CS.Hat] = Pal.HatBlue;
        helper[CS.HatAccent] = Pal.HatBlue;
        helper[CS.Vest] = Pal.VestBlue;
        helper[CS.Shirt] = Pal.ShirtWhite;
        helper[CS.Pants] = Pal.PantsKhaki;
        helper.Back = BackStyle.CarryRack;
        helper.Bulk = 0.95f;

        var hauler = Base("Hauler", Pal.SkinLight, Pal.HairGinger);
        hauler[CS.Hat] = Pal.HatWhite;
        hauler[CS.HatAccent] = Pal.VestRed;
        hauler[CS.Vest] = Pal.VestRed;
        hauler[CS.Shirt] = Pal.ShirtGrey;
        hauler[CS.Gloves] = Pal.GlovesLeather;
        hauler.EarMuffs = true;
        hauler.Beard = true;
        hauler.Bulk = 1.2f;
        hauler.Belly = 0.04f;
        hauler.Back = BackStyle.CarryRack;

        var runner = Base("Runner", Pal.SkinDark, Pal.HairBlack);
        runner.Hat = HatStyle.Cap;
        runner[CS.Hat] = Pal.HatGreen;
        runner[CS.HatAccent] = Pal.HatYellow;
        runner.Vest = false;
        runner.Apron = true;
        runner.Pouch = true;
        runner[CS.Apron] = Pal.ApronGreen;
        runner[CS.Shirt] = Pal.ShirtCream;
        runner[CS.Pants] = Pal.PantsBlack;
        runner[CS.Gloves] = Pal.SkinDark;
        runner.Back = BackStyle.CarryRack;
        runner.Bulk = 0.95f;

        var smelter = Base("Smelter", Pal.SkinTan, Pal.HairBrown);
        smelter[CS.Hat] = Pal.HatRed;
        smelter[CS.HatAccent] = Pal.Charcoal;
        smelter.Vest = false;
        smelter.Apron = true;
        smelter.Goggles = true;
        smelter.Mustache = true;
        smelter[CS.Apron] = Pal.ApronLeather;
        smelter[CS.Shirt] = Pal.ShirtGrey;
        smelter[CS.Pants] = Pal.PantsBrown;
        smelter[CS.Gloves] = Pal.GlovesLeather;
        smelter.Back = BackStyle.CarryRack;
        smelter.Bulk = 1.1f;

        // M7 specialists. Operator: white hard hat, teal bib, ear muffs. Seller: shopkeeper with a red apron and a
        // mustache. Loader: broad, sleeveless, purple vest, beanie.
        var op = Base("Operator", Pal.SkinBrown, Pal.HairBlack);
        op[CS.Hat] = Pal.HatWhite;
        op[CS.HatAccent] = Pal.HatTeal;
        op.Vest = false;
        op.Apron = true;
        op.EarMuffs = true;
        op[CS.Apron] = Pal.ApronTeal;
        op[CS.Shirt] = Pal.ShirtWhite;
        op[CS.Pants] = Pal.PantsNavy;
        op[CS.Gloves] = Pal.GlovesYellow;

        var seller = Base("Seller", Pal.SkinLight, Pal.HairBlonde);
        seller.Hat = HatStyle.None;
        seller.Vest = false;
        seller.Apron = true;
        seller.Mustache = true;
        seller.Pouch = true;
        seller[CS.Apron] = Pal.ApronRed;
        seller[CS.Shirt] = Pal.ShirtWhite;
        seller[CS.Pants] = Pal.PantsBlack;
        seller[CS.Gloves] = Pal.SkinLight;
        seller[CS.Boots] = Pal.Sneakers;
        seller.Belly = 0.05f;

        var loader = Base("Loader", Pal.SkinDark, Pal.HairBlack);
        loader.Hat = HatStyle.Beanie;
        loader[CS.Hat] = Pal.PackDark;
        loader[CS.HatAccent] = Pal.HatYellow;
        loader[CS.Vest] = Pal.VestPurple;
        loader[CS.Shirt] = Pal.ShirtGrey;
        loader[CS.Pants] = Pal.PantsKhaki;
        loader[CS.Gloves] = Pal.GlovesLeather;
        loader.Sleeves = false;
        loader.Beard = true;
        loader.Bulk = 1.25f;
        loader.Back = BackStyle.CarryRack;

        return new Dictionary<string, ToySpec>
        {
            ["Worker_Porter"] = porter, ["Worker_Helper"] = helper, ["Worker_Hauler"] = hauler, ["Worker_Runner"] = runner, ["Worker_Smelter"] = smelter,
            ["Worker_Operator"] = op, ["Worker_Seller"] = seller, ["Worker_Loader"] = loader,
        };
    }

    static Dictionary<string, ToySpec> CustomerSpecs()
    {
        ToySpec Civ(string name, Material skin, Material hair, Material shirt, Material pants, Material shoes)
        {
            var s = Base(name, skin, hair);
            s.Hat = HatStyle.None;
            s.Vest = false;
            s[CS.Shirt] = shirt;
            s[CS.Pants] = pants;
            s[CS.Boots] = shoes;
            s[CS.Gloves] = skin;
            s.Bulk = 0.92f;
            return s;
        }

        var a = Civ("CustomerA", Pal.SkinLight, Pal.HairBrown, Pal.ShirtTeal, Pal.PantsDenim, Pal.Sneakers);
        a.Hair = HairStyle.Long;
        var c = Civ("CustomerC", Pal.SkinBrown, Pal.HairBlack, Pal.ShirtPurple, Pal.PantsBlack, Pal.SneakersRed);
        c.Hair = HairStyle.Bun;
        var d = Civ("CustomerD", Pal.SkinTan, Pal.HairGray, Pal.ShirtGreen, Pal.PantsBrown, Pal.Boots);
        d.Hair = HairStyle.Bald;
        d.Mustache = true;
        d.Belly = 0.06f;
        d.Bulk = 1f;
        var e = Civ("CustomerE", Pal.SkinLight, Pal.HairGinger, Pal.ShirtRed, Pal.PantsDenim, Pal.Sneakers);
        e.Hat = HatStyle.Beanie;
        e[CS.Hat] = Pal.HatBlue;
        e[CS.HatAccent] = Pal.ShirtWhite;
        e.Beard = true;
        e.Bulk = 1f;
        var f = Civ("CustomerF", Pal.SkinDark, Pal.HairBlack, Pal.ShirtWhite, Pal.PantsKhaki, Pal.SneakersRed);
        f.Hat = HatStyle.Cap;
        f[CS.Hat] = Pal.HatRed;
        f[CS.HatAccent] = Pal.HatRed;
        f.Bulk = 1f;
        return new Dictionary<string, ToySpec>
        {
            ["Customer_female-a"] = a, ["Customer_female-c"] = c, ["Customer_male-f"] = f, ["Customer_female-d"] = d, ["Customer_female-e"] = e,
        };
    }

    // =====================================================================================
    // Cutter (three upgrade tiers)
    // =====================================================================================

    enum TS { Body, Accent, Dark, Steel, Blade, Glow, Hose, Count }

    /// <summary>
    /// Circular scrap cutter held two-handed: rear grip at the origin (right hand), motor housing, side handle (left
    /// hand), gear arm and a big disc blade with a half guard. Forward = +Z, blade spins about local X.
    /// </summary>
    static Transform BuildCutter(Transform hand, Transform character)
    {
        // Built level with the character (forward, upright) at the hand of the posed rig, then parented to the hand,
        // so the hold pose carries it straight ahead whatever the arm angles are.
        var root = new GameObject("Cutter").transform;
        root.SetPositionAndRotation(hand.position + character.TransformVector(new Vector3(0f, -0.02f, 0.02f)), character.rotation);
        root.SetParent(hand, true);

        var tiers = new[]
        {
            (name: "Tier1", body: Mat("CutterOrange", Orange, 0.5f, 0.05f, Detail.Paint, 2f), accent: Mat("CutterGrey", Gray, 0.45f, 0.2f, Detail.Metal, 2f),
                radius: 0.25f, glow: false, hoses: false),
            (name: "Tier2", body: Mat("CutterYellow", Yellow, 0.55f, 0.05f, Detail.Paint, 2f), accent: Mat("CutterOrangeAccent", Orange, 0.5f, 0.05f, Detail.Paint, 2f),
                radius: 0.3f, glow: false, hoses: true),
            (name: "Tier3", body: Mat("CutterRed", new Color(0.86f, 0.14f, 0.1f), 0.6f, 0.1f, Detail.Paint, 2f),
                accent: Mat("CutterGold", new Color(1f, 0.78f, 0.25f), 0.7f, 0.4f, Detail.Metal, 2f), radius: 0.35f, glow: true, hoses: true),
        };
        var dark = Mat("CutterDark", Charcoal, 0.35f, 0f, Detail.Rubber, 2f);
        var steel = Mat("CutterSteel", new Color(0.78f, 0.8f, 0.84f), 0.7f, 0.35f, Detail.Metal, 2f);
        var blade = Mat("CutterBlade", new Color(0.84f, 0.86f, 0.9f), 0.75f, 0.35f, Detail.Metal, 3f);
        var glow = Mat("CutterHotEdge", new Color(1f, 0.55f, 0.15f), 0.6f, 0f, Detail.Plain, 1f, new Color(2.2f, 0.9f, 0.2f));
        var hose = Mat("CutterHose", new Color(0.12f, 0.12f, 0.14f), 0.4f, 0f, Detail.Rubber, 2f);

        Transform tipParent = null;
        float tipRadius = 0f;
        foreach (var tier in tiers)
        {
            var table = new Material[(int)TS.Count];
            table[(int)TS.Body] = tier.body;
            table[(int)TS.Accent] = tier.accent;
            table[(int)TS.Dark] = dark;
            table[(int)TS.Steel] = steel;
            table[(int)TS.Blade] = blade;
            table[(int)TS.Glow] = glow;
            table[(int)TS.Hose] = hose;
            var t = new GameObject(tier.name).transform;
            t.SetParent(root, false);
            float r = tier.radius;
            float bladeZ = 0.36f + r;

            var k = new MeshKit();
            // Rear grip (right hand) and trigger guard.
            k.Cylinder((int)TS.Dark, new Vector3(0f, 0.02f, -0.02f), 0.038f, 0.2f, 10, new Vector3(-12f, 0f, 0f), 0.012f);
            k.Box((int)TS.Dark, new Vector3(0f, 0.12f, 0.03f), new Vector3(0.06f, 0.04f, 0.14f), 0.015f);
            // Motor housing with cooling fins and a stripe.
            k.Box((int)TS.Body, new Vector3(0f, 0.08f, 0.15f), new Vector3(0.2f, 0.2f, 0.26f), 0.06f, default, 2);
            k.Box((int)TS.Accent, new Vector3(0f, 0.08f, 0.15f), new Vector3(0.212f, 0.05f, 0.2f), 0.02f);
            for (int i = 0; i < 4; i++) k.Box((int)TS.Dark, new Vector3(-0.106f, 0.12f, 0.07f + i * 0.045f), new Vector3(0.012f, 0.06f, 0.025f), 0.004f);
            k.Cylinder((int)TS.Dark, new Vector3(0f, 0.08f, 0.015f), 0.07f, 0.03f, 14, new Vector3(90f, 0f, 0f), 0.01f);
            // Side handle for the left hand.
            k.Cylinder((int)TS.Dark, new Vector3(-0.17f, 0.1f, 0.22f), 0.034f, 0.16f, 10, new Vector3(0f, 0f, 90f), 0.01f);
            k.Sphere((int)TS.Accent, new Vector3(-0.255f, 0.1f, 0.22f), new Vector3(0.04f, 0.045f, 0.045f), 10, 6);
            // Gear arm to the blade.
            k.Box((int)TS.Steel, new Vector3(0.065f, 0.04f, 0.26f + r * 0.5f), new Vector3(0.06f, 0.1f, 0.24f + r), 0.025f);
            k.Cylinder((int)TS.Steel, new Vector3(0.065f, 0.04f, bladeZ), 0.06f, 0.07f, 14, new Vector3(0f, 0f, 90f), 0.015f);
            // Half guard over the back/top of the blade.
            k.Push(new Vector3(0.02f, 0.04f, bladeZ), new Vector3(0f, 0f, 90f));
            k.Lathe((int)TS.Body, Vector3.zero, new[] { new Vector2(r * 0.92f, -0.035f), new Vector2(r + 0.04f, -0.035f), new Vector2(r + 0.04f, 0.035f),
                new Vector2(r * 0.92f, 0.035f) }, 20, default, false, 45f, true, 225f, 195f);
            k.Pop();
            if (tier.hoses)
            {
                k.Torus((int)TS.Hose, new Vector3(0.1f, 0.12f, 0.3f), 0.09f, 0.018f, 12, 6, new Vector3(0f, 0f, 90f), null, 180f, 0f);
                k.Cylinder((int)TS.Accent, new Vector3(0.1f, 0.2f, 0.12f), 0.035f, 0.12f, 10, new Vector3(90f, 0f, 0f), 0.01f);
            }

            if (tier.glow)
            {
                // Twin exhausts and a pressure gauge on the heavy tier.
                for (int side = -1; side <= 1; side += 2)
                    k.Cylinder((int)TS.Steel, new Vector3(side * 0.06f, 0.2f, 0.06f), 0.022f, 0.1f, 8, default, 0.006f);
                k.Cylinder((int)TS.Accent, new Vector3(-0.1f, 0.15f, 0.12f), 0.035f, 0.02f, 12, new Vector3(0f, 0f, 90f), 0.006f);
            }

            ArtAssets.Part("Cutter", tier.name, k, table, t, objectName: "Housing");

            // Blade: separate object so the Cut clip can spin it.
            var bk = new MeshKit();
            bk.Cylinder((int)TS.Blade, Vector3.zero, r, 0.022f, 28, new Vector3(0f, 0f, 90f), 0.006f);
            int teeth = Mathf.RoundToInt(r * 90f);
            for (int i = 0; i < teeth; i++)
            {
                float a = 360f * i / teeth;
                var dir = Quaternion.Euler(a, 0f, 0f) * Vector3.up;
                bk.Box(tier.glow ? (int)TS.Glow : (int)TS.Steel, dir * (r + 0.012f), new Vector3(0.026f, 0.03f, 0.022f), 0.004f, new Vector3(a + 20f, 0f, 0f));
            }

            // Dark slots make the spin readable.
            for (int i = 0; i < 3; i++)
            {
                float a = 120f * i;
                var dir = Quaternion.Euler(a, 0f, 0f) * Vector3.up;
                bk.Box((int)TS.Dark, dir * r * 0.55f, new Vector3(0.026f, r * 0.42f, 0.03f), 0.01f, new Vector3(a, 0f, 0f));
            }

            bk.Cylinder((int)TS.Dark, Vector3.zero, 0.055f, 0.04f, 12, new Vector3(0f, 0f, 90f), 0.01f);
            bk.Cylinder((int)TS.Accent, Vector3.zero, 0.025f, 0.05f, 8, new Vector3(0f, 0f, 90f), 0.005f);
            ArtAssets.Part("Cutter", tier.name + "_Blade", bk, table, t, new Vector3(0f, 0.04f, bladeZ), objectName: "Blade");
            if (tier.name == "Tier2")
            {
                tipParent = t;
                tipRadius = r;
            }
        }

        // Spark point at the front-bottom edge of the mid-tier blade (where it bites into scrap).
        var tip = new GameObject("ToolTip").transform;
        tip.SetParent(root, false);
        tip.localPosition = new Vector3(0f, 0.04f - tipRadius * 0.55f, 0.36f + tipRadius * 1.75f);
        _ = tipParent;
        return root;
    }

    // =====================================================================================
    // Entry points
    // =====================================================================================

    /// <summary>Renders every character in a lineup (game-camera angle and a front view) into <paramref name="outDir"/>.</summary>
    public static string Preview(string outDir)
    {
        Log.Clear();
        var clips = ToyRig.BuildClips(BladePaths);
        var scene = EditorSceneManager.NewPreviewScene();
        var specs = new List<ToySpec> { PlayerSpec() };
        specs.AddRange(WorkerSpecs().Values);
        specs.AddRange(CustomerSpecs().Values);
        var row = new GameObject("Lineup");
        SceneManager.MoveGameObjectToScene(row, scene);
        for (int i = 0; i < specs.Count; i++)
        {
            var holder = new GameObject(specs[i].Name).transform;
            holder.SetParent(row.transform, false);
            holder.localPosition = new Vector3((i - (specs.Count - 1) * 0.5f) * 1.05f, 0f, 0f);
            var parts = ToyRig.Build(specs[i], holder, null);
            clips.Idle.SampleAnimation(parts.Model, 0.4f);
            if (i == 0)
            {
                clips.Hold.SampleAnimation(parts.Model, 0.2f);
                var cutter = BuildCutter(parts.HandR, holder);
                cutter.Find("Tier1").gameObject.SetActive(false);
                cutter.Find("Tier3").gameObject.SetActive(false);
            }
            else if (i % 3 == 1) clips.Walk.SampleAnimation(parts.Model, 0.15f);
        }

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        SceneManager.MoveGameObjectToScene(ground, scene);
        ground.transform.localScale = new Vector3(3f, 1f, 1f);
        ground.GetComponent<Renderer>().sharedMaterial = Mat("PreviewGround", new Color(0.8f, 0.74f, 0.62f), 0.1f, 0f, Detail.Concrete);

        Directory.CreateDirectory(outDir);
        RenderPreview(scene, Path.Combine(outDir, "chars_game.png"), new Vector3(50f, 0f, 0f), new Vector3(0f, 0.9f, 0f), 6.6f, 1600, 700);
        RenderPreview(scene, Path.Combine(outDir, "chars_front.png"), new Vector3(12f, 200f, 0f), new Vector3(0f, 0.95f, 0f), 6.2f, 1600, 600);
        RenderPreview(scene, Path.Combine(outDir, "chars_player.png"), new Vector3(25f, 210f, 0f), new Vector3(row.transform.GetChild(0).position.x + 0.1f, 1.0f, 0.2f), 1.3f, 800, 800);
        EditorSceneManager.ClosePreviewScene(scene);
        return "preview rendered to " + outDir + "\n" + Log;
    }

    static void RenderPreview(Scene scene, string path, Vector3 euler, Vector3 target, float width, int w, int h)
    {
        var camGo = new GameObject("PreviewCam");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = scene;
        cam.orthographic = true;
        cam.orthographicSize = width * h / w * 0.5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        var rot = Quaternion.Euler(euler);
        camGo.transform.SetPositionAndRotation(target - rot * Vector3.forward * 20f, rot);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 60f;
        var sun = new GameObject("Sun");
        SceneManager.MoveGameObjectToScene(sun, scene);
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        light.color = new Color(1f, 0.95f, 0.85f);
        light.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
        var fill = new GameObject("Fill");
        SceneManager.MoveGameObjectToScene(fill, scene);
        var fl = fill.AddComponent<Light>();
        fl.type = LightType.Directional;
        fl.intensity = 0.5f;
        fl.color = new Color(0.7f, 0.8f, 1f);
        fill.transform.rotation = Quaternion.Euler(30f, 150f, 0f);

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(sun);
        Object.DestroyImmediate(fill);
    }

    /// <summary>Clips, controllers, the player / worker / customer prefabs (edited in place) and their icons.</summary>
    public static string Build()
    {
        Log.Clear();
        var clips = ToyRig.BuildClips(BladePaths);

        // Controllers. The player's arm mask comes from a real rig with the cutter attached (paths must exist).
        var maskHolder = new GameObject("MaskRig");
        var maskParts = ToyRig.Build(PlayerSpec(), maskHolder.transform, null);
        clips.Hold.SampleAnimation(maskParts.Model, 0f);
        BuildCutter(maskParts.HandR, maskHolder.transform);
        var playerController = ToyRig.PlayerController(clips, maskParts.Model, new[] { ToyRig.ArmL, ToyRig.ArmR });
        Object.DestroyImmediate(maskHolder);
        var workerController = ToyRig.WorkerController(clips);
        var customerController = ToyRig.CustomerController(clips);

        BuildPlayer(playerController);
        var workers = WorkerSpecs();
        foreach (var kv in workers) BuildWorker(kv.Key, kv.Value, workerController);
        foreach (var kv in CustomerSpecs()) BuildCustomer(kv.Key, kv.Value, customerController);

        // Icons for the hire tiles and the cutter upgrade card.
        foreach (var (prefab, asset, icon) in new[]
                 {
                     ("Worker_Porter", "Worker_Porter", "Icon_Porter"), ("Worker_Helper", "Worker_Helper", "Icon_Helper"),
                     ("Worker_Hauler", "Worker_Hauler", "Icon_Hauler"), ("Worker_Runner", "Worker_Runner", "Icon_Runner"),
                     ("Worker_Smelter", "Worker_Smelter", "Icon_Smelter"),
                     ("Worker_Operator", "Worker_OperatorCrusher", "Icon_Operator"), ("Worker_Operator", "Worker_OperatorSorter", "Icon_Operator"),
                     ("Worker_Operator", "Worker_OperatorFurnace", "Icon_Operator"), ("Worker_Seller", "Worker_SellerYard", "Icon_Seller"),
                     ("Worker_Seller", "Worker_SellerMarket", "Icon_Seller"), ("Worker_Loader", "Worker_Loader", "Icon_Loader"),
                 })
        {
            var def = AssetDatabase.LoadAssetAtPath<WorkerDefinition>($"{DataDir}/Workers/{asset}.asset");
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Workers/{prefab}.prefab");
            if (def != null && go != null) ArtAssets.Set(def, ("icon", ArtIcons.Render(icon, go, new Vector3(12f, 165f, 0f), ArtIcons.Pose(clips.Idle, 0.3f), 0.62f)));
        }

        var cutterHolder = new GameObject("CutterIcon");
        var cutter = BuildCutter(cutterHolder.transform, cutterHolder.transform);
        cutter.Find("Tier1").gameObject.SetActive(false);
        cutter.Find("Tier3").gameObject.SetActive(false);
        var saw = AssetDatabase.LoadAssetAtPath<PlayerStatUpgradeDefinition>($"{DataDir}/Progression/Upgrade_Chainsaw.asset");
        var tmpPrefab = ArtAssets.SavePrefab(cutterHolder, "Assets/_Project/Prefabs/Player/_IconCutter.prefab");
        if (saw != null) ArtAssets.Set(saw, ("icon", ArtIcons.Render("Icon_Chainsaw", tmpPrefab, new Vector3(20f, -60f, 0f), null, 0.78f)));
        AssetDatabase.DeleteAsset("Assets/_Project/Prefabs/Player/_IconCutter.prefab");

        // Backpack upgrade: the utility pack with a stack of scrap pieces. Boots upgrade: a pair of chunky work boots.
        var packHolder = new GameObject("PackIcon");
        var spec = PlayerSpec();
        var packK = new MeshKit();
        packK.Box((int)CS.Pack, new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.5f, 0.22f), 0.08f);
        packK.Box((int)CS.PackDark, new Vector3(0f, 0.42f, 0.01f), new Vector3(0.52f, 0.16f, 0.24f), 0.05f);
        packK.Box((int)CS.PackDark, new Vector3(0f, 0.16f, 0.12f), new Vector3(0.3f, 0.2f, 0.06f), 0.03f);
        packK.Box((int)CS.HatAccent, new Vector3(0f, 0.3f, 0.125f), new Vector3(0.48f, 0.05f, 0.02f), 0.01f);
        ArtAssets.Part("Icons", "Pack", packK, spec.Table, packHolder.transform);
        var pieces = AssetDatabase.LoadAllAssetsAtPath($"{ArtAssets.MeshDir}/ScrapPieces.asset").OfType<Mesh>().ToArray();
        for (int i = 0; i < Mathf.Min(4, pieces.Length); i++)
            ArtAssets.MeshObject("Piece" + i, packHolder.transform, pieces[i], new[] { ArtPalette.Material }, new Vector3(0f, 0.6f + i * 0.18f, -0.05f),
                new Vector3(0f, i * 40f, i % 2 == 0 ? 8f : -6f));
        var backpack = AssetDatabase.LoadAssetAtPath<PlayerStatUpgradeDefinition>($"{DataDir}/Progression/Upgrade_Backpack.asset");
        var packPrefab = ArtAssets.SavePrefab(packHolder, "Assets/_Project/Prefabs/Player/_IconPack.prefab");
        if (backpack != null) ArtAssets.Set(backpack, ("icon", ArtIcons.Render("Icon_Backpack", packPrefab, new Vector3(18f, 200f, 0f), null, 0.8f)));
        AssetDatabase.DeleteAsset("Assets/_Project/Prefabs/Player/_IconPack.prefab");

        var bootHolder = new GameObject("BootIcon");
        var bootK = new MeshKit();
        for (int side = -1; side <= 1; side += 2)
        {
            bootK.Push(new Vector3(side * 0.16f, 0f, side * 0.05f), new Vector3(0f, side * 8f, 0f));
            bootK.Box((int)CS.Boots, new Vector3(0f, 0.22f, -0.04f), new Vector3(0.22f, 0.3f, 0.24f), 0.07f);
            bootK.Box((int)CS.Boots, new Vector3(0f, 0.09f, 0.06f), new Vector3(0.22f, 0.15f, 0.36f), 0.06f);
            bootK.Box((int)CS.Hat, new Vector3(0f, 0.09f, 0.2f), new Vector3(0.23f, 0.13f, 0.1f), 0.04f);
            bootK.Box((int)CS.Charcoal, new Vector3(0f, 0.015f, 0.05f), new Vector3(0.23f, 0.04f, 0.38f), 0.015f);
            bootK.Box((int)CS.Hat, new Vector3(0f, 0.37f, -0.04f), new Vector3(0.24f, 0.05f, 0.26f), 0.02f);
            bootK.Pop();
        }

        ArtAssets.Part("Icons", "Boots", bootK, spec.Table, bootHolder.transform);
        var boots = AssetDatabase.LoadAssetAtPath<PlayerStatUpgradeDefinition>($"{DataDir}/Progression/Upgrade_Boots.asset");
        var bootPrefab = ArtAssets.SavePrefab(bootHolder, "Assets/_Project/Prefabs/Player/_IconBoots.prefab");
        if (boots != null) ArtAssets.Set(boots, ("icon", ArtIcons.Render("Icon_Boots", bootPrefab, new Vector3(22f, 210f, 0f), null, 0.8f)));
        AssetDatabase.DeleteAsset("Assets/_Project/Prefabs/Player/_IconBoots.prefab");

        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static void EditPrefab(string path, Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Log.AppendLine("edited " + path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform ResetBody(GameObject root, params string[] remove)
    {
        var body = root.transform.Find("Body");
        foreach (var n in remove.Prepend("Model"))
        {
            var t = body.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        // Hats parented outside the model in older builders.
        var hat = root.transform.Find("HardHat");
        if (hat != null) Object.DestroyImmediate(hat.gameObject);
        return body;
    }

    static void BuildPlayer(AnimatorController controller)
    {
        EditPrefab($"{PrefabDir}/Player/Player.prefab", root =>
        {
            var body = ResetBody(root);
            var parts = ToyRig.Build(PlayerSpec(), body, controller);
            parts.Model.GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var clipSet = ToyRig.BuildClips(BladePaths);
            clipSet.Hold.SampleAnimation(parts.Model, 0f);
            var cutter = BuildCutter(parts.HandR, root.transform);

            var smokePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/VFX/VFX_ChainsawSmoke.prefab");
            var smoke = (GameObject)PrefabUtility.InstantiatePrefab(smokePrefab, cutter);
            smoke.transform.localPosition = new Vector3(0f, 0.12f, 0.0f);
            smoke.transform.localScale = Vector3.one * 0.8f;

            var tiers = cutter.gameObject.AddComponent<UpgradeVisualTiers>();
            var so = new SerializedObject(tiers);
            so.FindProperty("upgradeId").stringValue = "chainsaw";
            so.FindProperty("punchTarget").objectReferenceValue = cutter;
            so.FindProperty("renderers").arraySize = 0;
            var tp = so.FindProperty("tiers");
            tp.arraySize = 3;
            int[] levels = { 1, 4, 7 };
            for (int i = 0; i < 3; i++)
            {
                var e = tp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("minLevel").intValue = levels[i];
                e.FindPropertyRelative("material").objectReferenceValue = null;
                var objs = e.FindPropertyRelative("objects");
                objs.arraySize = 1;
                objs.GetArrayElementAtIndex(0).objectReferenceValue = cutter.Find("Tier" + (i + 1)).gameObject;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            cutter.Find("Tier2").gameObject.SetActive(false);
            cutter.Find("Tier3").gameObject.SetActive(false);

            ArtAssets.Set(root.GetComponent<PlayerVisuals>(), ("animator", parts.Model.GetComponent<Animator>()), ("body", body), ("tool", cutter),
                ("toolSmoke", smoke.GetComponent<ParticleSystem>()));
            ArtAssets.Set(root.GetComponent<HarvestTool>(), ("toolTip", cutter.Find("ToolTip")));
            // The utility pack sits on the back; start the carry stack just behind it.
            var anchor = root.transform.Find("StackAnchor");
            if (anchor != null) anchor.localPosition = new Vector3(0f, 0.98f, -0.56f);
        });
    }

    static void BuildWorker(string prefab, ToySpec spec, AnimatorController controller)
    {
        string path = $"{PrefabDir}/Workers/{prefab}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Log.AppendLine("!! missing " + path);
            return;
        }

        EditPrefab(path, root =>
        {
            var body = ResetBody(root, "CarryRack");
            var parts = ToyRig.Build(spec, body, controller);
            ArtAssets.Set(root.GetComponent<Worker>(), ("animator", parts.Model.GetComponent<Animator>()));
            var anchor = root.transform.Find("StackAnchor");
            if (anchor != null) anchor.localPosition = new Vector3(0f, 0.86f, -0.5f);
        });
    }

    static void BuildCustomer(string prefab, ToySpec spec, AnimatorController controller)
    {
        string path = $"{PrefabDir}/Customers/{prefab}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Log.AppendLine("!! missing " + path);
            return;
        }

        EditPrefab(path, root =>
        {
            var body = ResetBody(root);
            var parts = ToyRig.Build(spec, body, controller);
            ArtAssets.Set(root.GetComponent<Customer>(), ("animator", parts.Model.GetComponent<Animator>()));
        });
    }
}
