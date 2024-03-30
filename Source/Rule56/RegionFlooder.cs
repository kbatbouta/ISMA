using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
namespace CombatAI
{
    public class RegionFlooder
    {
        private static readonly FastHeap<Node> queue   = new FastHeap<Node>(128);
        private static readonly HashSet<int>   flooded = new HashSet<int>(256);

        public static void Flood(IntVec3 root, IntVec3 target, Map map, Func<Region, int, int, bool> action, Func<Region, bool> validator = null, Func<Region, float> cost = null, TraverseParms? traverseParms = null, int minRegions = 0, int maxRegions = 9999, float maxDist = 9999f, bool depthCost = true, float depthCostMul = 12)
        {
            Region rootRegion = root.GetRegion(map);
            if (rootRegion == null || validator != null && !validator(rootRegion))
            {
                return;
            }
            bool               targetIsValid     = target.IsValid;
            Func<Region, bool> traverseValidator = GetTraverseValidator(traverseParms);
            int                num               = 1;
            queue.Clear();
            queue.Enqueue(new Node
            {
                region = rootRegion,
                score  = targetIsValid ? Maths.Sqrt_Fast(target.DistanceToSquared(rootRegion.extentsClose.Max), 3) : 0,
            });
            flooded.Clear();
            flooded.Add(rootRegion.id);
            while (!queue.IsEmpty && (num < minRegions || num <= maxRegions))
            {
                Node   node    = queue.Dequeue();
                Region current = node.region;
                if (action(current, Mathf.CeilToInt(node.score / 12f), node.depth))
                {
                    break;
                }
                num++;
                List<RegionLink> links = current.links;
                for (int li = 0; li < links.Count; li++)
                {
                    RegionLink link = links[li];
                    for (int ri = 0; ri < 2; ri++)
                    {
                        Region next = link.regions[ri];
                        if (next != null && next != current && !flooded.Contains(next.id) && next.valid)
                        {
                            flooded.Add(next.id);
                            if ((validator == null || validator(next)) && (traverseValidator == null || traverseValidator(next)))
                            {
                                if (targetIsValid)
                                {
                                    float distToTarget = Maths.Sqrt_Fast(target.DistanceToSquared(next.extentsClose.Max), 3);
                                    if (distToTarget < maxDist)
                                    {
                                        queue.Enqueue(new Node
                                        {
                                            region = next,
                                            score  = distToTarget + (!depthCost ? 0 : (node.depth + 1) * depthCostMul) + (cost?.Invoke(next) ?? 0),
                                            depth  = node.depth + 1
                                        });
                                    }
                                }
                                else
                                {
                                    queue.Enqueue(new Node
                                    {
                                        region = next,
                                        score  = (!depthCost ? 0 : (node.depth + 1) * depthCostMul) + (cost?.Invoke(next) ?? 0),
                                        depth  = node.depth + 1
                                    });
                                }
                            }
                        }
                    }
                }
            }
            queue.Clear();
            flooded.Clear();
        }

        private static Func<Region, bool> GetTraverseValidator(TraverseParms? traverseParms)
        {
            if (traverseParms != null)
            {
                TraverseParms            traverse = traverseParms.Value;
                if (traverse.mode == TraverseMode.PassAllDestroyableThings || traverse.mode == TraverseMode.PassAllDestroyableThingsNotWater)
                {
                    traverse.mode = TraverseMode.PassDoors;
                }
                Pawn                     pawn     = traverse.pawn;
                SightTracker.SightReader sight    = null;
                if (pawn != null && traverse.maxDanger != Danger.Unspecified && traverse.maxDanger != Danger.Deadly)
                {
                    pawn.TryGetSightReader(out sight);
                }
                return region =>
                {
                    if (!traverse.canBashDoors && region.IsDoorway)
                    {
                        return false;
                    }
                    if (sight != null)
                    {
                        int visibility = sight.GetRegionAbsVisibilityToEnemies(region);
                        if (traverse.maxDanger == Danger.None && visibility > 0)
                        {
                            return false;
                        }
                        if (traverse.maxDanger == Danger.Some && visibility > 2)
                        {
                            return false;
                        }
                    }
                    return region.type.Passable();
                };
            }
            return null;
        }

        private struct Node : IComparable<Node>
        {
            public Region region;
            public float  score;
            public int    depth;

            public int CompareTo(Node other)
            {
                return score.CompareTo(other.score) * -1;
            }
        }
    }
}
