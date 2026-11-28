using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>
/// Bakes every animated unit type's and mount's clips for the GPU (Tools > Battle > Bake Figures): a
/// <see cref="BakedFigure"/> per type, holding a mesh with the body and its props merged and a texture of every
/// bone's pose in every frame (see <see cref="BakedFigure"/> for the layout). Battles then skin figures with the
/// Baked Figure shader instead of SkinnedMeshRenderers, which WebGL skins on the CPU every frame.
/// </summary>
/// <remarks>
/// <para>
/// Clips are sampled the way <see cref="FigureAnimation"/> plays them: in place (no root motion), through a
/// PlayableGraph on the prefab's own Animator, with foot IK on except for held clips (deaths) and for riders. Riders
/// and soldiers on foot share some clips but play them with different IK, so every unit type has its own bake.
/// </para>
/// <para>
/// The prefab is instantiated hidden in a preview scene and destroyed afterwards, so a bake can never leave
/// figures in a saved scene. The results go to Assets/_Project/ThirdParty/Baked (git-ignored: they're made from
/// Synty, Kevin Iglesias and Quirky Series assets). Re-baking keeps each asset's GUID.
/// </para>
/// </remarks>
public static class FigureBaker
{
    const string Folder = "Assets/_Project/ThirdParty/Baked";
    const string ShaderPath = "Assets/_Project/Shaders/Baked Figure.shadergraph";
    const string MaterialPath = "Assets/_Project/Materials/Baked Figure.mat";
    const float LoopFps = 30f;
    // actions are often sped up to fit between blows, so they get more frames
    static readonly float[] ActionFpsTries = { 60f, 45f, 30f };
    // WebGL2's guaranteed texture size
    const int MaxRows = 2048;

    struct ClipSpec
    {
        public AnimationClip clip;
        public bool footIK;
        public bool action;
    }

    struct PropSpec
    {
        public GameObject prefab;
        public string bone;
        public Vector3 position;
        public Vector3 rotation;
    }

    [MenuItem("Tools/Battle/Bake Figures")]
    public static void BakeMenu() => Debug.Log(BakeAll());

    /// <summary>Bakes every unit type and mount and returns a report (also callable from an eval).</summary>
    public static string BakeAll()
    {
        var report = new StringBuilder();
        var material = TemplateMaterial(report);
        if (material == null)
            return report.ToString();
        EnsureFolder(Folder);

        // where BattleView hangs each type's weapon, from the open scene's view (the same in both battle scenes)
        string weaponBone = "Hand_R";
        Vector3 weaponPosition = Vector3.zero, weaponRotation = Vector3.zero;
        var view = Object.FindAnyObjectByType<BattleView>(FindObjectsInactive.Include);
        if (view != null)
        {
            var so = new SerializedObject(view);
            weaponBone = so.FindProperty("weaponBone").stringValue;
            weaponPosition = so.FindProperty("weaponPosition").vector3Value;
            weaponRotation = so.FindProperty("weaponRotation").vector3Value;
        }
        else
            report.AppendLine("No BattleView open: weapons hang on Hand_R with no offset.");

        var mounts = new HashSet<MountType>();
        foreach (var guid in AssetDatabase.FindAssets("t:UnitType"))
        {
            var type = AssetDatabase.LoadAssetAtPath<UnitType>(AssetDatabase.GUIDToAssetPath(guid));
            if (type == null || !type.Animated || type.visualPrefab == null)
                continue;
            bool riding = type.mount != null;
            var clips = new List<ClipSpec>();
            void Add(AnimationClip clip, bool ik, bool action)
            {
                if (clip != null && clips.All(c => c.clip != clip))
                    clips.Add(new ClipSpec { clip = clip, footIK = ik, action = action });
            }
            // as FigureAnimation plays them: riders never use foot IK, held deaths don't either
            Add(type.idleClip, !riding, false);
            Add(type.walkClip, !riding, false);
            Add(type.runClip, !riding, false);
            Add(type.shootClip, !riding, true);
            foreach (var clip in type.attackClips) Add(clip, !riding, true);
            foreach (var clip in type.hitClips) Add(clip, !riding, true);
            foreach (var clip in type.knockbackClips) Add(clip, !riding, true);
            foreach (var clip in type.deathClips) Add(clip, false, true);

            var props = new List<PropSpec>();
            if (type.weaponPrefab != null)
                props.Add(new PropSpec { prefab = type.weaponPrefab, bone = weaponBone, position = weaponPosition, rotation = weaponRotation });
            foreach (var attachment in type.attachments)
                if (attachment.prefab != null)
                    props.Add(new PropSpec { prefab = attachment.prefab, bone = attachment.bone, position = attachment.position, rotation = attachment.rotation });

            var baked = Bake(type.name, type.visualPrefab, clips, props, null, null, material, report);
            Assign(type, baked);
            if (type.mount != null)
                mounts.Add(type.mount);
        }

        foreach (var mount in mounts)
        {
            if (!mount.Animated || mount.prefab == null)
                continue;
            var clips = new List<ClipSpec>();
            foreach (var (clip, action) in new[] { (mount.idleClip, false), (mount.walkClip, false), (mount.runClip, false),
                         (mount.hitClip, true), (mount.chargeClip, true), (mount.deathClip, true) })
                if (clip != null && clips.All(c => c.clip != clip))
                    clips.Add(new ClipSpec { clip = clip, footIK = false, action = action });
            var baked = Bake(mount.name, mount.prefab, clips, new List<PropSpec>(), mount.seatBone, mount.deadFace, material, report);
            Assign(mount, baked);
        }
        AssetDatabase.SaveAssets();
        return report.ToString();
    }

    static Material TemplateMaterial(StringBuilder report)
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            report.AppendLine("No " + ShaderPath + ": run Tools > Battle > Build Baked Figure Shader first.");
            return null;
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            EnsureFolder(System.IO.Path.GetDirectoryName(MaterialPath).Replace('\\', '/'));
            material = new Material(shader) { name = "Baked Figure" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else if (material.shader != shader)
            material.shader = shader;
        return material;
    }

    static void Assign(Object owner, BakedFigure baked)
    {
        if (baked == null)
            return;
        var so = new SerializedObject(owner);
        so.FindProperty("baked").objectReferenceValue = baked;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static BakedFigure Bake(string name, GameObject prefab, List<ClipSpec> clips, List<PropSpec> props, string seatBone,
        string deadFace, Material material, StringBuilder report)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var made = new List<GameObject>();
        // a PlayableGraph's Evaluate doesn't pose a humanoid in Edit mode; AnimationMode's sampling does
        bool animationMode = AnimationMode.InAnimationMode();
        if (!animationMode)
            AnimationMode.StartAnimationMode();
        try
        {
            var instance = Spawn(prefab, scene, made);
            var root = instance.transform;
            var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.gameObject.activeInHierarchy && s.enabled && s.sharedMesh != null).ToArray();
            var skin = skins.FirstOrDefault();
            if (skins.Length > 1)
                report.AppendLine($"{name}: {skins.Length} skinned meshes showing; only {skin.name} is baked.");
            var animator = instance.GetComponentInChildren<Animator>(true);
            if (skin == null || animator == null)
            {
                report.AppendLine($"{name}: no active SkinnedMeshRenderer or Animator, skipped.");
                return null;
            }
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.enabled = true;
            animator.Rebind();

            var bones = skin.bones;
            int boneCount = bones.Length;
            var sourceMesh = skin.sharedMesh;
            // Everything is measured from the prefab's rest pose, where Unity draws the mesh before any animation:
            // bones in root space, and each vertex where its bones put it (bone × bindpose × vertex). The renderer's
            // own transform isn't used, because the zebra's bindposes were made with its mesh turned 90 degrees
            // from where its renderer sits now.
            var bind = new Matrix4x4[boneCount];
            var bindAt = new Vector3[boneCount];
            var bindTurn = new Quaternion[boneCount];
            var bindScale = new Vector3[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                bind[i] = root.worldToLocalMatrix * bones[i].localToWorldMatrix;
                Decompose(bind[i], out bindAt[i], out bindTurn[i], out bindScale[i]);
            }
            var upper = UpperFlags(animator, bones);
            int hips = animator.isHuman ? System.Array.IndexOf(bones, animator.GetBoneTransform(HumanBodyBones.Hips)) : -1;
            int seat = string.IsNullOrEmpty(seatBone) ? -1 : System.Array.FindIndex(bones, b => b != null && b.name == seatBone);
            if (!string.IsNullOrEmpty(seatBone) && seat < 0)
                report.AppendLine($"{name}: no skin bone called {seatBone} for the seat.");

            // the mesh first, at the prefab's rest pose (props hang off bones by their offsets from them)
            var mesh = BuildMesh(name, root, skin, bones, bind, props, scene, made, null, report);
            Mesh deadMesh = null;
            if (!string.IsNullOrEmpty(deadFace) && sourceMesh.GetBlendShapeIndex(deadFace) >= 0)
                deadMesh = BuildMesh(name + " Dead", root, skin, bones, bind, props, scene, made, deadFace, report);

            // then every clip's frames
            float actionFps = ActionFpsTries[0];
            List<(Vector4[] turns, Vector4[] places)> rows = null;
            BakedFigure.Clip[] baked = null;
            int nonUniform = 0;
            foreach (float fps in ActionFpsTries)
            {
                actionFps = fps;
                rows = new List<(Vector4[], Vector4[])>();
                baked = new BakedFigure.Clip[clips.Count];
                nonUniform = 0;
                for (int c = 0; c < clips.Count; c++)
                    baked[c] = SampleClip(clips[c], clips[c].action ? fps : LoopFps, animator, root, bones, bindTurn, bindScale, rows, ref nonUniform);
                if (1 + rows.Count <= MaxRows)
                    break;
            }
            if (1 + rows.Count > MaxRows)
                report.AppendLine($"{name}: {1 + rows.Count} rows is over {MaxRows}, too tall for some WebGL2 devices.");

            var texture = BuildTexture(name, boneCount, bindAt, upper, rows);
            var asset = Save(name, mesh, deadMesh, texture);
            asset.material = material;
            asset.boneCount = boneCount;
            asset.boneNames = bones.Select(b => b != null ? b.name : "").ToArray();
            asset.clips = baked;
            asset.hipsBone = hips;
            asset.seatBone = seat;
            asset.hipsPoses = hips >= 0 ? BoneRows(rows, hips) : new float[0];
            asset.seatPoses = seat >= 0 ? BoneRows(rows, seat) : new float[0];
            asset.ResetLookup();
            EditorUtility.SetDirty(asset);

            string check = Check(baked, rows, animator, root, skin, mesh, bindAt, clips);
            report.AppendLine($"{name}: {clips.Count} clips, {1 + rows.Count} rows x {boneCount} bones, actions at {actionFps} fps, " +
                              $"{mesh.vertexCount} vertices{(deadMesh != null ? ", dead mesh" : "")}, hips {hips}, seat {seat}" +
                              $"{(nonUniform > 0 ? $", {nonUniform} non-uniform bone scales (shown uniform)" : "")}. {check}");
            return asset;
        }
        finally
        {
            if (!animationMode)
                AnimationMode.StopAnimationMode();
            foreach (var go in made)
                if (go != null)
                    Object.DestroyImmediate(go);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void Pose(PlayableGraph graph, double time)
    {
        AnimationMode.BeginSampling();
        AnimationMode.SamplePlayableGraph(graph, 0, (float)time);
        AnimationMode.EndSampling();
    }

    static GameObject Spawn(GameObject prefab, Scene scene, List<GameObject> made)
    {
        // straight into the preview scene, so the open scene is never touched
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.hideFlags = HideFlags.HideAndDontSave;
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        made.Add(instance);
        return instance;
    }

    /// <summary>1 for bones that belong to the upper body (spine up, head, arms, fingers), by their nearest humanoid bone.</summary>
    static float[] UpperFlags(Animator animator, Transform[] bones)
    {
        var flags = new float[bones.Length];
        if (!animator.isHuman)
            return flags;
        var human = new Dictionary<Transform, bool>();
        for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
        {
            var t = animator.GetBoneTransform(bone);
            if (t != null)
                human[t] = IsUpper(bone);
        }
        for (int i = 0; i < bones.Length; i++)
            for (var t = bones[i]; t != null; t = t.parent)
                if (human.TryGetValue(t, out bool up))
                {
                    flags[i] = up ? 1f : 0f;
                    break;
                }
        return flags;
    }

    static bool IsUpper(HumanBodyBones bone) => bone switch
    {
        HumanBodyBones.Hips or HumanBodyBones.LeftUpperLeg or HumanBodyBones.RightUpperLeg or HumanBodyBones.LeftLowerLeg
            or HumanBodyBones.RightLowerLeg or HumanBodyBones.LeftFoot or HumanBodyBones.RightFoot or HumanBodyBones.LeftToes
            or HumanBodyBones.RightToes => false,
        _ => true,
    };

    static BakedFigure.Clip SampleClip(ClipSpec spec, float fps, Animator animator, Transform root, Transform[] bones,
        Quaternion[] bindTurn, Vector3[] bindScale, List<(Vector4[] turns, Vector4[] places)> rows, ref int nonUniform)
    {
        var clip = spec.clip;
        float length = Mathf.Max(1e-3f, clip.length);
        bool loop = clip.isLooping;
        int frames = loop ? Mathf.Max(1, Mathf.RoundToInt(length * fps)) : Mathf.Max(2, Mathf.CeilToInt(length * fps) + 1);
        var result = new BakedFigure.Clip { clip = clip, start = 1 + rows.Count, frames = frames, length = length, loop = loop };

        animator.Rebind();
        var graph = PlayableGraph.Create("Figure bake");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Figure", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(spec.footIK);
            output.SetSourcePlayable(playable);
            Vector4[] previous = null;
            int first = rows.Count;
            for (int f = 0; f < frames; f++)
            {
                double time = loop ? length * f / frames : length * f / (frames - 1);
                playable.SetTime(time);
                playable.SetTime(time);
                Pose(graph, time);
                var turns = new Vector4[bones.Length];
                var places = new Vector4[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    Decompose(root.worldToLocalMatrix * bones[i].localToWorldMatrix, out var at, out var turn, out var scale);
                    var q = turn * Quaternion.Inverse(bindTurn[i]);
                    var v = new Vector4(q.x, q.y, q.z, q.w);
                    // keep each bone's quaternions on one side, so blending between frames takes the short way
                    if (previous != null && Vector4.Dot(previous[i], v) < 0f)
                        v = -v;
                    turns[i] = v;
                    var ratio = new Vector3(scale.x / bindScale[i].x, scale.y / bindScale[i].y, scale.z / bindScale[i].z);
                    if (Mathf.Abs(ratio.x - ratio.y) > 0.02f || Mathf.Abs(ratio.x - ratio.z) > 0.02f)
                        nonUniform++;
                    places[i] = new Vector4(at.x, at.y, at.z, (ratio.x + ratio.y + ratio.z) / 3f);
                }
                rows.Add((turns, places));
                previous = turns;
            }
            if (loop)
            {
                // the first frame again, on the same side as the last, so filtering wraps round
                var (turns, places) = rows[first];
                var again = (Vector4[])turns.Clone();
                for (int i = 0; i < again.Length; i++)
                    if (Vector4.Dot(previous[i], again[i]) < 0f)
                        again[i] = -again[i];
                rows.Add((again, places));
            }
        }
        finally
        {
            graph.Destroy();
        }
        return result;
    }

    static Mesh BuildMesh(string name, Transform root, SkinnedMeshRenderer skin, Transform[] bones, Matrix4x4[] bind,
        List<PropSpec> props, Scene scene, List<GameObject> made, string deadFace, StringBuilder report)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<Vector4>();
        var triangles = new List<int>();
        var source = skin.sharedMesh;
        // where each bone puts a vertex at rest, as Unity skins it: bone × bindpose (called before any sampling)
        var bindposes = source.bindposes;
        var meshToRoot = new Matrix4x4[bones.Length];
        for (int k = 0; k < bones.Length; k++)
            meshToRoot[k] = root.worldToLocalMatrix * bones[k].localToWorldMatrix * bindposes[k];

        // the body, its blend shapes at their weights (and the dead face full on, for the dead copy)
        var v = source.vertices;
        var n = source.normals;
        var deltaV = new Vector3[v.Length];
        var deltaN = new Vector3[v.Length];
        var tempV = new Vector3[v.Length];
        var tempN = new Vector3[v.Length];
        var tempT = new Vector3[v.Length];
        for (int s = 0; s < source.blendShapeCount; s++)
        {
            float weight = source.GetBlendShapeName(s) == deadFace ? 100f : skin.GetBlendShapeWeight(s);
            if (weight <= 0f)
                continue;
            int frame = source.GetBlendShapeFrameCount(s) - 1;
            source.GetBlendShapeFrameVertices(s, frame, tempV, tempN, tempT);
            float share = weight / Mathf.Max(1e-3f, source.GetBlendShapeFrameWeight(s, frame));
            for (int i = 0; i < v.Length; i++)
            {
                deltaV[i] += tempV[i] * share;
                deltaN[i] += tempN[i] * share;
            }
        }
        var bodyUv = new List<Vector2>();
        source.GetUVs(0, bodyUv);
        var perVertex = source.GetBonesPerVertex();
        var boneWeights = source.GetAllBoneWeights();
        int w = 0;
        for (int i = 0; i < v.Length; i++)
        {
            int count = perVertex[i];
            var shaped = v[i] + deltaV[i];
            var shapedNormal = n.Length > i ? n[i] + deltaN[i] : Vector3.up;
            Vector3 at = Vector3.zero, facing = Vector3.zero;
            for (int j = 0; j < count; j++)
            {
                var bw = boneWeights[w + j];
                at += meshToRoot[bw.boneIndex].MultiplyPoint3x4(shaped) * bw.weight;
                facing += meshToRoot[bw.boneIndex].MultiplyVector(shapedNormal) * bw.weight;
            }
            vertices.Add(at);
            normals.Add(facing.normalized);
            uvs.Add(bodyUv.Count > i ? bodyUv[i] : Vector2.zero);
            // Unity keeps each vertex's weights strongest first; the two strongest, renormalised, as Mobile skins
            var a = count > 0 ? boneWeights[w] : default;
            var b = count > 1 ? boneWeights[w + 1] : default;
            float sum = Mathf.Max(1e-6f, a.weight + b.weight);
            weights.Add(new Vector4(a.boneIndex, count > 1 ? b.boneIndex : a.boneIndex, a.weight / sum, count > 1 ? b.weight / sum : 0f));
            w += count;
        }
        for (int sub = 0; sub < source.subMeshCount; sub++)
            triangles.AddRange(source.GetTriangles(sub));

        // rigid parts hanging off bones: the prefab's own (armour on the head) and the props BattleView attaches
        var bodyMaterial = skin.sharedMaterial;
        var boneSet = new Dictionary<Transform, int>();
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null)
                boneSet[bones[i]] = i;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(false))
        {
            if (LowerDetail(filter))
                continue;
            int bone = NearestBone(filter.transform, boneSet);
            if (bone < 0 || filter.sharedMesh == null)
                continue;
            var offset = bones[bone].worldToLocalMatrix * filter.transform.localToWorldMatrix;
            AddRigid(filter, bind[bone] * offset, bone, bodyMaterial, vertices, normals, uvs, weights, triangles, name, report);
        }
        foreach (var prop in props)
        {
            var holder = FindDeep(root, prop.bone);
            int bone = holder != null ? NearestBone(holder, boneSet) : -1;
            if (bone < 0)
            {
                report.AppendLine($"{name}: no bone {prop.bone} for {prop.prefab.name}.");
                continue;
            }
            var instance = Spawn(prop.prefab, scene, made);
            var propRoot = instance.transform;
            var hang = Matrix4x4.TRS(prop.position, Quaternion.Euler(prop.rotation), prop.prefab.transform.localScale);
            // the holder may be a non-skin bone under the skin bone found
            var fromBone = bones[bone].worldToLocalMatrix * holder.localToWorldMatrix;
            foreach (var filter in propRoot.GetComponentsInChildren<MeshFilter>(false))
            {
                if (filter.sharedMesh == null || LowerDetail(filter))
                    continue;
                var local = propRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                AddRigid(filter, bind[bone] * fromBone * hang * local, bone, bodyMaterial, vertices, normals, uvs, weights, triangles, name, report);
            }
        }

        var mesh = new Mesh { name = name };
        mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(3, weights);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        // room for a body lying down or a spear held high, so culling and shadows never clip a pose
        var bounds = mesh.bounds;
        float reach = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 2f;
        mesh.bounds = new Bounds(new Vector3(0f, bounds.center.y, 0f), Vector3.one * reach);
        mesh.UploadMeshData(false);
        return mesh;
    }

    static void AddRigid(MeshFilter filter, Matrix4x4 toRoot, int bone, Material bodyMaterial, List<Vector3> vertices,
        List<Vector3> normals, List<Vector2> uvs, List<Vector4> weights, List<int> triangles, string name, StringBuilder report)
    {
        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null || !renderer.enabled)
            return;
        if (renderer.sharedMaterial != bodyMaterial)
            report.AppendLine($"{name}: {filter.name} uses {(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "no material")}, " +
                              $"not the body's {bodyMaterial.name}; merged anyway, drawn with the body's.");
        var mesh = filter.sharedMesh;
        int start = vertices.Count;
        var v = mesh.vertices;
        var n = mesh.normals;
        var uv = new List<Vector2>();
        mesh.GetUVs(0, uv);
        for (int i = 0; i < v.Length; i++)
        {
            vertices.Add(toRoot.MultiplyPoint3x4(v[i]));
            normals.Add(toRoot.MultiplyVector(n.Length > i ? n[i] : Vector3.up).normalized);
            uvs.Add(uv.Count > i ? uv[i] : Vector2.zero);
            weights.Add(new Vector4(bone, bone, 1f, 0f));
        }
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
            foreach (int index in mesh.GetTriangles(sub))
                triangles.Add(start + index);
    }

    /// <summary>A renderer in a LOD group's lower levels: merging every level would stack them on top of each other.</summary>
    static bool LowerDetail(MeshFilter filter)
    {
        var group = filter.GetComponentInParent<LODGroup>();
        if (group == null)
            return false;
        var renderer = filter.GetComponent<Renderer>();
        var lods = group.GetLODs();
        for (int level = 1; level < lods.Length; level++)
            if (System.Array.IndexOf(lods[level].renderers, renderer) >= 0)
                return true;
        return false;
    }

    static int NearestBone(Transform t, Dictionary<Transform, int> bones)
    {
        for (; t != null; t = t.parent)
            if (bones.TryGetValue(t, out int index))
                return index;
        return -1;
    }

    static Transform FindDeep(Transform parent, string childName)
    {
        if (parent.name == childName)
            return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindDeep(parent.GetChild(i), childName);
            if (found != null)
                return found;
        }
        return null;
    }

    static void Decompose(Matrix4x4 m, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        position = m.GetColumn(3);
        Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
        scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
        rotation = Quaternion.LookRotation(z / Mathf.Max(1e-8f, scale.z), y / Mathf.Max(1e-8f, scale.y));
    }

    static Texture2D BuildTexture(string name, int boneCount, Vector3[] pivots, float[] upper, List<(Vector4[] turns, Vector4[] places)> rows)
    {
        int width = boneCount * BakedFigure.TexelsPerBone, height = 1 + rows.Count;
        var data = new ushort[width * height * 4];
        void Put(int x, int y, Vector4 value)
        {
            int i = (y * width + x) * 4;
            data[i] = Mathf.FloatToHalf(value.x);
            data[i + 1] = Mathf.FloatToHalf(value.y);
            data[i + 2] = Mathf.FloatToHalf(value.z);
            data[i + 3] = Mathf.FloatToHalf(value.w);
        }
        for (int b = 0; b < boneCount; b++)
            Put(b * 2, 0, new Vector4(pivots[b].x, pivots[b].y, pivots[b].z, upper[b]));
        for (int r = 0; r < rows.Count; r++)
            for (int b = 0; b < boneCount; b++)
            {
                Put(b * 2, r + 1, rows[r].turns[b]);
                Put(b * 2 + 1, r + 1, rows[r].places[b]);
            }
        var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
        {
            name = name + " Poses",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0,
        };
        texture.SetPixelData(data, 0);
        texture.Apply(false, false);
        return texture;
    }

    static float[] BoneRows(List<(Vector4[] turns, Vector4[] places)> rows, int bone)
    {
        var data = new float[(1 + rows.Count) * BakedFigure.FloatsPerRow];
        // row 0 is the texture's header: the bone at rest
        data[3] = 1f;
        data[7] = 1f;
        for (int r = 0; r < rows.Count; r++)
        {
            int i = (r + 1) * BakedFigure.FloatsPerRow;
            var q = rows[r].turns[bone];
            var p = rows[r].places[bone];
            // through half precision, so the CPU's copy matches what the shader reads
            for (int k = 0; k < 4; k++)
            {
                data[i + k] = Mathf.HalfToFloat(Mathf.FloatToHalf(q[k]));
                data[i + 4 + k] = Mathf.HalfToFloat(Mathf.FloatToHalf(p[k]));
            }
        }
        return data;
    }

    static BakedFigure Save(string name, Mesh mesh, Mesh deadMesh, Texture2D texture)
    {
        string path = $"{Folder}/{name}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<BakedFigure>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<BakedFigure>();
            asset.name = name;
            AssetDatabase.CreateAsset(asset, path);
        }
        else
            foreach (var old in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                AssetDatabase.RemoveObjectFromAsset(old);
                Object.DestroyImmediate(old, true);
            }
        AssetDatabase.AddObjectToAsset(mesh, asset);
        AssetDatabase.AddObjectToAsset(texture, asset);
        if (deadMesh != null)
            AssetDatabase.AddObjectToAsset(deadMesh, asset);
        asset.mesh = mesh;
        asset.deadMesh = deadMesh;
        asset.poses = texture;
        return asset;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    /// <summary>
    /// Compares the baked skinning, worked out on the CPU from the half-precision rows the shader reads, with Unity's
    /// own skinning of the real figure at the same frames. Differences come from half precision, the two bone
    /// weights kept (Unity's here uses up to four) and any bake mistakes.
    /// </summary>
    static string Check(BakedFigure.Clip[] baked, List<(Vector4[] turns, Vector4[] places)> rows, Animator animator,
        Transform root, SkinnedMeshRenderer skin, Mesh bakedMesh, Vector3[] pivots, List<ClipSpec> specs)
    {
        var source = skin.sharedMesh;
        var rest = source.vertices;
        // the body comes first in the baked mesh, at rest in root space
        var restInRoot = bakedMesh.vertices;
        var perVertex = source.GetBonesPerVertex();
        var boneWeights = source.GetAllBoneWeights();
        var posed = new Mesh();
        double sum = 0;
        float worst = 0f;
        int samples = 0;
        try
        {
            for (int c = 0; c < baked.Length; c += Mathf.Max(1, baked.Length / 4))
            {
                var clip = baked[c];
                int frame = clip.frames / 2;
                double time = clip.loop ? clip.length * frame / clip.frames : clip.length * frame / (clip.frames - 1);
                animator.Rebind();
                var graph = PlayableGraph.Create("Figure check");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Figure", animator);
                var playable = AnimationClipPlayable.Create(graph, clip.clip);
                playable.SetApplyFootIK(specs[c].footIK);
                output.SetSourcePlayable(playable);
                playable.SetTime(time);
                playable.SetTime(time);
                Pose(graph, time);
                // without "use scale": with it, a renderer scaled 100 (the zebra's) comes out a hundred times too small
                skin.BakeMesh(posed, false);
                graph.Destroy();
                var real = posed.vertices;
                var row = rows[clip.start - 1 + frame];
                int w = 0;
                for (int i = 0; i < rest.Length; i++)
                {
                    int count = perVertex[i];
                    var a = boneWeights[w];
                    var b = count > 1 ? boneWeights[w + 1] : a;
                    w += count;
                    float total = a.weight + (count > 1 ? b.weight : 0f);
                    var v = restInRoot[i];
                    var mine = Skin(row, pivots, a.boneIndex, v) * (a.weight / total);
                    if (count > 1)
                        mine += Skin(row, pivots, b.boneIndex, v) * (b.weight / total);
                    // BakeMesh gives positions round the renderer, in its rotation
                    var theirs = root.worldToLocalMatrix.MultiplyPoint3x4(skin.transform.position + skin.transform.rotation * real[i]);
                    float error = Vector3.Distance(mine, theirs);
                    sum += error;
                    worst = Mathf.Max(worst, error);
                    samples++;
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(posed);
        }
        return samples > 0 ? $"Check against Unity's skinning: mean {sum / samples * 1000:0.0} mm, worst {worst * 1000:0} mm." : "";
    }

    static Vector3 Skin((Vector4[] turns, Vector4[] places) row, Vector3[] pivots, int bone, Vector3 v)
    {
        var t = row.turns[bone];
        var p = row.places[bone];
        var q = new Quaternion(Half(t.x), Half(t.y), Half(t.z), Half(t.w)).normalized;
        var at = new Vector3(Half(p.x), Half(p.y), Half(p.z));
        var pivot = new Vector3(Half(pivots[bone].x), Half(pivots[bone].y), Half(pivots[bone].z));
        return at + Half(p.w) * (q * (v - pivot));
    }

    static float Half(float value) => Mathf.HalfToFloat(Mathf.FloatToHalf(value));
}
