// Skinning for battle figures baked by FigureBaker (see BakedFigure.cs for the texture's layout).
// Called from the Baked Figure shader graph's vertex stage (a Custom Function node, function BakedSkin).
//
// Each vertex has two bones (UV3: indices in xy, weights in zw). Each bone's pose is blended from up to four
// samples of the pose texture: locomotion A and B, then action slots 1 and 2.
//   Loco = (row A, row B, B's share, -)
//   ActN = (row, whole-body weight, upper-body weight, -)
//   ReNQ, ReNT = slot N's re-base (rotation, offset) onto the walking hips, for upper-body bones
// Rows are fractional: the texture is sampled between two rows with bilinear filtering, which blends the frames.
// A slot with no weight is skipped (a uniform branch), so most frames read only one or two samples.

#ifndef BAKED_SKINNING_INCLUDED
#define BAKED_SKINNING_INCLUDED

float4 BakedTexel(UnityTexture2D poses, float column, float row)
{
    float2 uv = float2((column + 0.5) * poses.texelSize.x, (row + 0.5) * poses.texelSize.y);
    return SAMPLE_TEXTURE2D_LOD(poses.tex, poses.samplerstate, uv, 0);
}

float3 BakedRotate(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

float4 BakedMultiply(float4 a, float4 b)
{
    return float4(a.w * b.xyz + b.w * a.xyz + cross(a.xyz, b.xyz), a.w * b.w - dot(a.xyz, b.xyz));
}

// adds an action slot's pose of a bone into the running sum, re-based onto the walking hips for its upper-body share
void BakedAddAction(UnityTexture2D poses, float column, float upperFlag, float4 act, float4 reQ, float4 reT,
    float4 reference, inout float4 q, inout float4 ps)
{
    float w = act.y + act.z * upperFlag;
    float4 sq = BakedTexel(poses, column, act.x);
    float4 sp = BakedTexel(poses, column + 1.0, act.x);
    if (upperFlag > 0.5 && act.z > 0.0)
    {
        float share = act.z / (act.y + act.z);
        float4 r = normalize(lerp(float4(0.0, 0.0, 0.0, 1.0), reQ, share));
        sp.xyz = reT.xyz * share + BakedRotate(r, sp.xyz);
        sq = BakedMultiply(r, sq);
    }
    sq = dot(reference, sq) < 0.0 ? -sq : sq;
    q += sq * w;
    ps += sp * w;
}

// one bone's blended pose: rotation from bind (q), position and scale (ps), and its bind position (pivot)
void BakedBone(UnityTexture2D poses, float bone, float4 loco, float4 act1, float4 act2,
    float4 re1Q, float4 re1T, float4 re2Q, float4 re2T, out float4 q, out float4 ps, out float3 pivot)
{
    float column = bone * 2.0;
    float4 header = BakedTexel(poses, column, 0.0);
    pivot = header.xyz;
    float upperFlag = header.w;

    float4 lq = BakedTexel(poses, column, loco.x);
    float4 lp = BakedTexel(poses, column + 1.0, loco.x);
    [branch] if (loco.z > 0.0)
    {
        float4 bq = BakedTexel(poses, column, loco.y);
        float4 bp = BakedTexel(poses, column + 1.0, loco.y);
        bq = dot(lq, bq) < 0.0 ? -bq : bq;
        lq = lerp(lq, bq, loco.z);
        lp = lerp(lp, bp, loco.z);
    }

    float w1 = act1.y + act1.z * upperFlag;
    float w2 = act2.y + act2.z * upperFlag;
    float rest = saturate(1.0 - w1 - w2);
    q = lq * rest;
    ps = lp * rest;
    [branch] if (act1.y + act1.z > 0.0)
        BakedAddAction(poses, column, upperFlag, act1, re1Q, re1T, lq, q, ps);
    [branch] if (act2.y + act2.z > 0.0)
        BakedAddAction(poses, column, upperFlag, act2, re2Q, re2T, lq, q, ps);
    q = normalize(q);
}

void BakedSkin_float(float3 PositionOS, float3 NormalOS, float4 Bones, UnityTexture2D Poses, float4 Loco,
    float4 Act1, float4 Act2, float4 Re1Q, float4 Re1T, float4 Re2Q, float4 Re2T,
    out float3 Position, out float3 Normal)
{
    float4 q;
    float4 ps;
    float3 pivot;
    BakedBone(Poses, Bones.x, Loco, Act1, Act2, Re1Q, Re1T, Re2Q, Re2T, q, ps, pivot);
    float3 position = ps.xyz + ps.w * BakedRotate(q, PositionOS - pivot);
    float3 normal = BakedRotate(q, NormalOS);
    [branch] if (Bones.w > 0.001)
    {
        BakedBone(Poses, Bones.y, Loco, Act1, Act2, Re1Q, Re1T, Re2Q, Re2T, q, ps, pivot);
        position = position * Bones.z + (ps.xyz + ps.w * BakedRotate(q, PositionOS - pivot)) * Bones.w;
        normal = normal * Bones.z + BakedRotate(q, NormalOS) * Bones.w;
    }
    Position = position;
    Normal = normalize(normal);
}

void BakedSkin_half(float3 PositionOS, float3 NormalOS, float4 Bones, UnityTexture2D Poses, float4 Loco,
    float4 Act1, float4 Act2, float4 Re1Q, float4 Re1T, float4 Re2Q, float4 Re2T,
    out float3 Position, out float3 Normal)
{
    BakedSkin_float(PositionOS, NormalOS, Bones, Poses, Loco, Act1, Act2, Re1Q, Re1T, Re2Q, Re2T, Position, Normal);
}

#endif
