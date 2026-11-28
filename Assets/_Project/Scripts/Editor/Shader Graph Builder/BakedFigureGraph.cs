using UnityEditor;
using UnityEditor.Graphing;
using UnityEditor.Rendering.Universal.ShaderGraph;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Drawing;
using UnityEditor.ShaderGraph.Drawing.Controls;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;

/// <summary>
/// Writes the Baked Figure shader graph (Tools > Battle > Build Baked Figure Shader) through Shader Graph's own code,
/// so the file is exactly what this Shader Graph version writes. It's a URP Lit graph like Synty's Generic_Basic
/// (base map × colour, metallic, smoothness, alpha clip, and that colour as emission for the Quirky Series horse) whose vertex stage skins the mesh from a baked pose
/// texture (BakedSkinning.hlsl). Running it again replaces the graph.
/// </summary>
/// <remarks>
/// Shader Graph's graph-building types are internal. This assembly is named after one the Shader Graph and URP
/// editor assemblies both make their internals visible to (the VFX Graph's editor tests, a package this project
/// doesn't have), which is the only reason for its odd name.
/// </remarks>
static class BakedFigureGraph
{
    const string GraphPath = "Assets/_Project/Shaders/Baked Figure.shadergraph";
    const string SkinningPath = "Assets/_Project/Shaders/BakedSkinning.hlsl";

    [MenuItem("Tools/Battle/Build Baked Figure Shader")]
    public static void Build()
    {
        string skinningGuid = AssetDatabase.AssetPathToGUID(SkinningPath);
        if (string.IsNullOrEmpty(skinningGuid))
        {
            Debug.LogError("No " + SkinningPath);
            return;
        }

        var target = new UniversalTarget();
        target.TrySetActiveSubTarget(typeof(UniversalLitSubTarget));
        target.alphaClip = true;
        var blocks = new[]
        {
            BlockFields.VertexDescription.Position,
            BlockFields.VertexDescription.Normal,
            BlockFields.VertexDescription.Tangent,
            BlockFields.SurfaceDescription.BaseColor,
            BlockFields.SurfaceDescription.Metallic,
            BlockFields.SurfaceDescription.Smoothness,
            BlockFields.SurfaceDescription.Alpha,
            BlockFields.SurfaceDescription.AlphaClipThreshold,
            BlockFields.SurfaceDescription.Emission,
        };
        var graph = new GraphData();
        graph.AddContexts();
        graph.InitializeOutputs(new Target[] { target }, blocks);
        graph.AddCategory(CategoryData.DefaultCategory());
        graph.path = "Battle";

        // surface
        var baseMap = Add(graph, new Texture2DShaderProperty { defaultType = Texture2DShaderProperty.DefaultType.White, isMainTexture = true }, "Base Map", "_BaseMap");
        var baseColour = Add(graph, new ColorShaderProperty { value = Color.white, isMainColor = true }, "Base Colour", "_BaseColor");
        var metallic = Add(graph, new Vector1ShaderProperty { value = 0f, floatType = FloatType.Slider, rangeValues = new Vector2(0f, 1f) }, "Metallic", "_Metallic");
        var smoothness = Add(graph, new Vector1ShaderProperty { value = 0.2f, floatType = FloatType.Slider, rangeValues = new Vector2(0f, 1f) }, "Smoothness", "_Smoothness");
        var cutoff = Add(graph, new Vector1ShaderProperty { value = 0.5f, floatType = FloatType.Slider, rangeValues = new Vector2(0f, 1f) }, "Alpha Clip", "_Cutoff");
        // the Quirky Series animals' SoftSurface shader adds its colour (texture × tint) as emission, 0.5 on the horse; 0 for Synty
        var emission = Add(graph, new Vector1ShaderProperty { value = 0f, floatType = FloatType.Slider, rangeValues = new Vector2(0f, 1f) }, "Emission", "_Emission");
        // the baked poses and this figure's place in them, set every frame by BakedFigureAnimation
        var poses = Add(graph, new Texture2DShaderProperty { defaultType = Texture2DShaderProperty.DefaultType.Black }, "Poses", "_Poses");
        var animation = new AbstractShaderProperty[]
        {
            Add(graph, new Vector4ShaderProperty { value = Vector4.zero }, "Loco", "_Loco"),
            Add(graph, new Vector4ShaderProperty { value = Vector4.zero }, "Act1", "_Act1"),
            Add(graph, new Vector4ShaderProperty { value = Vector4.zero }, "Act2", "_Act2"),
            Add(graph, new Vector4ShaderProperty { value = new Vector4(0f, 0f, 0f, 1f) }, "Re1Q", "_Re1Q"),
            Add(graph, new Vector4ShaderProperty { value = Vector4.zero }, "Re1T", "_Re1T"),
            Add(graph, new Vector4ShaderProperty { value = new Vector4(0f, 0f, 0f, 1f) }, "Re2Q", "_Re2Q"),
            Add(graph, new Vector4ShaderProperty { value = Vector4.zero }, "Re2T", "_Re2T"),
        };

        // fragment: base map × colour, its alpha clipped
        var sample = Node(graph, new SampleTexture2DNode(), -700, 200);
        Connect(graph, PropertyNode(graph, baseMap, -950, 200), UnityEditor.ShaderGraph.PropertyNode.OutputSlotId, sample, SampleTexture2DNode.TextureInputId);
        var tint = Node(graph, new MultiplyNode(), -400, 200);
        Connect(graph, sample, SampleTexture2DNode.OutputSlotRGBAId, tint, 0);
        Connect(graph, PropertyNode(graph, baseColour, -700, 450), UnityEditor.ShaderGraph.PropertyNode.OutputSlotId, tint, 1);
        Connect(graph, tint, 2, Block(graph, BlockFields.SurfaceDescription.BaseColor), 0);
        Connect(graph, PropertyNode(graph, metallic, -400, 500), UnityEditor.ShaderGraph.PropertyNode.OutputSlotId, Block(graph, BlockFields.SurfaceDescription.Metallic), 0);
        Connect(graph, PropertyNode(graph, smoothness, -400, 560), UnityEditor.ShaderGraph.PropertyNode.OutputSlotId, Block(graph, BlockFields.SurfaceDescription.Smoothness), 0);
        Connect(graph, sample, SampleTexture2DNode.OutputSlotAId, Block(graph, BlockFields.SurfaceDescription.Alpha), 0);
        Connect(graph, PropertyNode(graph, cutoff, -400, 620), UnityEditor.ShaderGraph.PropertyNode.OutputSlotId, Block(graph, BlockFields.SurfaceDescription.AlphaClipThreshold), 0);
        var glow = Node(graph, new MultiplyNode(), -400, 700);
        Connect(graph, tint, 2, glow, 0);
        Connect(graph, PropertyNode(graph, emission, -700, 750), UnityEditor.ShaderGraph.PropertyNode.OutputSlotId, glow, 1);
        Connect(graph, glow, 2, Block(graph, BlockFields.SurfaceDescription.Emission), 0);

        // vertex: skinned from the poses
        var skin = Node(graph, new CustomFunctionNode(), -400, -300);
        skin.sourceType = HlslSourceType.File;
        skin.functionName = "BakedSkin";
        skin.functionSource = skinningGuid;
        const ShaderStageCapability vertex = ShaderStageCapability.Vertex;
        skin.AddSlot(new Vector3MaterialSlot(0, "PositionOS", "PositionOS", SlotType.Input, Vector3.zero, vertex));
        skin.AddSlot(new Vector3MaterialSlot(1, "NormalOS", "NormalOS", SlotType.Input, Vector3.zero, vertex));
        skin.AddSlot(new Vector4MaterialSlot(2, "Bones", "Bones", SlotType.Input, Vector4.zero, vertex));
        skin.AddSlot(new Texture2DInputMaterialSlot(3, "Poses", "Poses", vertex));
        string[] names = { "Loco", "Act1", "Act2", "Re1Q", "Re1T", "Re2Q", "Re2T" };
        for (int i = 0; i < names.Length; i++)
            skin.AddSlot(new Vector4MaterialSlot(4 + i, names[i], names[i], SlotType.Input, Vector4.zero, vertex));
        skin.AddSlot(new Vector3MaterialSlot(20, "Position", "Position", SlotType.Output, Vector3.zero, vertex));
        skin.AddSlot(new Vector3MaterialSlot(21, "Normal", "Normal", SlotType.Output, Vector3.zero, vertex));

        var position = Node(graph, new PositionNode(), -800, -400);
        position.spacePopup = new UnityEditor.ShaderGraph.Drawing.Controls.PopupList(new[] { "Object", "View", "World", "Tangent", "Absolute World" }, (int)CoordinateSpace.Object);
        var normal = Node(graph, new NormalVectorNode(), -800, -300);
        normal.spacePopup = new UnityEditor.ShaderGraph.Drawing.Controls.PopupList(new[] { "Object", "View", "World", "Tangent" }, (int)CoordinateSpace.Object);
        var bones = Node(graph, new UVNode(), -800, -200);
        bones.uvChannel = UVChannel.UV3;
        Connect(graph, position, 0, skin, 0);
        Connect(graph, normal, NormalVectorNode.kOutputSlotId, skin, 1);
        Connect(graph, bones, UVNode.OutputSlotId, skin, 2);
        Connect(graph, PropertyNode(graph, poses, -800, -100), UnityEditor.ShaderGraph.PropertyNode.OutputSlotId, skin, 3);
        for (int i = 0; i < animation.Length; i++)
            Connect(graph, PropertyNode(graph, animation[i], -800, -40 + i * 40), UnityEditor.ShaderGraph.PropertyNode.OutputSlotId, skin, 4 + i);
        Connect(graph, skin, 20, Block(graph, BlockFields.VertexDescription.Position), 0);
        Connect(graph, skin, 21, Block(graph, BlockFields.VertexDescription.Normal), 0);

        graph.ValidateGraph();
        FileUtilities.WriteShaderGraphToDisk(GraphPath, graph);
        AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("[Bake] wrote " + GraphPath);
    }

    static T Add<T>(GraphData graph, T property, string displayName, string referenceName) where T : AbstractShaderProperty
    {
        graph.AddGraphInput(property);
        property.SetDisplayNameAndSanitizeForGraph(graph, displayName);
        property.SetReferenceNameAndSanitizeForGraph(graph, referenceName);
        return property;
    }

    static T Node<T>(GraphData graph, T node, float x, float y) where T : AbstractMaterialNode
    {
        node.drawState = new DrawState { position = new Rect(x, y, 0f, 0f), expanded = true };
        graph.AddNode(node);
        return node;
    }

    static UnityEditor.ShaderGraph.PropertyNode PropertyNode(GraphData graph, AbstractShaderProperty property, float x, float y)
    {
        var node = Node(graph, new UnityEditor.ShaderGraph.PropertyNode(), x, y);
        node.property = property;
        return node;
    }

    static BlockNode Block(GraphData graph, BlockFieldDescriptor descriptor)
    {
        foreach (var block in graph.GetNodes<BlockNode>())
            if (block.descriptor == descriptor)
                return block;
        throw new System.InvalidOperationException("No block " + descriptor.name);
    }

    static void Connect(GraphData graph, AbstractMaterialNode from, int fromSlot, AbstractMaterialNode to, int toSlot)
    {
        var edge = graph.Connect(new SlotReference(from, fromSlot), new SlotReference(to, toSlot));
        if (edge == null)
            Debug.LogError($"[Bake] couldn't connect {from.name}.{fromSlot} to {to.name}.{toSlot}");
    }
}
