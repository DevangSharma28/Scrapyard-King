using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

// Art pass 4 — the world. Layered stylized ground (dirt, oily gravel, panelled concrete, asphalt, grass) with decals
// (oil, cracks, tyre marks, puddles, painted lines), custom corrugated walls, procedural scrap mounds, a prop library
// (containers, crushed-car stacks, pallets of bales, barrels, tyres, light poles, office cabin, workbench, tanks, pipe
// racks, forklift, tower crane, gantry crane, warehouses, trees, trucks, birds, flags), purposeful clusters in every
// area, ambient life (road traffic, forklift rounds, crane, birds, flags), bright warm lighting and post. No Kenney.
// Entry points: Props (prefabs), Scene (everything in the scene + NavMesh), Lighting; All runs them in order.
public static class Art_World
{
    const string P = "Assets/_Project";
    const string PropDir = P + "/Prefabs/Props";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";
    const string MatDir = P + "/Art/Materials";
    static readonly StringBuilder Log = new();

    static int C(PC c) => (int)c;

    // =====================================================================================
    // Prop library (palette meshes, one material each, static batched)
    // =====================================================================================

    static GameObject SaveProp(string name, MeshKit k, bool metal = false, Vector3? colliderSize = null, Vector3? colliderCenter = null, bool isStatic = true)
    {
        var root = new GameObject(name);
        var mesh = ArtAssets.SaveMesh("Props", name, k.ToPaletteMesh(name));
        var go = ArtAssets.MeshObject("Mesh", root.transform, mesh, new[] { metal ? ArtPalette.MetalMaterial : ArtPalette.Material });
        if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        if (colliderSize.HasValue)
        {
            var box = root.AddComponent<BoxCollider>();
            box.size = colliderSize.Value;
            box.center = colliderCenter ?? new Vector3(0f, colliderSize.Value.y * 0.5f, 0f);
        }

        return ArtAssets.SavePrefab(root, $"{PropDir}/{name}.prefab");
    }

    static GameObject PalettePart(string model, string part, MeshKit k, Transform parent, Vector3 pos = default, Vector3 euler = default, bool metal = false)
    {
        var mesh = ArtAssets.SaveMesh("Props", model + "_" + part, k.ToPaletteMesh(part));
        var go = ArtAssets.MeshObject(part, parent, mesh, new[] { metal ? ArtPalette.MetalMaterial : ArtPalette.Material }, pos, euler);
        return go;
    }

    static void Container(MeshKit k, Vector3 pos, float yaw, PC color)
    {
        k.Push(pos, new Vector3(0f, yaw, 0f));
        k.Box(C(color), new Vector3(0f, 1.3f, 0f), new Vector3(2.4f, 2.5f, 6.0f), 0.04f);
        for (float z = -2.7f; z <= 2.71f; z += 0.3f)
            for (int side = -1; side <= 1; side += 2)
                k.Box(C(color), new Vector3(side * 1.22f, 1.3f, z), new Vector3(0.06f, 2.2f, 0.14f), 0.01f);
        foreach (float y in new[] { 0.08f, 2.53f })
            for (int side = -1; side <= 1; side += 2)
                k.Box(C(PC.Charcoal), new Vector3(side * 1.2f, y, 0f), new Vector3(0.12f, 0.14f, 6.04f), 0.02f);
        foreach (float x in new[] { -1.16f, 1.16f })
        foreach (float z in new[] { -2.96f, 2.96f })
        foreach (float y in new[] { 0.1f, 2.5f })
            k.Box(C(PC.Charcoal), new Vector3(x, y, z), new Vector3(0.18f, 0.2f, 0.18f), 0.02f);
        // Doors on the +Z end with locking bars.
        k.Box(C(color), new Vector3(0f, 1.3f, 3.01f), new Vector3(2.3f, 2.3f, 0.05f), 0.01f);
        for (int i = 0; i < 4; i++) k.Cylinder(C(PC.Silver), new Vector3(-0.85f + i * 0.57f, 1.3f, 3.06f), 0.025f, 2.2f, 6, default, 0.005f);
        k.Box(C(PC.White), new Vector3(0.6f, 2.0f, 3.05f), new Vector3(0.6f, 0.25f, 0.02f), 0.01f);
        k.Box(C(PC.RustDark), new Vector3(-1.23f, 0.5f, -1.5f), new Vector3(0.03f, 0.5f, 0.8f), 0.01f);
        k.Pop();
    }

    static void CrushedCar(MeshKit k, Vector3 pos, Vector3 euler, PC color)
    {
        k.Push(pos, euler);
        k.Box(C(color), Vector3.zero, new Vector3(1.6f, 0.85f, 1.1f), 0.1f);
        k.Box(C(PC.Charcoal), new Vector3(0f, -0.2f, 0f), new Vector3(1.64f, 0.18f, 1.14f), 0.05f);
        k.Box(C(PC.Glass), new Vector3(0.2f, 0.25f, 0.56f), new Vector3(0.7f, 0.18f, 0.02f), 0.01f, new Vector3(0f, 0f, 8f));
        k.Box(C(PC.Silver), new Vector3(-0.3f, -0.05f, -0.56f), new Vector3(0.9f, 0.08f, 0.03f), 0.01f);
        k.Box(C(PC.Rust), new Vector3(0.5f, 0.43f, -0.1f), new Vector3(0.4f, 0.02f, 0.4f), 0.01f);
        k.Torus(C(PC.Rubber), new Vector3(-0.81f, -0.05f, 0.2f), 0.2f, 0.07f, 10, 5, new Vector3(0f, 0f, 90f));
        k.Pop();
    }

    static void Barrel(MeshKit k, Vector3 pos, Vector3 euler, PC color)
    {
        k.Push(pos, euler);
        k.Lathe(C(color), Vector3.zero, new[] { new Vector2(0.34f, 0f), new Vector2(0.38f, 0.03f), new Vector2(0.38f, 0.33f), new Vector2(0.4f, 0.35f), new Vector2(0.38f, 0.37f),
            new Vector2(0.38f, 0.7f), new Vector2(0.4f, 0.72f), new Vector2(0.38f, 0.74f), new Vector2(0.38f, 1.04f), new Vector2(0.35f, 1.07f) }, 10);
        k.Box(C(PC.White), new Vector3(0f, 0.55f, 0.38f), new Vector3(0.3f, 0.2f, 0.02f), 0.01f);
        k.Pop();
    }

    static void Tire(MeshKit k, Vector3 pos, Vector3 euler, float r = 0.55f)
    {
        k.Torus(C(PC.Rubber), pos, r * 0.68f, r * 0.33f, 12, 6, euler, new Vector2(1f, 1.1f));
    }

    static void Pallet(MeshKit k, Vector3 pos, float yaw)
    {
        k.Push(pos, new Vector3(0f, yaw, 0f));
        for (int i = 0; i < 5; i++) k.Box(C(PC.Wood), new Vector3(-0.48f + i * 0.24f, 0.13f, 0f), new Vector3(0.18f, 0.03f, 1.2f), 0.008f);
        foreach (float z in new[] { -0.5f, 0f, 0.5f }) k.Box(C(PC.WoodDark), new Vector3(0f, 0.06f, z), new Vector3(1.2f, 0.1f, 0.12f), 0.01f);
        k.Pop();
    }

    static void Bale(MeshKit k, Vector3 pos, float yaw, PC color)
    {
        k.Push(pos, new Vector3(0f, yaw, 0f));
        k.Box(C(color), Vector3.zero, new Vector3(0.5f, 0.38f, 0.5f), 0.05f);
        k.Box(C(PC.Charcoal), Vector3.zero, new Vector3(0.52f, 0.05f, 0.52f), 0.01f);
        k.Box(C(PC.Charcoal), Vector3.zero, new Vector3(0.05f, 0.4f, 0.52f), 0.01f);
        k.Pop();
    }

    static void Tree(MeshKit k, Vector3 pos, int style, Random rng)
    {
        float s = 0.85f + (float)rng.NextDouble() * 0.4f;
        k.Push(pos, new Vector3(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * s);
        k.Cylinder(C(PC.Bark), new Vector3(0f, 0.9f, 0f), 0.2f, 1.8f, 8, default, 0.05f, 0.14f);
        if (style == 0)
        {
            k.Sphere(C(PC.Leaf), new Vector3(0f, 2.6f, 0f), new Vector3(1.4f, 1.25f, 1.4f), 12, 8);
            k.Sphere(C(PC.LeafDark), new Vector3(0.7f, 2.2f, 0.4f), new Vector3(0.9f, 0.8f, 0.9f), 10, 6);
            k.Sphere(C(PC.Green), new Vector3(-0.5f, 3.2f, -0.3f), new Vector3(0.8f, 0.75f, 0.8f), 10, 6);
        }
        else if (style == 1)
        {
            for (int i = 0; i < 3; i++)
                k.Cylinder(i % 2 == 0 ? C(PC.LeafDark) : C(PC.GreenDark), new Vector3(0f, 1.8f + i * 0.85f, 0f), 1.3f - i * 0.32f, 1.3f, 9, default, 0.05f, 0.08f);
        }
        else
        {
            k.Sphere(C(PC.Leaf), new Vector3(0f, 3.0f, 0f), new Vector3(0.85f, 1.9f, 0.85f), 10, 8);
            k.Sphere(C(PC.LeafDark), new Vector3(0.25f, 2.4f, 0.2f), new Vector3(0.6f, 1.1f, 0.6f), 8, 6);
        }

        k.Pop();
    }

    static void TruckBody(MeshKit k, PC cab, PC trailer)
    {
        k.Box(C(PC.Charcoal), new Vector3(0f, 0.75f, 0f), new Vector3(1.8f, 0.3f, 9.0f), 0.05f);
        k.Box(C(cab), new Vector3(0f, 1.75f, 3.7f), new Vector3(2.4f, 2.2f, 1.7f), 0.2f);
        k.Box(C(PC.Glass), new Vector3(0f, 2.3f, 4.56f), new Vector3(2.0f, 0.8f, 0.05f), 0.04f);
        k.Box(C(PC.Silver), new Vector3(0f, 1.05f, 4.57f), new Vector3(1.4f, 0.4f, 0.05f), 0.02f);
        foreach (int side in new[] { -1, 1 }) k.Box(C(PC.Cream), new Vector3(side * 0.85f, 1.05f, 4.58f), new Vector3(0.3f, 0.18f, 0.04f), 0.02f);
        k.Box(C(trailer), new Vector3(0f, 2.15f, -1.0f), new Vector3(2.5f, 2.6f, 6.6f), 0.08f);
        k.Box(C(PC.White), new Vector3(1.26f, 2.2f, -1.0f), new Vector3(0.02f, 1.2f, 4.0f), 0.01f);
        k.Box(C(cab), new Vector3(1.27f, 2.2f, -1.0f), new Vector3(0.02f, 0.5f, 3.4f), 0.01f);
    }

    public static string Props()
    {
        Log.Clear();
        Directory.CreateDirectory(PropDir);
        var rng = new Random(7);

        foreach (var (name, color) in new[] { ("Blue", PC.Blue), ("Red", PC.Red), ("Green", PC.GreenDark), ("Orange", PC.Orange), ("Yellow", PC.Yellow), ("Teal", PC.Teal) })
        {
            var k = new MeshKit();
            Container(k, Vector3.zero, 0f, color);
            SaveProp("Prop_Container_" + name, k, false, new Vector3(2.4f, 2.6f, 6.0f));
        }

        var stack = new MeshKit();
        PC[] carColors = { PC.Red, PC.Blue, PC.Yellow, PC.Teal, PC.Green, PC.Orange, PC.Purple, PC.Cream };
        for (int layer = 0; layer < 3; layer++)
            for (int i = 0; i < 3 - layer; i++)
                CrushedCar(stack, new Vector3(-1.7f + i * 1.7f + layer * 0.85f, 0.42f + layer * 0.84f, 0f), new Vector3(0f, rng.Next(-6, 6), rng.Next(-3, 3)),
                    carColors[rng.Next(carColors.Length)]);
        SaveProp("Prop_CrushedCars", stack, false, new Vector3(5.2f, 2.5f, 1.3f));

        var palletBales = new MeshKit();
        Pallet(palletBales, Vector3.zero, 0f);
        PC[] baleColors = { PC.Gray, PC.Silver, PC.Rust, PC.SteelBlue };
        for (int x = 0; x < 2; x++)
        for (int z = 0; z < 2; z++)
        for (int y = 0; y < 2; y++)
            Bale(palletBales, new Vector3(-0.28f + x * 0.56f, 0.35f + y * 0.4f, -0.28f + z * 0.56f), rng.Next(-5, 5), baleColors[rng.Next(baleColors.Length)]);
        SaveProp("Prop_PalletBales", palletBales, false, new Vector3(1.25f, 1.1f, 1.25f));

        var pallets = new MeshKit();
        for (int i = 0; i < 4; i++) Pallet(pallets, new Vector3(0f, i * 0.16f, 0f), rng.Next(-8, 8));
        SaveProp("Prop_PalletStack", pallets, false, new Vector3(1.25f, 0.7f, 1.25f));

        var barrels = new MeshKit();
        Barrel(barrels, new Vector3(0f, 0f, 0f), default, PC.Blue);
        Barrel(barrels, new Vector3(0.82f, 0f, 0.1f), default, PC.Red);
        Barrel(barrels, new Vector3(0.4f, 0f, 0.75f), default, PC.Yellow);
        Barrel(barrels, new Vector3(-0.5f, 0.38f, 0.9f), new Vector3(0f, 30f, 90f), PC.Green);
        SaveProp("Prop_Barrels", barrels, false, new Vector3(2.2f, 1.1f, 1.9f), new Vector3(0.3f, 0.55f, 0.4f));

        var tires = new MeshKit();
        for (int i = 0; i < 4; i++) Tire(tires, new Vector3(0f, 0.18f + i * 0.36f, 0f), new Vector3(0f, i * 23f, 0f));
        Tire(tires, new Vector3(1.0f, 0.18f, 0.3f), default);
        Tire(tires, new Vector3(0.9f, 0.5f, -0.6f), new Vector3(70f, 20f, 0f));
        SaveProp("Prop_Tires", tires, false, new Vector3(2.0f, 1.4f, 1.6f), new Vector3(0.4f, 0.7f, 0f));

        var pole = new MeshKit();
        pole.Cylinder(C(PC.Charcoal), new Vector3(0f, 0.2f, 0f), 0.22f, 0.4f, 10, default, 0.03f);
        pole.Cylinder(C(PC.Gray), new Vector3(0f, 3.2f, 0f), 0.09f, 6.0f, 10, default, 0.02f, 0.07f);
        pole.Box(C(PC.Gray), new Vector3(0f, 6.15f, 0.45f), new Vector3(0.12f, 0.1f, 1.0f), 0.02f);
        pole.Box(C(PC.Charcoal), new Vector3(0f, 6.1f, 0.95f), new Vector3(0.5f, 0.16f, 0.36f), 0.05f);
        pole.Box(C(PC.Cream), new Vector3(0f, 6.01f, 0.95f), new Vector3(0.42f, 0.02f, 0.28f), 0.01f);
        pole.Box(C(PC.Yellow), new Vector3(0f, 1.2f, 0f), new Vector3(0.2f, 0.4f, 0.2f), 0.02f);
        SaveProp("Prop_LightPole", pole, true, new Vector3(0.45f, 3f, 0.45f));

        var office = new MeshKit();
        office.Box(C(PC.White), new Vector3(0f, 1.45f, 0f), new Vector3(5.2f, 2.5f, 2.5f), 0.06f);
        office.Box(C(PC.Blue), new Vector3(0f, 0.25f, 0f), new Vector3(5.3f, 0.3f, 2.6f), 0.03f);
        office.Box(C(PC.Blue), new Vector3(0f, 2.78f, 0f), new Vector3(5.35f, 0.18f, 2.65f), 0.03f);
        for (int i = 0; i < 3; i++) office.Box(C(PC.Glass), new Vector3(-1.8f + i * 1.3f, 1.65f, -1.26f), new Vector3(0.9f, 0.7f, 0.04f), 0.02f);
        office.Box(C(PC.SteelBlue), new Vector3(2.0f, 1.3f, -1.27f), new Vector3(0.85f, 1.9f, 0.05f), 0.02f);
        office.Box(C(PC.Gray), new Vector3(2.0f, 0.35f, -1.75f), new Vector3(1.2f, 0.12f, 0.9f), 0.02f);
        office.Box(C(PC.Gray), new Vector3(2.0f, 0.15f, -2.3f), new Vector3(1.2f, 0.12f, 0.3f), 0.02f);
        office.Box(C(PC.GrayLight), new Vector3(-2.2f, 3.05f, 0.4f), new Vector3(0.8f, 0.4f, 0.8f), 0.05f);
        office.Cylinder(C(PC.Charcoal), new Vector3(-2.2f, 3.26f, 0.4f), 0.28f, 0.04f, 10, default, 0f);
        for (int i = 0; i < 3; i++) office.Box(C(PC.Gray), new Vector3(0.2f + i * 0.9f, 2.92f, 0.5f), new Vector3(0.5f, 0.12f, 0.5f), 0f);
        office.Box(C(PC.Navy), new Vector3(0f, 2.9f, -0.2f), new Vector3(4.6f, 0.02f, 1.6f), 0f);
        office.Cylinder(C(PC.Silver), new Vector3(1.9f, 3.3f, 0.9f), 0.03f, 1.0f, 6, default, 0f);
        office.Box(C(PC.Yellow), new Vector3(-0.4f, 2.4f, -1.29f), new Vector3(2.4f, 0.45f, 0.04f), 0.02f);
        office.Box(C(PC.Charcoal), new Vector3(-0.4f, 2.4f, -1.32f), new Vector3(2.1f, 0.12f, 0.01f), 0.004f);
        SaveProp("Prop_Office", office, false, new Vector3(5.4f, 3f, 2.7f));

        var bench = new MeshKit();
        bench.Box(C(PC.Wood), new Vector3(0f, 0.92f, 0f), new Vector3(2.0f, 0.1f, 0.8f), 0.02f);
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            bench.Box(C(PC.Charcoal), new Vector3(x * 0.9f, 0.45f, z * 0.32f), new Vector3(0.08f, 0.9f, 0.08f), 0.01f);
        bench.Box(C(PC.Charcoal), new Vector3(0f, 0.3f, 0f), new Vector3(1.9f, 0.05f, 0.7f), 0.01f);
        bench.Box(C(PC.Red), new Vector3(-0.55f, 1.12f, 0.05f), new Vector3(0.6f, 0.3f, 0.35f), 0.04f);
        bench.Box(C(PC.RedDeep), new Vector3(-0.55f, 1.28f, 0.05f), new Vector3(0.62f, 0.04f, 0.37f), 0.01f);
        bench.Box(C(PC.Gray), new Vector3(0.6f, 1.05f, 0.1f), new Vector3(0.25f, 0.16f, 0.2f), 0.03f);
        bench.Cylinder(C(PC.Silver), new Vector3(0.2f, 1.0f, -0.15f), 0.025f, 0.4f, 6, new Vector3(0f, 30f, 90f), 0.005f);
        bench.Box(C(PC.Orange), new Vector3(0.1f, 1.0f, 0.2f), new Vector3(0.3f, 0.06f, 0.08f), 0.02f, new Vector3(0f, -20f, 0f));
        bench.Box(C(PC.Charcoal), new Vector3(0f, 1.65f, 0.38f), new Vector3(2.0f, 1.3f, 0.05f), 0.01f);
        for (int i = 0; i < 5; i++) bench.Box(C(PC.Silver), new Vector3(-0.8f + i * 0.4f, 1.7f, 0.33f), new Vector3(0.04f, 0.4f, 0.03f), 0.005f);
        SaveProp("Prop_Workbench", bench, false, new Vector3(2.1f, 1.2f, 0.9f));

        var dumpster = new MeshKit();
        dumpster.Prism(C(PC.GreenDark), new Vector3(0f, 0f, 0f), new[] { new Vector2(-0.8f, 0.1f), new Vector2(-1.0f, 1.2f), new Vector2(1.0f, 1.2f), new Vector2(0.8f, 0.1f) },
            1.8f, 0.05f, new Vector3(0f, 90f, 0f));
        dumpster.Box(C(PC.Charcoal), new Vector3(0f, 1.25f, 0f), new Vector3(2.05f, 0.08f, 1.85f), 0.02f, new Vector3(0f, 0f, 4f));
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            dumpster.Cylinder(C(PC.Charcoal), new Vector3(x * 0.7f, 0.06f, z * 0.6f), 0.08f, 0.08f, 8, new Vector3(0f, 0f, 90f), 0.01f);
        SaveProp("Prop_Dumpster", dumpster, false, new Vector3(2.1f, 1.3f, 1.9f));

        var tank = new MeshKit();
        tank.Lathe(C(PC.Silver), Vector3.zero, new[] { new Vector2(1.4f, 0.3f), new Vector2(1.4f, 5.6f), new Vector2(1.1f, 6.2f), new Vector2(0.3f, 6.4f) }, 18);
        tank.Cylinder(C(PC.Charcoal), new Vector3(0f, 0.15f, 0f), 1.5f, 0.3f, 18, default, 0.03f);
        foreach (float y in new[] { 1.5f, 3.0f, 4.5f }) tank.Torus(C(PC.Gray), new Vector3(0f, y, 0f), 1.42f, 0.05f, 18, 5);
        tank.Box(C(PC.Teal), new Vector3(0f, 3.4f, 1.41f), new Vector3(1.6f, 0.9f, 0.04f), 0.02f);
        for (int side = -1; side <= 1; side += 2) tank.Cylinder(C(PC.Yellow), new Vector3(1.3f + side * 0.0f, 3.1f, side * 0.25f + 0.6f), 0.03f, 6.0f, 6, default, 0.005f);
        SaveProp("Prop_Tank", tank, true, new Vector3(3f, 6.4f, 3f));

        var pipes = new MeshKit();
        foreach (float x in new[] { -3.5f, 0f, 3.5f })
        {
            pipes.Box(C(PC.Charcoal), new Vector3(x, 0.6f, 0f), new Vector3(0.15f, 1.2f, 1.2f), 0.02f);
            pipes.Box(C(PC.Yellow), new Vector3(x, 1.2f, 0f), new Vector3(0.2f, 0.1f, 1.3f), 0.02f);
        }

        PC[] pipeColors = { PC.Gray, PC.Orange, PC.SteelBlue, PC.Silver, PC.Rust };
        for (int i = 0; i < 7; i++)
            pipes.Cylinder(C(pipeColors[i % pipeColors.Length]), new Vector3(0f, 1.42f + (i / 4) * 0.36f, -0.45f + (i % 4) * 0.3f + (i / 4) * 0.15f), 0.15f, 8f, 12,
                new Vector3(0f, 0f, 90f), 0.02f);
        SaveProp("Prop_PipeRack", pipes, true, new Vector3(8.2f, 2.2f, 1.4f));

        var beams = new MeshKit();
        for (int i = 0; i < 6; i++)
        {
            float y = 0.12f + (i / 3) * 0.24f, x = -0.6f + (i % 3) * 0.6f + (i / 3) * 0.3f;
            beams.Box(C(PC.Orange), new Vector3(x, y, 0f), new Vector3(0.36f, 0.04f, 5f), 0.01f);
            beams.Box(C(PC.Orange), new Vector3(x, y - 0.1f, 0f), new Vector3(0.36f, 0.04f, 5f), 0.01f);
            beams.Box(C(PC.Orange), new Vector3(x, y - 0.05f, 0f), new Vector3(0.05f, 0.18f, 5f), 0.01f);
        }

        foreach (float z in new[] { -1.8f, 1.8f }) beams.Box(C(PC.WoodDark), new Vector3(0f, 0.0f, z), new Vector3(2.2f, 0.12f, 0.2f), 0.01f);
        SaveProp("Prop_Beams", beams, true, new Vector3(2.2f, 0.6f, 5f));

        var cone = new MeshKit();
        cone.Box(C(PC.Orange), new Vector3(0f, 0.03f, 0f), new Vector3(0.5f, 0.06f, 0.5f), 0.02f);
        cone.Cylinder(C(PC.Orange), new Vector3(0f, 0.4f, 0f), 0.2f, 0.7f, 12, default, 0.01f, 0.05f);
        cone.Cylinder(C(PC.White), new Vector3(0f, 0.45f, 0f), 0.135f, 0.12f, 12, default, 0.005f, 0.115f);
        SaveProp("Prop_Cone", cone);

        var crate = new MeshKit();
        crate.Box(C(PC.Wood), new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.9f), 0.05f);
        foreach (int s in new[] { -1, 1 })
        {
            crate.Box(C(PC.WoodDark), new Vector3(0f, 0.45f, s * 0.455f), new Vector3(0.92f, 0.1f, 0.02f), 0.01f);
            crate.Box(C(PC.WoodDark), new Vector3(s * 0.455f, 0.45f, 0f), new Vector3(0.02f, 0.1f, 0.92f), 0.01f);
        }

        SaveProp("Prop_Crate", crate, false, new Vector3(0.9f, 0.9f, 0.9f));

        // Forklift (parked or driven by an AmbientPath).
        {
            var root = new GameObject("Prop_Forklift");
            var body = new MeshKit();
            body.Box(C(PC.Yellow), new Vector3(0f, 0.75f, -0.2f), new Vector3(1.2f, 0.8f, 1.8f), 0.12f);
            body.Box(C(PC.Charcoal), new Vector3(0f, 0.75f, -1.15f), new Vector3(1.15f, 0.7f, 0.4f), 0.1f);
            body.Box(C(PC.Charcoal), new Vector3(0f, 1.25f, -0.35f), new Vector3(0.6f, 0.15f, 0.6f), 0.04f);
            body.Box(C(PC.Charcoal), new Vector3(0f, 1.5f, -0.65f), new Vector3(0.6f, 0.5f, 0.12f), 0.04f);
            foreach (int x in new[] { -1, 1 })
            foreach (int z in new[] { -1, 1 })
                body.Box(C(PC.Charcoal), new Vector3(x * 0.55f, 1.65f, -0.35f + z * 0.55f), new Vector3(0.07f, 1.4f, 0.07f), 0.01f);
            body.Box(C(PC.Charcoal), new Vector3(0f, 2.35f, -0.35f), new Vector3(1.2f, 0.08f, 1.2f), 0.02f);
            body.Box(C(PC.Orange), new Vector3(0f, 2.43f, -0.35f), new Vector3(0.25f, 0.12f, 0.25f), 0.04f);
            foreach (int x in new[] { -1, 1 }) body.Box(C(PC.Gray), new Vector3(x * 0.38f, 1.4f, 0.78f), new Vector3(0.1f, 2.6f, 0.12f), 0.02f);
            body.Box(C(PC.Gray), new Vector3(0f, 0.55f, 0.85f), new Vector3(0.9f, 0.5f, 0.06f), 0.02f);
            foreach (int x in new[] { -1, 1 }) body.Box(C(PC.Gray), new Vector3(x * 0.28f, 0.15f, 1.35f), new Vector3(0.12f, 0.06f, 1.0f), 0.01f);
            Pallet(body, new Vector3(0f, 0.12f, 1.4f), 90f);
            body.Box(C(PC.Wood), new Vector3(0f, 0.6f, 1.4f), new Vector3(0.9f, 0.6f, 0.9f), 0.05f);
            PalettePart("Forklift", "Body", body, root.transform);
            var wheels = new List<Transform>();
            foreach (int x in new[] { -1, 1 })
            foreach (float z in new[] { -0.8f, 0.45f })
            {
                var w = new MeshKit();
                w.Cylinder(C(PC.Rubber), Vector3.zero, 0.3f, 0.26f, 12, new Vector3(0f, 0f, 90f), 0.05f);
                w.Cylinder(C(PC.Yellow), Vector3.zero, 0.14f, 0.28f, 8, new Vector3(0f, 0f, 90f), 0.02f);
                wheels.Add(PalettePart("Forklift", "Wheel", w, root.transform, new Vector3(x * 0.6f, 0.3f, z)).transform);
            }

            ArtAssets.SavePrefab(root, $"{PropDir}/Prop_Forklift.prefab");
        }

        // Tower crane with a slewing top (AmbientMotion swing) and a hook on a trolley.
        {
            var root = new GameObject("Prop_TowerCrane");
            var mast = new MeshKit();
            mast.Box(C(PC.Concrete), new Vector3(0f, 0.3f, 0f), new Vector3(3f, 0.6f, 3f), 0.05f);
            for (float y = 0.6f; y < 15f; y += 1.5f)
            {
                foreach (int x in new[] { -1, 1 })
                foreach (int z in new[] { -1, 1 })
                    mast.Box(C(PC.Yellow), new Vector3(x * 0.7f, y + 0.75f, z * 0.7f), new Vector3(0.14f, 1.5f, 0.14f), 0.02f);
                for (int s = 0; s < 4; s++)
                    mast.Box(C(PC.YellowDeep), Quaternion.Euler(0f, s * 90f, 0f) * new Vector3(0f, y + 0.75f, 0.7f), new Vector3(1.5f, 0.07f, 0.07f), 0.01f,
                        new Vector3(0f, s * 90f, 40f));
            }

            PalettePart("TowerCrane", "Mast", mast, root.transform);
            var top = new GameObject("Slew").transform;
            top.SetParent(root.transform, false);
            top.localPosition = new Vector3(0f, 15f, 0f);
            var jib = new MeshKit();
            jib.Box(C(PC.Yellow), new Vector3(0f, 0.6f, 6f), new Vector3(0.9f, 0.9f, 14f), 0.04f);
            jib.Box(C(PC.Yellow), new Vector3(0f, 0.6f, -3f), new Vector3(0.9f, 0.7f, 5f), 0.04f);
            jib.Box(C(PC.Concrete), new Vector3(0f, 0.2f, -5f), new Vector3(1.6f, 1.2f, 1.6f), 0.05f);
            jib.Box(C(PC.Yellow), new Vector3(0f, 2.5f, 0f), new Vector3(0.6f, 3.2f, 0.6f), 0.04f);
            jib.Box(C(PC.Red), new Vector3(0.9f, 0.0f, 0.6f), new Vector3(1.2f, 1.4f, 1.4f), 0.1f);
            jib.Box(C(PC.Glass), new Vector3(0.9f, 0.1f, 1.31f), new Vector3(1.0f, 0.8f, 0.04f), 0.02f);
            jib.Cylinder(C(PC.Charcoal), new Vector3(0f, 2.3f, 6f), 0.03f, 8f, 6, new Vector3(-75f, 0f, 0f), 0.005f);
            jib.Box(C(PC.Charcoal), new Vector3(0f, 0.05f, 9f), new Vector3(0.7f, 0.25f, 0.6f), 0.03f);
            jib.Cylinder(C(PC.Charcoal), new Vector3(0f, -3.5f, 9f), 0.02f, 7f, 4, default, 0.002f);
            jib.Box(C(PC.Orange), new Vector3(0f, -7.1f, 9f), new Vector3(0.6f, 0.4f, 0.4f), 0.05f);
            jib.Box(C(PC.Rust), new Vector3(0f, -7.6f, 9f), new Vector3(1.2f, 0.5f, 0.9f), 0.05f);
            PalettePart("TowerCrane", "Jib", jib, top);
            var swing = top.gameObject.AddComponent<AmbientMotion>();
            ArtAssets.Set(swing, ("mode", (int)AmbientMotion.Mode.Swing), ("axis", Vector3.up), ("amount", 35f), ("frequency", 0.025f));
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 7f, 0f);
            box.size = new Vector3(1.8f, 14f, 1.8f);
            ArtAssets.SavePrefab(root, $"{PropDir}/Prop_TowerCrane.prefab");
        }

        var gantry = new MeshKit();
        foreach (int x in new[] { -1, 1 })
        {
            gantry.Box(C(PC.Yellow), new Vector3(x * 6f, 5f, 0f), new Vector3(0.8f, 10f, 0.8f), 0.05f);
            gantry.Box(C(PC.Charcoal), new Vector3(x * 6f, 0.4f, 0f), new Vector3(1.2f, 0.8f, 3f), 0.06f);
        }

        gantry.Box(C(PC.Yellow), new Vector3(0f, 10.4f, 0f), new Vector3(13f, 1.2f, 1.4f), 0.06f);
        gantry.Box(C(PC.Orange), new Vector3(1.5f, 9.4f, 0f), new Vector3(1.6f, 0.8f, 1.6f), 0.06f);
        gantry.Cylinder(C(PC.Charcoal), new Vector3(1.5f, 7f, 0f), 0.03f, 4f, 4, default, 0.003f);
        Container(gantry, new Vector3(1.5f, 3.6f, 0f), 90f, PC.Red);
        SaveProp("Prop_GantryCrane", gantry, false, new Vector3(13f, 11f, 3f), new Vector3(0f, 5.5f, 0f));

        foreach (var (name, wall, roof) in new[] { ("Prop_Warehouse_Blue", PC.SteelBlue, PC.Red), ("Prop_Warehouse_Cream", PC.Cream, PC.Teal) })
        {
            var shed = new MeshKit();
            shed.Box(C(wall), new Vector3(0f, 3f, 0f), new Vector3(12f, 6f, 8f), 0.06f);
            for (float x = -5.7f; x <= 5.71f; x += 0.4f) shed.Box(C(wall), new Vector3(x, 3f, -4.02f), new Vector3(0.12f, 5.8f, 0.06f), 0.01f);
            shed.Prism(C(roof), new Vector3(0f, 6f, 0f), new[] { new Vector2(-4.3f, 0f), new Vector2(0f, 2f), new Vector2(4.3f, 0f) }, 12.6f, 0.08f, new Vector3(0f, 90f, 0f));
            shed.Box(C(PC.GrayLight), new Vector3(-2.5f, 2f, -4.05f), new Vector3(4f, 4f, 0.08f), 0.02f);
            for (int i = 0; i < 10; i++) shed.Box(C(PC.Gray), new Vector3(-2.5f, 0.3f + i * 0.38f, -4.1f), new Vector3(3.9f, 0.04f, 0.02f), 0.005f);
            for (int i = 0; i < 4; i++) shed.Box(C(PC.Glass), new Vector3(1.0f + i * 1.2f, 4.6f, -4.05f), new Vector3(0.9f, 0.7f, 0.06f), 0.02f);
            shed.Box(C(PC.Yellow), new Vector3(3.2f, 3.3f, -4.06f), new Vector3(3f, 0.6f, 0.04f), 0.02f);
            SaveProp(name, shed, false, new Vector3(12f, 6f, 8f));
        }

        for (int style = 0; style < 3; style++)
        {
            var t = new MeshKit();
            Tree(t, Vector3.zero, style, new Random(style * 13 + 1));
            SaveProp("Prop_Tree_" + style, t, false, new Vector3(0.6f, 3f, 0.6f));
        }

        var bush = new MeshKit();
        bush.Sphere(C(PC.Leaf), new Vector3(0f, 0.4f, 0f), new Vector3(0.7f, 0.5f, 0.7f), 10, 6);
        bush.Sphere(C(PC.LeafDark), new Vector3(0.5f, 0.32f, 0.2f), new Vector3(0.5f, 0.38f, 0.5f), 8, 5);
        SaveProp("Prop_Bush", bush);

        var upole = new MeshKit();
        upole.Cylinder(C(PC.WoodDark), new Vector3(0f, 4.5f, 0f), 0.14f, 9f, 8, default, 0.03f, 0.11f);
        upole.Box(C(PC.WoodDark), new Vector3(0f, 8.4f, 0f), new Vector3(2.2f, 0.14f, 0.14f), 0.02f);
        foreach (float x in new[] { -0.9f, 0f, 0.9f }) upole.Cylinder(C(PC.White), new Vector3(x, 8.6f, 0f), 0.05f, 0.25f, 6, default, 0.01f);
        upole.Cylinder(C(PC.Gray), new Vector3(0.5f, 7.6f, 0.2f), 0.25f, 0.6f, 10, default, 0.04f);
        SaveProp("Prop_UtilityPole", upole, false, new Vector3(0.4f, 4f, 0.4f));

        // Road truck for passing traffic (driven by AmbientPath; wheels spin).
        foreach (var (name, cab, trailer) in new[] { ("Prop_RoadTruck_Red", PC.Red, PC.White), ("Prop_RoadTruck_Blue", PC.Blue, PC.Yellow) })
        {
            var root = new GameObject(name);
            var body = new MeshKit();
            TruckBody(body, cab, trailer);
            PalettePart(name, "Body", body, root.transform);
            foreach (int x in new[] { -1, 1 })
            foreach (float z in new[] { 3.4f, -2.6f, -3.8f })
            {
                var w = new MeshKit();
                w.Cylinder(C(PC.Rubber), Vector3.zero, 0.5f, 0.4f, 14, new Vector3(0f, 0f, 90f), 0.06f);
                w.Cylinder(C(PC.Silver), Vector3.zero, 0.26f, 0.42f, 10, new Vector3(0f, 0f, 90f), 0.02f);
                PalettePart(name, "Wheel", w, root.transform, new Vector3(x * 1.05f, 0.5f, z));
            }

            ArtAssets.SavePrefab(root, $"{PropDir}/{name}.prefab");
        }

        // Bird: body + two flapping wings; the prefab root circles via AmbientMotion on a pivot placed in the scene.
        {
            var root = new GameObject("Prop_Bird");
            var b = new MeshKit();
            b.Sphere(C(PC.White), Vector3.zero, new Vector3(0.12f, 0.1f, 0.28f), 8, 6);
            b.Box(C(PC.Orange), new Vector3(0f, 0f, 0.3f), new Vector3(0.05f, 0.04f, 0.1f), 0.01f);
            b.Box(C(PC.Charcoal), new Vector3(0f, 0.02f, -0.3f), new Vector3(0.16f, 0.02f, 0.14f), 0.01f);
            PalettePart("Bird", "Body", b, root.transform);
            foreach (int side in new[] { -1, 1 })
            {
                var pivot = new GameObject(side < 0 ? "WingL" : "WingR").transform;
                pivot.SetParent(root.transform, false);
                var w = new MeshKit();
                w.Box(C(PC.White), new Vector3(side * 0.3f, 0f, 0f), new Vector3(0.55f, 0.02f, 0.2f), 0.01f);
                w.Box(C(PC.Gray), new Vector3(side * 0.55f, 0f, -0.03f), new Vector3(0.12f, 0.022f, 0.16f), 0.005f);
                PalettePart("Bird", side < 0 ? "WingL" : "WingR", w, pivot);
                var flap = pivot.gameObject.AddComponent<AmbientMotion>();
                ArtAssets.Set(flap, ("mode", (int)AmbientMotion.Mode.Swing), ("axis", Vector3.forward * side), ("amount", 35f), ("frequency", 2.2f), ("randomPhase", false));
            }

            ArtAssets.SavePrefab(root, $"{PropDir}/Prop_Bird.prefab");
        }

        {
            var root = new GameObject("Prop_Flag");
            var poleK = new MeshKit();
            poleK.Cylinder(C(PC.Silver), new Vector3(0f, 2.5f, 0f), 0.05f, 5f, 8, default, 0.01f);
            poleK.Sphere(C(PC.Yellow), new Vector3(0f, 5.05f, 0f), Vector3.one * 0.09f, 8, 5);
            PalettePart("Flag", "Pole", poleK, root.transform, default, default, true);
            var cloth = new GameObject("Cloth").transform;
            cloth.SetParent(root.transform, false);
            cloth.localPosition = new Vector3(0f, 4.4f, 0f);
            var c = new MeshKit();
            c.Box(C(PC.Orange), new Vector3(0.6f, 0f, 0f), new Vector3(1.2f, 0.8f, 0.03f), 0.01f);
            c.Box(C(PC.Yellow), new Vector3(0.6f, 0f, 0.02f), new Vector3(0.45f, 0.45f, 0.01f), 0.01f, new Vector3(0f, 0f, 45f));
            PalettePart("Flag", "Cloth", c, cloth);
            var wave = cloth.gameObject.AddComponent<AmbientMotion>();
            ArtAssets.Set(wave, ("mode", (int)AmbientMotion.Mode.Swing), ("axis", Vector3.up), ("amount", 18f), ("frequency", 0.7f));
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.5f, 0f);
            box.size = new Vector3(0.2f, 3f, 0.2f);
            ArtAssets.SavePrefab(root, $"{PropDir}/Prop_Flag.prefab");
        }

        var cyl = new MeshKit();
        for (int i = 0; i < 5; i++)
        {
            var pc = i % 2 == 0 ? PC.Green : PC.Teal;
            cyl.Cylinder(C(pc), new Vector3(-0.6f + i * 0.3f, 0.65f, (i % 2) * 0.25f), 0.13f, 1.3f, 10, default, 0.04f);
            cyl.Cylinder(C(PC.Silver), new Vector3(-0.6f + i * 0.3f, 1.35f, (i % 2) * 0.25f), 0.05f, 0.12f, 8, default, 0.01f);
        }

        cyl.Box(C(PC.Charcoal), new Vector3(0f, 0.9f, -0.2f), new Vector3(1.8f, 0.06f, 0.06f), 0.01f);
        SaveProp("Prop_GasCylinders", cyl, false, new Vector3(1.8f, 1.4f, 0.8f));

        var gen = new MeshKit();
        gen.Box(C(PC.Yellow), new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 1.0f, 1.0f), 0.1f);
        gen.Box(C(PC.Charcoal), new Vector3(0f, 0.08f, 0f), new Vector3(1.9f, 0.16f, 1.05f), 0.03f);
        gen.Box(C(PC.Charcoal), new Vector3(-0.4f, 0.65f, 0.51f), new Vector3(0.6f, 0.45f, 0.02f), 0.01f);
        for (int i = 0; i < 6; i++) gen.Box(C(PC.Charcoal), new Vector3(0.3f + i * 0.09f, 0.65f, 0.51f), new Vector3(0.04f, 0.6f, 0.02f), 0.005f);
        gen.Cylinder(C(PC.Silver), new Vector3(0.65f, 1.25f, -0.2f), 0.06f, 0.4f, 8, default, 0.01f);
        SaveProp("Prop_Generator", gen, false, new Vector3(1.9f, 1.2f, 1.1f));

        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    // =====================================================================================
    // Scrap mounds, walls
    // =====================================================================================

    /// <summary>A heap of junk along <paramref name="extents"/> (local X/Z half sizes): dark mound plus colourful items on top.</summary>
    static MeshKit ScrapMound(Vector2 extents, int seed, float height = 1.5f)
    {
        var rng = new Random(seed);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var k = new MeshKit();
        k.Dome(C(PC.Charcoal), Vector3.zero, new Vector3(extents.x * 1.02f, height * 0.72f, extents.y * 1.02f), 14, 4);
        k.Dome(C(PC.RustDark), new Vector3(R(-0.3f, 0.3f) * extents.x, 0f, 0f), new Vector3(extents.x * 0.7f, height * 0.92f, extents.y * 0.8f), 12, 4);
        float Surface(float x, float z)
        {
            float u = x / (extents.x * 1.05f), v = z / (extents.y * 1.05f);
            float d = 1f - (u * u + v * v);
            return d <= 0f ? 0f : Mathf.Sqrt(d) * height * 0.8f;
        }

        PC[] paints = { PC.Red, PC.Blue, PC.Yellow, PC.Teal, PC.Green, PC.Orange, PC.Cream, PC.White };
        PC[] metals = { PC.Gray, PC.Charcoal, PC.Rust, PC.RustDark, PC.Silver, PC.SteelBlue, PC.GrayLight, PC.Rust };
        // Dense metal junk first (plates, beams, drums in steel and rust tones), then colourful accents on top.
        int junk = Mathf.RoundToInt(extents.x * extents.y * 7f + (extents.x + extents.y) * 4f) + 10;
        for (int i = 0; i < junk; i++)
        {
            float x = R(-0.95f, 0.95f) * extents.x, z = R(-0.9f, 0.9f) * extents.y;
            var pos = new Vector3(x, Surface(x, z) * R(0.55f, 0.95f), z);
            var euler = new Vector3(R(-50f, 50f), R(0f, 360f), R(-50f, 50f));
            var m = metals[rng.Next(metals.Length)];
            switch (rng.Next(4))
            {
                case 0:
                    k.Box(C(m), pos, new Vector3(R(0.6f, 1.6f), R(0.05f, 0.12f), R(0.4f, 1.0f)), 0f, euler);
                    break;
                case 1:
                    k.Box(C(m), pos, new Vector3(R(0.3f, 0.7f), R(0.3f, 0.6f), R(0.3f, 0.7f)), 0.05f, euler);
                    break;
                case 2:
                    k.Cylinder(C(m), pos, R(0.05f, 0.12f), R(1.0f, 2.4f), 6, euler, 0f);
                    break;
                default:
                    k.Box(C(m), pos, new Vector3(0.12f, 0.24f, R(1.2f, 2.6f)), 0f, euler);
                    break;
            }
        }

        int items = Mathf.RoundToInt(extents.x * extents.y * 2.6f + (extents.x + extents.y) * 1.6f) + 5;
        for (int i = 0; i < items; i++)
        {
            float x = R(-0.85f, 0.85f) * extents.x, z = R(-0.8f, 0.8f) * extents.y;
            float y = Surface(x, z) * R(0.8f, 1.05f);
            var euler = new Vector3(R(-35f, 35f), R(0f, 360f), R(-35f, 35f));
            var pos = new Vector3(x, y, z);
            switch (rng.Next(9))
            {
                case 0:
                    CrushedCar(k, pos + Vector3.up * 0.2f, euler * 0.5f, paints[rng.Next(paints.Length)]);
                    break;
                case 1:
                    Tire(k, pos + Vector3.up * 0.1f, euler, R(0.45f, 0.65f));
                    break;
                case 2:
                    Barrel(k, pos, euler, paints[rng.Next(paints.Length)]);
                    break;
                case 3:
                    k.Cylinder(C(rng.Next(2) == 0 ? PC.Silver : PC.Rust), pos + Vector3.up * 0.1f, R(0.06f, 0.14f), R(1.2f, 2.6f), 10, euler, 0.02f);
                    break;
                case 4:
                    k.Box(C(PC.White), pos + Vector3.up * 0.3f, new Vector3(0.8f, R(0.8f, 1.4f), 0.7f), 0.08f, euler);
                    break;
                case 5:
                    // Engine block.
                    k.Push(pos + Vector3.up * 0.25f, euler);
                    k.Box(C(PC.Gray), Vector3.zero, new Vector3(0.8f, 0.5f, 0.6f), 0.06f);
                    for (int c = 0; c < 4; c++) k.Cylinder(C(PC.Charcoal), new Vector3(-0.3f + c * 0.2f, 0.32f, 0f), 0.07f, 0.16f, 8, default, 0.01f);
                    k.Pop();
                    break;
                case 6:
                    // Bicycle frame with two wheels.
                    k.Push(pos + Vector3.up * 0.15f, euler);
                    k.Torus(C(PC.Charcoal), new Vector3(0f, 0f, -0.45f), 0.3f, 0.03f, 12, 4, new Vector3(0f, 0f, 90f));
                    k.Torus(C(PC.Charcoal), new Vector3(0f, 0f, 0.45f), 0.3f, 0.03f, 12, 4, new Vector3(0f, 0f, 90f));
                    k.Box(C(paints[rng.Next(paints.Length)]), new Vector3(0f, 0.15f, 0f), new Vector3(0.05f, 0.05f, 0.9f), 0.01f, new Vector3(20f, 0f, 0f));
                    k.Pop();
                    break;
                case 7:
                    k.Box(C(paints[rng.Next(paints.Length)]), pos + Vector3.up * 0.1f, new Vector3(R(0.8f, 1.6f), 0.05f, R(0.5f, 1.0f)), 0.02f, euler);
                    break;
                default:
                    // Chair.
                    k.Push(pos + Vector3.up * 0.2f, euler);
                    k.Box(C(paints[rng.Next(paints.Length)]), Vector3.zero, new Vector3(0.45f, 0.06f, 0.45f), 0.02f);
                    k.Box(C(paints[rng.Next(paints.Length)]), new Vector3(0f, 0.25f, -0.2f), new Vector3(0.45f, 0.5f, 0.06f), 0.02f);
                    k.Pop();
                    break;
            }
        }

        return k;
    }

    /// <summary>Ribbed corrugated sheet wall along local +X with posts, colour-varied panels, rust and a top rail.</summary>
    static MeshKit CorrugatedWall(float length, int seed)
    {
        var rng = new Random(seed);
        PC[] colors = { PC.SteelBlue, PC.Gray, PC.Teal, PC.GrayLight, PC.SteelBlue, PC.RustDark, PC.Blue, PC.Cream };
        var k = new MeshKit();
        int panels = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
        float w = length / panels;
        for (int i = 0; i < panels; i++)
        {
            var c = colors[rng.Next(colors.Length)];
            float x = (i + 0.5f) * w;
            float tilt = (float)(rng.NextDouble() - 0.5) * 3f, lean = (float)(rng.NextDouble() - 0.5) * 2f;
            float h = 2.25f + (float)rng.NextDouble() * 0.15f;
            k.Push(new Vector3(x, 0f, 0f), new Vector3(lean, 0f, tilt));
            k.Box(C(c), new Vector3(0f, h * 0.5f, 0f), new Vector3(w - 0.04f, h, 0.05f), 0f);
            for (float rx = -w * 0.5f + 0.16f; rx < w * 0.5f - 0.05f; rx += 0.32f)
                for (int side = -1; side <= 1; side += 2)
                    k.Box(C(c), new Vector3(rx, h * 0.5f, side * 0.045f), new Vector3(0.11f, h - 0.04f, 0.05f), 0f);
            if (rng.NextDouble() < 0.35)
                k.Box(C(PC.Rust), new Vector3((float)(rng.NextDouble() - 0.5) * w * 0.6f, 0.4f + (float)rng.NextDouble() * 0.6f, -0.08f),
                    new Vector3(0.5f, 0.4f, 0.03f), 0.01f);
            k.Pop();
            k.Box(C(PC.Charcoal), new Vector3(i * w, 1.25f, 0f), new Vector3(0.16f, 2.5f, 0.16f), 0.03f);
        }

        k.Box(C(PC.Charcoal), new Vector3(length, 1.25f, 0f), new Vector3(0.16f, 2.5f, 0.16f), 0.03f);
        k.Box(C(PC.Yellow), new Vector3(length * 0.5f, 2.45f, 0f), new Vector3(length, 0.08f, 0.14f), 0.02f);
        return k;
    }

    // =====================================================================================
    // Ground textures and decals
    // =====================================================================================

    static Texture2D GroundTexture(string name, int size, Func<float, float, Color> f)
    {
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[y * size + x] = f(x / (float)size, y / (float)size);
        return ArtMaterials.SaveTexture($"{ArtAssets.TexDir}/{name}.png", size, px, true);
    }

    static float Pebbles(float u, float v, int cells, int seed)
    {
        // Voronoi-ish pebbles: distance to a jittered point per cell.
        float x = u * cells, y = v * cells;
        int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
        float best = 9f;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int gx = ((cx + dx) % cells + cells) % cells, gy = ((cy + dy) % cells + cells) % cells;
            float px = cx + dx + ArtMaterials.Noise(gx / (float)cells + 0.013f, gy / (float)cells, cells, seed);
            float py = cy + dy + ArtMaterials.Noise(gx / (float)cells, gy / (float)cells + 0.017f, cells, seed + 1);
            best = Mathf.Min(best, (x - px) * (x - px) + (y - py) * (y - py));
        }

        return Mathf.Sqrt(best);
    }

    static Material GroundMat(string name, Texture2D tex, float tileMetres, float smoothness = 0.08f)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat") ?? ArtMaterials.Mat(name.Replace("M_", ""), Color.white, smoothness);
        m.shader = Shader.Find("Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", Vector2.one / tileMetres);
        m.SetTextureOffset("_BaseMap", Vector2.zero);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void BuildGroundMaterials()
    {
        var dirt = GroundTexture("T_GroundDirt", 512, (u, v) =>
        {
            float n = ArtMaterials.Fbm(u, v, 6, 5, 3);
            float peb = Pebbles(u, v, 64, 9);
            var c = Color.Lerp(new Color(0.86f, 0.72f, 0.52f), new Color(0.74f, 0.6f, 0.42f), n);
            if (peb < 0.22f) c = Color.Lerp(c, new Color(0.62f, 0.52f, 0.4f), 0.55f);
            else if (peb < 0.3f) c *= 1.05f;
            return c;
        });
        GroundMat("M_Ground_Dirt", dirt, 8f);
        var oil = GroundTexture("T_GroundGravel", 512, (u, v) =>
        {
            float n = ArtMaterials.Fbm(u, v, 5, 5, 11);
            float peb = Pebbles(u, v, 80, 4);
            float stain = ArtMaterials.Spots(u, v, 6, 0.6f, 7);
            var c = Color.Lerp(new Color(0.66f, 0.6f, 0.52f), new Color(0.52f, 0.47f, 0.41f), n);
            c = Color.Lerp(c, peb < 0.25f ? new Color(0.72f, 0.69f, 0.64f) : new Color(0.46f, 0.42f, 0.38f), peb < 0.25f ? 0.5f : 0.15f);
            return Color.Lerp(c, new Color(0.3f, 0.27f, 0.25f), stain * 0.55f);
        });
        GroundMat("M_Ground_OilDirt", oil, 6f);
        Texture2D Concrete(string name, Color baseColor) => GroundTexture(name, 512, (u, v) =>
        {
            // 2 x 2 slabs per tile with joints, per-slab tone, hairline cracks and faint stains.
            float su = u * 2f, sv = v * 2f;
            int ix = Mathf.FloorToInt(su), iy = Mathf.FloorToInt(sv);
            float fu = su - ix, fv = sv - iy;
            float tone = ArtMaterials.Noise((ix + 0.5f) / 2f, (iy + 0.5f) / 2f, 2, 5) * 0.08f;
            float n = ArtMaterials.Fbm(u, v, 8, 4, 2);
            var c = baseColor * (0.95f + tone + (n - 0.5f) * 0.08f);
            float joint = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
            if (joint < 0.008f) c *= 0.72f;
            float crack = Mathf.Abs(ArtMaterials.Fbm(u * 1.3f, v, 4, 3, 19) - 0.5f);
            if (crack < 0.006f && ArtMaterials.Noise(u, v, 4, 23) > 0.55f) c *= 0.75f;
            float stain = ArtMaterials.Spots(u, v, 5, 0.66f, 31);
            c = Color.Lerp(c, c * 0.78f, stain);
            c.a = 1f;
            return c;
        });
        GroundMat("M_Ground_Concrete", Concrete("T_ConcreteSlabs", new Color(0.82f, 0.81f, 0.78f)), 4f, 0.12f);
        GroundMat("M_Ground_ConcreteSell", Concrete("T_ConcreteSlabsWarm", new Color(0.88f, 0.84f, 0.76f)), 4f, 0.12f);
        var asphalt = GroundTexture("T_Asphalt2", 512, (u, v) =>
        {
            float n = ArtMaterials.Fbm(u, v, 16, 4, 5);
            float peb = Pebbles(u, v, 96, 12);
            var c = new Color(0.27f, 0.28f, 0.31f) * (0.9f + n * 0.2f);
            if (peb < 0.18f) c *= 1.25f;
            float crack = Mathf.Abs(ArtMaterials.Fbm(u, v * 0.7f, 3, 3, 41) - 0.5f);
            if (crack < 0.0025f && ArtMaterials.Noise(u, v, 3, 9) > 0.6f) c *= 0.8f;
            c.a = 1f;
            return c;
        });
        GroundMat("M_Ground_Asphalt", asphalt, 6f, 0.15f);
        var locked = GroundMat("M_Ground_Locked", dirt, 8f);
        locked.SetColor("_BaseColor", new Color(0.62f, 0.56f, 0.5f));
        var grass = GroundTexture("T_Grass2", 512, (u, v) =>
        {
            float n = ArtMaterials.Fbm(u, v, 6, 4, 8);
            float clumps = ArtMaterials.Fbm(u, v, 24, 2, 3);
            var c = Color.Lerp(new Color(0.5f, 0.76f, 0.32f), new Color(0.38f, 0.63f, 0.26f), n);
            c = Color.Lerp(c, new Color(0.62f, 0.84f, 0.38f), Mathf.Clamp01((clumps - 0.6f) * 3f));
            float flower = ArtMaterials.Spots(u, v, 48, 0.86f, 17);
            if (flower > 0.3f) c = ArtMaterials.Noise(u, v, 32, 3) > 0.5f ? new Color(1f, 0.92f, 0.4f) : new Color(1f, 1f, 1f);
            return c;
        });
        GroundMat("M_Ground_Grass", grass, 7f);
        var curb = GroundTexture("T_Curb", 256, (u, v) => new Color(0.86f, 0.85f, 0.82f) * (0.94f + ArtMaterials.Fbm(u, v, 8, 3, 2) * 0.1f));
        GroundMat("M_Curb", curb, 2f);
    }

    const int DecalCells = 4;

    enum DecalKind { OilA, OilB, OilC, CrackA, CrackB, CrackC, Tire, Puddle, ScuffLight, ScuffDark, Rust, Leaves, Grate, Splat, Debris, Patch }

    static Texture2D DecalAtlas()
    {
        const int cell = 128, size = cell * DecalCells;
        var px = new Color[size * size];
        for (int i = 0; i < 16; i++)
        {
            int cx = i % DecalCells, cy = i / DecalCells;
            var kind = (DecalKind)i;
            for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                float u = x / (float)cell, v = y / (float)cell;
                float du = u - 0.5f, dv = v - 0.5f, r = Mathf.Sqrt(du * du + dv * dv) * 2f;
                float edge = ArtMaterials.Fbm(u, v, 4, 3, i * 7 + 1);
                Color c = new(0f, 0f, 0f, 0f);
                switch (kind)
                {
                    case DecalKind.OilA:
                    case DecalKind.OilB:
                    case DecalKind.OilC:
                    {
                        float shape = r + (edge - 0.5f) * 0.9f;
                        float a = Mathf.Clamp01((0.75f - shape) * 6f);
                        c = new Color(0.16f, 0.13f, 0.11f, a * 0.38f);
                        break;
                    }
                    case DecalKind.CrackA:
                    case DecalKind.CrackB:
                    case DecalKind.CrackC:
                    {
                        float line = Mathf.Abs(ArtMaterials.Fbm(u * 0.8f, v, 3, 3, i * 13) - 0.5f) + Mathf.Abs(du) * 0.05f;
                        float branch = Mathf.Abs(ArtMaterials.Fbm(u, v * 0.8f, 3, 3, i * 17) - 0.5f);
                        float a = Mathf.Max(line < 0.007f ? 1f : 0f, branch < 0.005f && r < 0.6f ? 1f : 0f) * Mathf.Clamp01((1f - r) * 3f);
                        c = new Color(0.2f, 0.19f, 0.18f, a * 0.45f);
                        break;
                    }
                    case DecalKind.Tire:
                    {
                        float a = 0f;
                        foreach (float lane in new[] { 0.3f, 0.7f })
                        {
                            float d = Mathf.Abs(u - lane);
                            float tread = Mathf.Repeat(v * 16f, 1f) < 0.55f ? 1f : 0.7f;
                            if (d < 0.07f) a = Mathf.Max(a, (1f - d / 0.07f) * tread);
                        }

                        c = new Color(0.1f, 0.09f, 0.08f, a * 0.35f * Mathf.Clamp01(Mathf.Min(v, 1f - v) * 8f));
                        break;
                    }
                    case DecalKind.Puddle:
                    {
                        float shape = r + (edge - 0.5f) * 0.7f;
                        float a = Mathf.Clamp01((0.7f - shape) * 8f);
                        c = new Color(0.55f, 0.72f, 0.86f, a * 0.45f);
                        break;
                    }
                    case DecalKind.ScuffLight:
                    case DecalKind.ScuffDark:
                    {
                        float shape = r * 0.9f + (edge - 0.5f) * 1.2f;
                        float a = Mathf.Clamp01((0.7f - shape) * 2f) * (0.6f + ArtMaterials.Fbm(u, v, 16, 2, 3) * 0.4f);
                        c = kind == DecalKind.ScuffLight ? new Color(1f, 0.95f, 0.82f, a * 0.22f) : new Color(0.45f, 0.33f, 0.22f, a * 0.2f);
                        break;
                    }
                    case DecalKind.Rust:
                    {
                        float shape = r + (edge - 0.5f) * 0.8f;
                        c = new Color(0.55f, 0.27f, 0.1f, Mathf.Clamp01((0.7f - shape) * 4f) * 0.55f);
                        break;
                    }
                    case DecalKind.Leaves:
                    {
                        float s = ArtMaterials.Spots(u, v, 12, 0.7f, 5);
                        c = new Color(0.45f, 0.62f, 0.25f, s * Mathf.Clamp01((1f - r) * 2f));
                        break;
                    }
                    case DecalKind.Grate:
                    {
                        bool ring = r < 0.85f && r > 0.72f;
                        bool bars = r < 0.72f && Mathf.Repeat(u * 8f, 1f) < 0.35f;
                        c = ring ? new Color(0.3f, 0.31f, 0.33f, 1f) : bars ? new Color(0.22f, 0.23f, 0.25f, 1f) : r < 0.72f ? new Color(0.08f, 0.08f, 0.09f, 1f) : c;
                        break;
                    }
                    case DecalKind.Splat:
                    {
                        float shape = r + (edge - 0.5f) * 1.1f;
                        c = new Color(1f, 0.78f, 0.1f, Mathf.Clamp01((0.55f - shape) * 6f) * 0.85f);
                        break;
                    }
                    case DecalKind.Debris:
                    {
                        float s = Pebbles(u, v, 10, i) < 0.12f && r < 0.9f ? 1f : 0f;
                        c = new Color(0.45f, 0.42f, 0.4f, s * 0.35f);
                        break;
                    }
                    case DecalKind.Patch:
                    {
                        bool inside = Mathf.Abs(du) < 0.42f && Mathf.Abs(dv) < 0.42f;
                        float n = ArtMaterials.Fbm(u, v, 8, 3, 9);
                        c = inside ? new Color(0.62f + n * 0.1f, 0.6f + n * 0.1f, 0.57f + n * 0.1f, 0.85f) : c;
                        break;
                    }
                }

                px[(cy * cell + y) * size + cx * cell + x] = c;
            }
        }

        return ArtMaterials.SaveTexture($"{ArtAssets.TexDir}/T_Decals.png", size, px, false, true);
    }

    static Rect DecalRect(DecalKind kind)
    {
        int i = (int)kind;
        float s = 1f / DecalCells;
        return new Rect((i % DecalCells) * s, (i / DecalCells) * s, s, s);
    }

    // =====================================================================================
    // Scene
    // =====================================================================================

    static Transform Root(Scene scene, string name) => scene.GetRootGameObjects().First(g => g.name == name).transform;

    static Transform Reset(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
        t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Place(string prop, Transform parent, Vector3 pos, float yaw = 0f, float scale = 1f)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/{prop}.prefab");
        if (src == null)
        {
            Log.AppendLine("!! missing prop " + prop);
            return null;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    static void DeleteKenney(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true).ToArray())
        {
            if (t == null || !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
            if (AssetDatabase.GetAssetPath(src).Contains("ThirdParty/Kenney")) Object.DestroyImmediate(t.gameObject);
        }
    }

    /// <summary>Gives a scaled-cube slab a real mesh at unit scale with UVs in metres (consistent texture scale on every slab).</summary>
    static void Reslab(Transform slab)
    {
        if (slab == null) return;
        var mf = slab.GetComponent<MeshFilter>();
        if (mf == null) return;
        Vector3 size = slab.localScale;
        if (Vector3.Distance(size, Vector3.one) < 0.001f && mf.sharedMesh != null && mf.sharedMesh.name == slab.name) return;
        var k = new MeshKit { UvScale = 1f };
        k.Box(0, Vector3.zero, size, 0f);
        var mesh = ArtAssets.SaveMesh("Ground", slab.name, k.ToMesh(slab.name));
        mf.sharedMesh = mesh;
        slab.localScale = Vector3.one;
        var box = slab.GetComponent<BoxCollider>();
        if (box != null)
        {
            box.center = Vector3.zero;
            box.size = size;
        }
    }

    public static string Scene()
    {
        Log.Clear();
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var env = Root(scene, "_Environment");
        var gameplay = Root(scene, "_Gameplay");
        var systems = Root(scene, "_Systems");

        // ---------- ground ----------
        BuildGroundMaterials();
        foreach (var n in new[] { "WorldGround", "Road_South", "Sidewalk", "Area1_Ground", "ScrapZone_Floor", "WorkArea_Concrete", "SellArea_Concrete", "Area2_Locked",
                     "Area3_Locked" })
            Reslab(env.Find(n));
        foreach (var path in new[] { "RecyclingPlant/Content/Floor_Production", "RecyclingPlant/Content/Floor_Market", "FurnaceHall/Content/Floor_Hall" })
            Reslab(gameplay.Find(path));

        // ---------- walls: custom corrugated sheets on every Wall_* segment ----------
        var fences = env.Find("Fences");
        int seed = 3;
        foreach (Transform wall in fences)
        {
            if (!wall.name.StartsWith("Wall_")) continue;
            var box = wall.GetComponent<BoxCollider>();
            float length = box != null ? box.size.x : 4f;
            foreach (Transform c in wall.Cast<Transform>().ToArray()) Object.DestroyImmediate(c.gameObject);
            var mesh = ArtAssets.SaveMesh("Walls", wall.name, CorrugatedWall(length, seed++).ToPaletteMesh(wall.name));
            var go = ArtAssets.MeshObject("Sheets", wall, mesh, new[] { ArtPalette.Material });
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // ---------- scrap mounds replace every Kenney junk heap ----------
        var heapSites = new List<(Transform parent, string name, Vector3 pos, Vector2 ext)>();
        void CollectHeaps(Transform parent)
        {
            if (parent == null) return;
            foreach (Transform h in parent.Cast<Transform>().ToArray())
            {
                if (!h.name.StartsWith("Heap")) continue;
                var box = h.GetComponent<BoxCollider>();
                var ext = box != null ? new Vector2(box.size.x * 0.5f, box.size.z * 0.5f) : new Vector2(2f, 1f);
                heapSites.Add((parent, h.name, h.position, ext));
                Object.DestroyImmediate(h.gameObject);
            }
        }

        CollectHeaps(env.Find("JunkPiles"));
        CollectHeaps(gameplay.Find("HeavyYard/Content/Decor"));
        foreach (var (parent, name, pos, ext) in heapSites)
        {
            var heap = new GameObject(name).transform;
            heap.SetParent(parent, false);
            heap.position = pos;
            float height = Mathf.Clamp(Mathf.Min(ext.x, ext.y) * 1.4f + 0.6f, 1.0f, 2.4f);
            string key = $"{name}_{pos.x:0}_{pos.z:0}";
            var mesh = ArtAssets.SaveMesh("Heaps", key, ScrapMound(ext, Mathf.RoundToInt(pos.x * 31f + pos.z * 17f), height).ToPaletteMesh(key));
            var go = ArtAssets.MeshObject("Mound", heap, mesh, new[] { ArtPalette.Material });
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            var box = heap.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, height * 0.4f, 0f);
            box.size = new Vector3(ext.x * 2f, height * 0.8f, ext.y * 2f);
        }

        // ---------- extra scrap mounds along the walls (the yard's "huge piles"), gaps kept at gates and routes ----------
        var extra = new (Transform parent, string name, Vector3 pos, Vector2 ext, float yaw)[]
        {
            (env.Find("JunkPiles"), "Heap_WestField", new Vector3(1.5f, 0f, 30f), new Vector2(1.3f, 5.5f), 0f),
            (env.Find("JunkPiles"), "Heap_NorthA", new Vector3(16.9f, 0f, 36.8f), new Vector2(2.0f, 1.0f), 0f),
            (gameplay.Find("RecyclingPlant/Content"), "Heap_PlantWest", new Vector3(41.6f, 0f, 32.5f), new Vector2(1.3f, 3.5f), 0f),
            (gameplay.Find("FurnaceHall/Content"), "Heap_HallEast", new Vector3(74.3f, 0f, 35.5f), new Vector2(1.2f, 4.5f), 0f),
            (gameplay.Find("HeavyYard/Content/Decor"), "Heap_HeavySouthW", new Vector3(4.2f, 0f, 39.9f), new Vector2(3.6f, 1.2f), 0f),
            (gameplay.Find("HeavyYard/Content/Decor"), "Heap_HeavySouthE", new Vector3(28f, 0f, 39.9f), new Vector2(9f, 1.2f), 0f),
            (gameplay.Find("HeavyYard/Content/Decor"), "Heap_HeavyEastMid", new Vector3(35.5f, 0f, 44.5f), new Vector2(2.2f, 2.4f), 0f),
            // (The spot under the tower crane, around (17.5, 64.5), is the Giant Scrap landing zone since M7: keep it clear.)
        };
        foreach (var (parent, name, pos, ext, yaw) in extra)
        {
            if (parent == null) continue;
            var old = parent.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var heap = new GameObject(name).transform;
            heap.SetParent(parent, false);
            heap.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            float height = Mathf.Clamp(Mathf.Min(ext.x, ext.y) * 1.5f + 0.8f, 1.4f, 2.8f);
            var mesh = ArtAssets.SaveMesh("Heaps", name, ScrapMound(ext, Mathf.RoundToInt(pos.x * 31f + pos.z * 17f), height).ToPaletteMesh(name));
            var go = ArtAssets.MeshObject("Mound", heap, mesh, new[] { ArtPalette.Material });
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            var box = heap.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, height * 0.4f, 0f);
            box.size = new Vector3(ext.x * 2f, height * 0.8f, ext.y * 2f);
        }

        // ---------- everything else that was Kenney goes ----------
        DeleteKenney(env);
        DeleteKenney(gameplay.Find("HeavyYard"));
        DeleteKenney(gameplay.Find("RecyclingPlant"));
        DeleteKenney(gameplay.Find("BackLot"));

        // ---------- backdrop: trees, warehouses, cranes, water ----------
        var backdrop = Reset(env, "ArtBackdrop");
        var rng = new Random(11);
        float Rf(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        void Trees(Vector3 from, Vector3 to, int count, float jitter)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Vector3.Lerp(from, to, (i + 0.5f) / count) + new Vector3(Rf(-jitter, jitter), 0f, Rf(-jitter, jitter));
                Place("Prop_Tree_" + rng.Next(3), backdrop, p, Rf(0f, 360f), Rf(0.9f, 1.3f));
                if (rng.NextDouble() < 0.5) Place("Prop_Bush", backdrop, p + new Vector3(Rf(-2f, 2f), 0f, Rf(-2f, 2f)), Rf(0f, 360f), Rf(0.8f, 1.4f));
            }
        }

        Trees(new Vector3(-3.5f, 0f, 12f), new Vector3(-3.5f, 0f, 74f), 16, 1.2f);
        Trees(new Vector3(-8f, 0f, 14f), new Vector3(-8f, 0f, 76f), 12, 1.8f);
        Trees(new Vector3(-10f, 0f, -4f), new Vector3(120f, 0f, -4f), 22, 1.5f);
        Trees(new Vector3(-6f, 0f, 77f), new Vector3(42f, 0f, 77f), 10, 1.2f);
        Place("Prop_Warehouse_Blue", backdrop, new Vector3(14f, 0f, -11f), 180f);
        Place("Prop_Warehouse_Cream", backdrop, new Vector3(34f, 0f, -11f), 180f);
        Place("Prop_Warehouse_Blue", backdrop, new Vector3(58f, 0f, -11f), 180f);
        Place("Prop_Tank", backdrop, new Vector3(76f, 0f, -9f));
        Place("Prop_Tank", backdrop, new Vector3(80f, 0f, -9.5f), 30f, 0.8f);
        // (East of the plant is the Dockyard quay since M8: M8_Build.Scene builds the water, ship and cranes there.)
        for (int i = 0; i < 6; i++)
        {
            string[] cols = { "Blue", "Red", "Green", "Orange", "Yellow", "Teal" };
            var c = Place("Prop_Container_" + cols[rng.Next(cols.Length)], backdrop, new Vector3(82f + (i % 3) * 2.6f, (i / 3) * 2.6f, 50f), 0f);
            _ = c;
        }

        Place("Prop_GantryCrane", backdrop, new Vector3(86f, 0f, 58f), 90f);
        var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
        water.name = "Water";
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.transform.SetParent(backdrop, false);
        water.transform.position = new Vector3(60f, -0.03f, 100f);
        water.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        water.transform.localScale = new Vector3(240f, 40f, 1f);
        water.GetComponent<Renderer>().sharedMaterial = ArtMaterials.Mat("Water", new Color(0.25f, 0.6f, 0.82f), 0.92f, 0f, ArtMaterials.Detail.Plain);
        for (int i = 0; i < 8; i++) Place("Prop_UtilityPole", backdrop, new Vector3(-6f + i * 12f, 0f, -1.2f), 0f);

        // ---------- yard clusters ----------
        var decor = Reset(env, "ArtDecor");
        // Old Yard: office + pallets SW, container + barrels along the east wall, workbench, crushed-car stack, light poles.
        Place("Prop_Office", decor, new Vector3(7.8f, 0f, 12.2f), 180f);
        Place("Prop_PalletStack", decor, new Vector3(11.4f, 0f, 11.6f), 12f);
        Place("Prop_GasCylinders", decor, new Vector3(4.4f, 0f, 14.8f), 90f);
        Place("Prop_Container_Blue", decor, new Vector3(38.5f, 0f, 18.2f), 0f);
        Place("Prop_Barrels", decor, new Vector3(38.5f, 0f, 21.9f), 100f);
        Place("Prop_Workbench", decor, new Vector3(19.2f, 0f, 11.4f), 180f);
        Place("Prop_Generator", decor, new Vector3(16.6f, 0f, 11.5f), 0f);
        Place("Prop_CrushedCars", decor, new Vector3(5.2f, 0f, 37.0f), 0f);
        Place("Prop_Tires", decor, new Vector3(1.2f, 0f, 23.2f), 40f);
        foreach (var p in new[] { new Vector3(19.4f, 0f, 14.2f), new Vector3(31.2f, 0f, 21.6f), new Vector3(1.0f, 0f, 30.5f), new Vector3(36.6f, 0f, 11.4f) })
            Place("Prop_LightPole", decor, p, p.x < 10f ? 90f : 180f);
        Place("Prop_Flag", decor, new Vector3(29.4f, 0f, 11.2f), 0f);
        Place("Prop_Flag", decor, new Vector3(34.7f, 0f, 11.2f), 0f);
        // Recycling Plant: tanks + pipe rack north, pallets of bales and a forklift doing rounds south-west, beams, dumpster.
        var plantDecor = Reset(gameplay.Find("RecyclingPlant/Content"), "ArtDecor");
        Place("Prop_Tank", plantDecor, new Vector3(43.2f, 0f, 38.6f));
        Place("Prop_Tank", plantDecor, new Vector3(46.6f, 0f, 39.2f), 40f, 0.85f);
        Place("Prop_PipeRack", plantDecor, new Vector3(54.0f, 0f, 40.6f), 0f);
        Place("Prop_PalletBales", plantDecor, new Vector3(41.6f, 0f, 12.4f), 8f);
        Place("Prop_PalletBales", plantDecor, new Vector3(42.9f, 0f, 12.2f), -6f);
        Place("Prop_PalletBales", plantDecor, new Vector3(41.7f, 0f, 13.8f), 3f);
        Place("Prop_Beams", plantDecor, new Vector3(58.2f, 0f, 38.4f), 90f);
        // (The plant's south-east corner is the Truck Dock since M7: no decor there.)
        Place("Prop_Container_Orange", plantDecor, new Vector3(72.6f, 0f, 16.6f), 90f);
        var forklift = Place("Prop_Forklift", plantDecor, new Vector3(44.5f, 0f, 13.5f), 0f);
        if (forklift != null)
        {
            var route = Reset(plantDecor, "ForkliftRoute");
            var pts = new[] { new Vector3(44.6f, 0f, 13.0f), new Vector3(44.6f, 0f, 17.6f), new Vector3(42.6f, 0f, 17.8f), new Vector3(42.4f, 0f, 15.6f) }
                .Select((p, i) =>
                {
                    var t = new GameObject("P" + i).transform;
                    t.SetParent(route, false);
                    t.position = p;
                    return (Object)t;
                }).ToArray();
            var path = forklift.AddComponent<AmbientPath>();
            ArtAssets.Set(path, ("speed", 2.2f), ("pause", 1.2f), ("loop", true), ("wheelRadius", 0.3f));
            ArtAssets.SetArray(path, "points", pts);
            ArtAssets.SetArray(path, "wheels", forklift.GetComponentsInChildren<Transform>().Where(t => t.name == "Wheel").Cast<Object>().ToArray());
        }

        // Furnace plot teaser (inside the plant content): cones and crates.
        var plot = gameplay.Find("RecyclingPlant/Content/FurnacePlot/LockedOnly");
        if (plot != null)
        {
            var teaser = Reset(plot, "ArtTeaser");
            for (int i = 0; i < 6; i++) Place("Prop_Cone", teaser, new Vector3(64.5f + i, 0f, 28.2f + (i % 2) * 0.4f), i * 17f);
            Place("Prop_Crate", teaser, new Vector3(69.4f, 0f, 32.9f), 20f);
            Place("Prop_Crate", teaser, new Vector3(70.3f, 0f, 31.8f), -15f);
            Place("Prop_Beams", teaser, new Vector3(66f, 0f, 35f), 90f);
        }

        // Heavy Scrap Yard: tower crane, container stacks, tank, generator, beams.
        var heavyDecor = gameplay.Find("HeavyYard/Content/Decor");
        if (heavyDecor != null)
        {
            // This root is shared with the M6 builder (blockers, heaps), so it is not reset: clear our own props first.
            foreach (var old in heavyDecor.Cast<Transform>().Where(t => t.name.StartsWith("Prop_")).ToArray()) Object.DestroyImmediate(old.gameObject);
            Place("Prop_TowerCrane", heavyDecor, new Vector3(20f, 0f, 71.4f), 180f);
            Place("Prop_Container_Red", heavyDecor, new Vector3(3.6f, 0f, 70.5f), 90f);
            Place("Prop_Container_Yellow", heavyDecor, new Vector3(3.8f, 2.6f, 70.6f), 92f);
            Place("Prop_Container_Teal", heavyDecor, new Vector3(9.8f, 0f, 71.6f), 90f);
            Place("Prop_Tank", heavyDecor, new Vector3(36.5f, 0f, 70.5f));
            Place("Prop_Generator", heavyDecor, new Vector3(30.5f, 0f, 72.2f), 0f);
            Place("Prop_Beams", heavyDecor, new Vector3(26f, 0f, 71.8f), 90f);
            Place("Prop_CrushedCars", heavyDecor, new Vector3(13.2f, 0f, 68.6f), -10f);
            Place("Prop_Tires", heavyDecor, new Vector3(33.5f, 0f, 49.4f), 10f);
            Place("Prop_Barrels", heavyDecor, new Vector3(31.8f, 0f, 41.6f), -30f);
            Place("Prop_LightPole", heavyDecor, new Vector3(1.0f, 0f, 46f), 90f);
            Place("Prop_LightPole", heavyDecor, new Vector3(39.0f, 0f, 62f), -90f);
            Place("Prop_Container_Blue", heavyDecor, new Vector3(36.8f, 0f, 70.8f), 0f);
        }

        // Plant extras: barrels and crates by the walls, a light pole, cylinders by the furnace hall.
        Place("Prop_Barrels", plantDecor, new Vector3(41.4f, 0f, 27.6f), 70f);
        Place("Prop_Crate", plantDecor, new Vector3(57.4f, 0f, 33.6f), 10f);
        Place("Prop_Crate", plantDecor, new Vector3(58.3f, 0f, 34.4f), -25f);
        Place("Prop_Crate", plantDecor, new Vector3(57.8f, 0.9f, 33.9f), 40f);
        Place("Prop_LightPole", plantDecor, new Vector3(43.5f, 0f, 33.0f), 180f);
        Place("Prop_CrushedCars", plantDecor, new Vector3(55.8f, 0f, 36.4f), 5f);
        Place("Prop_PalletBales", plantDecor, new Vector3(53.2f, 0f, 34.0f), 12f);
        Place("Prop_PalletBales", plantDecor, new Vector3(54.6f, 0f, 33.7f), -8f);
        Place("Prop_PalletStack", plantDecor, new Vector3(53.8f, 0f, 35.4f), 30f);
        Place("Prop_Tires", plantDecor, new Vector3(41.2f, 0f, 20.4f), 0f);
        var hallContent = gameplay.Find("FurnaceHall/Content");
        if (hallContent != null)
        {
            var hallDecor = Reset(hallContent, "ArtDecor");
            // Kept clear of Gate 4 (east wall, z 24) and its tile.
            Place("Prop_GasCylinders", hallDecor, new Vector3(74.9f, 0f, 20.9f), 90f);
            Place("Prop_PalletStack", hallDecor, new Vector3(74.4f, 0f, 29.6f), 5f);
            Place("Prop_LightPole", hallDecor, new Vector3(62f, 0f, 40.8f), 180f);
        }

        // Back Lot extras (pop in with the lot).
        var lotContent = gameplay.Find("BackLot/Content");
        if (lotContent != null)
        {
            var lotDecor = Reset(lotContent, "ArtDecor");
            Place("Prop_Tires", lotDecor, new Vector3(20.6f, 0f, 36.4f), 30f);
            Place("Prop_Barrels", lotDecor, new Vector3(38.4f, 0f, 34.8f), 80f);
        }

        // ---------- decals ----------
        var decalMat = ArtMaterials.Transparent("Decals", Color.white, DecalAtlas());
        var decals = Reset(env, "ArtDecals");
        var dk = new MeshKit();
        var drng = new Random(5);
        float D(float a, float b) => a + (float)drng.NextDouble() * (b - a);
        var keepOut = new List<Rect>();
        foreach (var pad in Object.FindObjectsByType<ScrapYardKing.Items.TransferPad>(FindObjectsInactive.Include))
            keepOut.Add(new Rect(pad.transform.position.x - 1.4f, pad.transform.position.z - 1.4f, 2.8f, 2.8f));
        foreach (var tile in Object.FindObjectsByType<ScrapYardKing.Tiles.Tile>(FindObjectsInactive.Include))
            keepOut.Add(new Rect(tile.transform.position.x - 1.5f, tile.transform.position.z - 1.5f, 3f, 3f));
        bool Free(float x, float z) => !keepOut.Any(r => r.Contains(new Vector2(x, z)));
        void Scatter(Rect area, DecalKind[] kinds, int count, Vector2 size, float y, MeshKit target = null)
        {
            var into = target ?? dk;
            for (int i = 0; i < count; i++)
            {
                float x = D(area.xMin, area.xMax), z = D(area.yMin, area.yMax);
                if (!Free(x, z)) continue;
                var kind = kinds[drng.Next(kinds.Length)];
                float s = D(size.x, size.y);
                var sz = kind == DecalKind.Tire ? new Vector2(s * 0.5f, s * 2.2f) : new Vector2(s, s);
                into.Plane(0, new Vector3(x, y + D(0f, 0.004f), z), sz, new Vector3(0f, D(0f, 360f), 0f), false, DecalRect(kind));
            }
        }

        var oil = new[] { DecalKind.OilA, DecalKind.OilB, DecalKind.OilC };
        var cracks = new[] { DecalKind.CrackA, DecalKind.CrackB, DecalKind.CrackC };
        // Big soft patches first (break up texture tiling), then sparse detail. Dirty, not ugly.
        Scatter(new Rect(1f, 11f, 38f, 27f), new[] { DecalKind.ScuffLight, DecalKind.ScuffDark }, 34, new Vector2(4f, 8f), 0.026f);
        // Plant and heavy yard decals belong to their areas (they appear when the gate opens, not on the locked ground).
        var plantKit = new MeshKit();
        var heavyKit = new MeshKit();
        Scatter(new Rect(41f, 11f, 34f, 31f), new[] { DecalKind.ScuffLight, DecalKind.ScuffDark }, 30, new Vector2(4f, 8f), 0.03f, plantKit);
        Scatter(new Rect(1f, 39f, 38f, 34f), new[] { DecalKind.ScuffLight, DecalKind.ScuffDark }, 36, new Vector2(4f, 9f), 0.02f, heavyKit);
        Scatter(new Rect(1f, 23f, 17f, 13f), oil.Concat(new[] { DecalKind.Rust, DecalKind.Tire }).ToArray(), 12, new Vector2(0.9f, 1.8f), 0.032f);
        Scatter(new Rect(1f, 11f, 38f, 26f), new[] { DecalKind.Tire, DecalKind.Leaves, DecalKind.OilC }, 18, new Vector2(1.2f, 2.4f), 0.03f);
        Scatter(new Rect(19.5f, 16.5f, 10f, 13f), cracks.Concat(new[] { DecalKind.Patch, DecalKind.OilA, DecalKind.Tire }).ToArray(), 12, new Vector2(0.9f, 1.8f), 0.036f);
        Scatter(new Rect(43f, 15f, 14f, 16f), cracks.Concat(new[] { DecalKind.Patch, DecalKind.OilB, DecalKind.Puddle }).ToArray(), 12, new Vector2(0.9f, 1.8f), 0.038f,
            plantKit);
        Scatter(new Rect(-5f, 0.5f, 130f, 9f), new[] { DecalKind.Patch, DecalKind.OilB, DecalKind.CrackA }, 14, new Vector2(0.8f, 1.6f), 0.006f);
        // Soften the straight edges of the oily scrap field with scuffs and debris along its border.
        for (float x = 1.5f; x < 18.5f; x += 1.3f)
            foreach (float z in new[] { 23.5f, 36.5f })
                if (Free(x, z)) dk.Plane(0, new Vector3(x, 0.03f, z + D(-0.4f, 0.4f)), Vector2.one * D(1.4f, 2.4f), new Vector3(0f, D(0f, 360f), 0f), false,
                    DecalRect(drng.Next(2) == 0 ? DecalKind.ScuffDark : DecalKind.OilC));
        for (float z = 24f; z < 36.5f; z += 1.3f)
            if (Free(18.5f, z)) dk.Plane(0, new Vector3(18.5f + D(-0.4f, 0.4f), 0.03f, z), Vector2.one * D(1.4f, 2.4f), new Vector3(0f, D(0f, 360f), 0f), false,
                DecalRect(DecalKind.ScuffDark));
        Scatter(new Rect(1f, 39f, 38f, 34f), new[] { DecalKind.Tire, DecalKind.OilA, DecalKind.Rust }, 16, new Vector2(1.4f, 2.6f), 0.024f, heavyKit);
        foreach (var (kit, contentPath, name) in new[] { (plantKit, "RecyclingPlant/Content", "PlantDecals"), (heavyKit, "HeavyYard/Content", "HeavyDecals") })
        {
            var content = gameplay.Find(contentPath);
            if (content == null) continue;
            var holder = Reset(content, "ArtDecals");
            var mesh = ArtAssets.SaveMesh("Ground", name, kit.ToMesh(name));
            var go = ArtAssets.MeshObject("Decals", holder, mesh, new[] { decalMat }, default, default, false);
            go.GetComponent<Renderer>().receiveShadows = false;
            if (!holder.TryGetComponent(out NavMeshModifier mod)) mod = holder.gameObject.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;
        }
        foreach (var p in new[] { new Vector3(26.8f, 0.04f, 15.0f), new Vector3(8f, 0.034f, 15.4f) })
            dk.Plane(0, p, new Vector2(0.9f, 0.9f), default, false, DecalRect(DecalKind.Grate));
        var decalMesh = ArtAssets.SaveMesh("Ground", "Decals", dk.ToMesh("Decals"));
        var decalGo = ArtAssets.MeshObject("Decals", decals, decalMesh, new[] { decalMat }, default, default, false);
        decalGo.GetComponent<Renderer>().receiveShadows = false;

        // Ground clutter: small flat junk (planks, cans, bolts, a flat tyre) and weed tufts along walls and fences. No colliders.
        var clutter = new MeshKit();
        var crng = new Random(21);
        float Cr(float a, float b) => a + (float)crng.NextDouble() * (b - a);
        PC[] bits = { PC.Wood, PC.Gray, PC.Rust, PC.Red, PC.Blue, PC.Yellow, PC.Silver, PC.Charcoal };
        void Bits(Rect area, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float x = Cr(area.xMin, area.xMax), z = Cr(area.yMin, area.yMax);
                if (!Free(x, z)) continue;
                var pos = new Vector3(x, 0.03f, z);
                var yaw = new Vector3(0f, Cr(0f, 360f), 0f);
                switch (crng.Next(6))
                {
                    case 0: clutter.Box(C(PC.Wood), pos + Vector3.up * 0.03f, new Vector3(0.18f, 0.05f, Cr(0.8f, 1.4f)), 0f, yaw); break;
                    case 1: clutter.Cylinder(C(bits[crng.Next(bits.Length)]), pos + Vector3.up * 0.06f, 0.06f, 0.18f, 6, yaw + new Vector3(0f, 0f, 90f), 0f); break;
                    case 2: clutter.Torus(C(PC.Rubber), pos + Vector3.up * 0.08f, 0.32f, 0.12f, 10, 5); break;
                    case 3: clutter.Box(C(bits[crng.Next(bits.Length)]), pos + Vector3.up * 0.02f, new Vector3(Cr(0.3f, 0.6f), 0.03f, Cr(0.2f, 0.45f)), 0f, yaw); break;
                    case 4: clutter.Cylinder(C(PC.Gray), pos + Vector3.up * 0.04f, 0.05f, Cr(0.6f, 1.2f), 6, yaw + new Vector3(90f, 0f, 0f), 0f); break;
                    default: clutter.Box(C(PC.Gray), pos + Vector3.up * 0.04f, new Vector3(0.14f, 0.08f, 0.14f), 0f, yaw); break;
                }
            }
        }

        void Weeds(Vector3 from, Vector3 to, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Vector3.Lerp(from, to, (float)crng.NextDouble()) + new Vector3(Cr(-0.4f, 0.4f), 0.02f, Cr(-0.4f, 0.4f));
                if (!Free(p.x, p.z)) continue;
                float s = Cr(0.18f, 0.32f);
                clutter.Sphere(C(crng.Next(2) == 0 ? PC.Leaf : PC.LeafDark), p, new Vector3(s, s * 0.7f, s), 6, 4);
                clutter.Sphere(C(PC.Lime), p + new Vector3(s * 0.7f, 0f, s * 0.3f), new Vector3(s * 0.6f, s * 0.5f, s * 0.6f), 6, 4);
            }
        }

        Bits(new Rect(2f, 12f, 16f, 10f), 26);
        Bits(new Rect(30f, 24f, 8f, 5.5f), 6);
        Bits(new Rect(2f, 23f, 16f, 13f), 18);
        Weeds(new Vector3(0.6f, 0f, 11.2f), new Vector3(0.6f, 0f, 37.4f), 26);
        Weeds(new Vector3(1f, 0f, 11.2f), new Vector3(26f, 0f, 11.2f), 18);
        Weeds(new Vector3(34f, 0f, 11.2f), new Vector3(39.4f, 0f, 11.2f), 6);
        Weeds(new Vector3(39.4f, 0f, 11.5f), new Vector3(39.4f, 0f, 15f), 5);
        Weeds(new Vector3(1f, 0f, 37.4f), new Vector3(9f, 0f, 37.4f), 8);
        var clutterMesh = ArtAssets.SaveMesh("Ground", "Clutter", clutter.ToPaletteMesh("Clutter"));
        var clutterGo = ArtAssets.MeshObject("Clutter", decals, clutterMesh, new[] { ArtPalette.Material }, default, default, false);
        GameObjectUtility.SetStaticEditorFlags(clutterGo, StaticEditorFlags.BatchingStatic);

        // Painted floor lines: yellow walkway border round the Old Yard work area and the plant floor.
        var lines = new MeshKit();
        void Outline(Rect r, float y)
        {
            lines.Box(C(PC.Yellow), new Vector3(r.center.x, y, r.yMin), new Vector3(r.width, 0.01f, 0.12f), 0f);
            lines.Box(C(PC.Yellow), new Vector3(r.center.x, y, r.yMax), new Vector3(r.width, 0.01f, 0.12f), 0f);
            lines.Box(C(PC.Yellow), new Vector3(r.xMin, y, r.center.y), new Vector3(0.12f, 0.01f, r.height), 0f);
            lines.Box(C(PC.Yellow), new Vector3(r.xMax, y, r.center.y), new Vector3(0.12f, 0.01f, r.height), 0f);
        }

        Outline(new Rect(19.25f, 16.75f, 10.5f, 13.5f), 0.031f);
        var lineMesh = ArtAssets.SaveMesh("Ground", "FloorLines", lines.ToPaletteMesh("FloorLines"));
        ArtAssets.MeshObject("FloorLines", decals, lineMesh, new[] { ArtPalette.Material }, default, default, false);

        // ---------- ambient life ----------
        var ambient = Reset(env, "ArtAmbient");
        void Traffic(string prop, float z, float fromX, float toX, float speed, float delay)
        {
            var truck = Place(prop, ambient, new Vector3(fromX, 0f, z), toX < fromX ? -90f : 90f);
            if (truck == null) return;
            var route = Reset(ambient, prop + "_Route_" + z);
            var a = new GameObject("A").transform;
            a.SetParent(route, false);
            a.position = new Vector3(fromX, 0f, z);
            var b = new GameObject("B").transform;
            b.SetParent(route, false);
            b.position = new Vector3(toX, 0f, z);
            var path = truck.AddComponent<AmbientPath>();
            ArtAssets.Set(path, ("speed", speed), ("loop", false), ("respawnDelay", delay), ("wheelRadius", 0.5f));
            ArtAssets.SetArray(path, "points", new Object[] { a, b });
            ArtAssets.SetArray(path, "wheels", truck.GetComponentsInChildren<Transform>().Where(t => t.name == "Wheel").Cast<Object>().ToArray());
        }

        // Lanes: customers walk along the south kerb, traffic uses the middle, the Truck Dock's truck parks on the north shoulder (M7).
        Traffic("Prop_RoadTruck_Red", 6.5f, 140f, -40f, 9f, 9f);
        Traffic("Prop_RoadTruck_Blue", 4.0f, -40f, 140f, 8f, 14f);
        for (int i = 0; i < 3; i++)
        {
            var pivot = new GameObject("BirdPivot" + i).transform;
            pivot.SetParent(ambient, false);
            pivot.position = new Vector3(22f + i * 14f, 11f + i * 1.5f, 24f + i * 6f);
            var spin = pivot.gameObject.AddComponent<AmbientMotion>();
            ArtAssets.Set(spin, ("mode", (int)AmbientMotion.Mode.Spin), ("axis", Vector3.up), ("amount", 18f + i * 6f));
            var bird = Place("Prop_Bird", pivot, pivot.position + new Vector3(9f + i * 2f, 0f, 0f), 0f, 1.15f);
            if (bird != null) bird.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        }

        var dust = Reset(env, "ArtDustMotes");
        BuildDustMotes(dust);

        // ---------- camera background (sky) ----------
        var cam = Root(scene, "_Camera").GetComponentInChildren<Camera>();
        if (cam != null) cam.backgroundColor = new Color(0.56f, 0.78f, 0.97f);

        // ---------- NavMesh over Areas 1-3 (expansion content active while baking) ----------
        var surface = env.Find("NavMesh").GetComponent<NavMeshSurface>();
        var toggled = new List<GameObject>();
        foreach (var content in new[] { "RecyclingPlant/Content", "FurnaceHall/Content", "HeavyYard/Content", "BackLot/Content", "TruckDock/Content" })
        {
            var t = gameplay.Find(content);
            if (t == null) continue;
            foreach (Transform c in t)
                if (!c.gameObject.activeSelf)
                {
                    c.gameObject.SetActive(true);
                    toggled.Add(c.gameObject);
                }
        }

        // Ambient movers and decals must not carve the NavMesh.
        foreach (var t in new[] { ambient, decals, backdrop, dust })
        {
            if (!t.TryGetComponent(out NavMeshModifier mod)) mod = t.gameObject.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;
        }

        // Mounds and big props must not swallow loose pieces (see DropBlocker): same sweep as M8_Build.AddDropBlockers.
        foreach (var blockerRoot in new[] { env.Find("JunkPiles"), env.Find("ArtDecor"), gameplay.Find("RecyclingPlant/Content"), gameplay.Find("FurnaceHall/Content"),
                     gameplay.Find("HeavyYard/Content/Decor"), gameplay.Find("BackLot/Content") })
        {
            if (blockerRoot == null) continue;
            foreach (var box in blockerRoot.GetComponentsInChildren<BoxCollider>(true))
            {
                var go = box.gameObject;
                if (box.isTrigger || !(go.name.StartsWith("Heap") || go.name.StartsWith("Prop_"))) continue;
                Vector3 size = Vector3.Scale(box.size, go.transform.lossyScale);
                if (Mathf.Min(size.x, size.z) < 0.9f || Mathf.Max(size.x, size.z) < 1.8f) continue;
                if (!go.TryGetComponent(out ScrapYardKing.Harvest.DropBlocker _)) go.AddComponent<ScrapYardKing.Harvest.DropBlocker>();
            }
        }

        var fl = gameplay.Find("RecyclingPlant/Content/ArtDecor");
        // Belts are lower than the agent step height: without this the bake lays walkable floor on top of them.
        foreach (var belt in Object.FindObjectsByType<ScrapYardKing.Factory.Conveyor>(FindObjectsInactive.Include))
        {
            if (!belt.TryGetComponent(out NavMeshModifier beltModifier)) beltModifier = belt.gameObject.AddComponent<NavMeshModifier>();
            beltModifier.overrideArea = true;
            beltModifier.area = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
        }

        surface.buildHeightMesh = true;
        surface.BuildNavMesh();
        foreach (var go in toggled) go.SetActive(false);
        // Expansion content stays hidden in the saved scene (Expansion also hides it on Awake).
        foreach (var content in new[] { "RecyclingPlant/Content", "FurnaceHall/Content", "HeavyYard/Content", "BackLot/Content", "TruckDock/Content" })
        {
            var t = gameplay.Find(content);
            if (t == null) continue;
            foreach (Transform c in t) c.gameObject.SetActive(false);
        }
        gameplay.Find("RecyclingPlant/Content/FurnacePlot")?.gameObject.SetActive(false);
        gameplay.Find("RecyclingPlant/Content/Tile_FurnaceHall")?.gameObject.SetActive(false);
        string navPath = Path.Combine(Path.GetDirectoryName(ScenePath), Path.GetFileNameWithoutExtension(ScenePath)).Replace('\\', '/') + "/NavMesh-Area1.asset";
        if (File.Exists(navPath)) AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        EditorUtility.SetDirty(surface);
        _ = fl;
        _ = systems;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene saved");
        return Log.ToString();
    }

    static void BuildDustMotes(Transform parent)
    {
        var go = new GameObject("Motes");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(30f, 2.5f, 25f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor = new Color(1f, 0.96f, 0.85f, 0.5f);
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var e = ps.emission;
        e.rateOverTime = 12f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(70f, 3f, 40f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.2f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_FX_Dust.mat");
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // =====================================================================================
    // Lighting and post
    // =====================================================================================

    public static string Lighting()
    {
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var lighting = Root(scene, "_Lighting");
        var sun = lighting.GetComponentInChildren<Light>();
        if (sun != null)
        {
            sun.color = new Color(1f, 0.93f, 0.8f);
            sun.intensity = 1.65f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            sun.transform.rotation = Quaternion.Euler(50f, 328f, 0f);
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.66f, 0.78f, 0.96f);
        RenderSettings.ambientEquatorColor = new Color(0.78f, 0.75f, 0.7f);
        RenderSettings.ambientGroundColor = new Color(0.46f, 0.41f, 0.35f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.74f, 0.85f, 0.97f);
        RenderSettings.fogStartDistance = 55f;
        RenderSettings.fogEndDistance = 150f;

        var volume = lighting.GetComponentInChildren<Volume>();
        var profile = volume != null ? volume.sharedProfile : null;
        if (profile != null)
        {
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.active = true;
                bloom.threshold.Override(1.05f);
                bloom.intensity.Override(0.35f);
                bloom.scatter.Override(0.6f);
            }

            if (profile.TryGet(out Tonemapping tone))
            {
                tone.active = true;
                tone.mode.Override(TonemappingMode.Neutral);
            }

            if (profile.TryGet(out ColorAdjustments color))
            {
                color.active = true;
                color.postExposure.Override(0.08f);
                color.contrast.Override(10f);
                color.saturation.Override(14f);
                color.colorFilter.Override(new Color(1f, 0.98f, 0.95f));
            }

            if (profile.TryGet(out Vignette vignette))
            {
                vignette.active = true;
                vignette.intensity.Override(0.16f);
                vignette.smoothness.Override(0.5f);
            }

            if (!profile.TryGet(out ShadowsMidtonesHighlights smh)) smh = profile.Add<ShadowsMidtonesHighlights>();
            smh.active = true;
            smh.shadows.Override(new Vector4(0.92f, 0.96f, 1.08f, 0f));
            smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.95f, 0f));
            EditorUtility.SetDirty(profile);
        }

        // SSAO: soft contact shadows that make the chunky shapes sit on the ground.
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            urp.shadowDistance = 48f;
            urp.shadowCascadeCount = 2;
            EditorUtility.SetDirty(urp);
            foreach (var data in urp.rendererDataList)
            {
                if (data == null) continue;
                foreach (var f in data.rendererFeatures)
                {
                    if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    var so = new SerializedObject(f);
                    var settings = so.FindProperty("m_Settings");
                    settings.FindPropertyRelative("Intensity").floatValue = 0.9f;
                    settings.FindPropertyRelative("Radius").floatValue = 0.4f;
                    settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.3f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return "lighting set";
    }

    public static string All()
    {
        var sb = new StringBuilder();
        sb.Append(Props());
        sb.Append(Scene());
        sb.Append(Lighting());
        return sb.ToString();
    }
}
