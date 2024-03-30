using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Assertions;
using Verse;
namespace CombatAI
{
	[StaticConstructorOnStartup]
	public class MapComponent_FogGrid : MapComponent
	{
		private const int SECTION_SIZE = 16;

		private static          float     zoom;
		private static readonly Texture2D fogTex;
		private static readonly Mesh      mesh;
		private static readonly Shader    fogShader;

		private readonly object        _lockerSpots = new object();
		private readonly AsyncActions  asyncActions;
		private readonly ISection[][]  grid2d;
		private readonly Rect          mapRect;
		private readonly List<Vector3> spotBuffer;

		private readonly HashSet<ITempSpot> spotsQueued;
		private          bool               alive;
		public           CellIndices        cellIndices;
		public           GlowGrid           glow;

		public  bool[] grid;
		private bool   initialized;

		private          Rect        mapScreenRect;
		private          bool        ready;
		public           SightGrid[] sight;
		private volatile int         ticksGame;
		private          int         updateNum;
		public           WallGrid    walls;


		static MapComponent_FogGrid()
		{
			fogTex = new Texture2D(SECTION_SIZE, SECTION_SIZE, TextureFormat.RGBAFloat, true);
			fogTex.Apply();
			mesh      = CombatAI_MeshMaker.NewPlaneMesh(Vector2.zero, Vector2.one * SECTION_SIZE);
			fogShader = AssetBundleDatabase.Get<Shader>("assets/fogshader.shader");
			Assert.IsNotNull(fogShader);
		}

		public MapComponent_FogGrid(Map map) : base(map)
		{
			alive        = true;
			spotsQueued  = new HashSet<ITempSpot>(16);
			spotBuffer   = new List<Vector3>(256);
			asyncActions = new AsyncActions();
			cellIndices  = map.cellIndices;
			mapRect      = new Rect(0, 0, cellIndices.mapSizeX, cellIndices.mapSizeZ);
			grid         = new bool[map.cellIndices.NumGridCells];
			grid2d       = new ISection[Mathf.CeilToInt(cellIndices.mapSizeX / (float)SECTION_SIZE)][];
		}

		public float SkyGlow
		{
			get;
			private set;
		}

		public override void FinalizeInit()
		{
			base.FinalizeInit();
			asyncActions.Start();
		}

		public bool IsFogged(IntVec3 cell)
		{
			return IsFogged(cellIndices.CellToIndex(cell));
		}
		public bool IsFogged(int index)
		{
			if (!Finder.Settings.FogOfWar_Enabled)
			{
				return false;
			}
			if (index >= 0 && index < cellIndices.NumGridCells)
			{
				return grid[index];
			}
			return false;
		}

		public override void MapComponentUpdate()
		{
			if (!initialized && Finder.Settings.FogOfWar_Enabled)
			{
				initialized = true;
				for (int i = 0; i < grid2d.Length; i++)
				{
					grid2d[i] = new ISection[Mathf.CeilToInt(cellIndices.mapSizeZ / (float)SECTION_SIZE)];
					for (int j = 0; j < grid2d[i].Length; j++)
					{
						grid2d[i][j] = new ISection(this, new Rect(new Vector2(i * SECTION_SIZE, j * SECTION_SIZE), Vector2.one * SECTION_SIZE), mapRect, mesh, fogTex, fogShader);
					}
				}
				asyncActions.EnqueueOffThreadAction(() =>
				{
					OffThreadLoop(0, 0, grid2d.Length, grid2d[0].Length);
				});
			}
			if (!alive || !Finder.Settings.FogOfWar_Enabled)
			{
				return;
			}
			if (Find.CurrentMap != map)
			{
				ready = false;
				return;
			}
			ticksGame = GenTicks.TicksGame;
			if (!ready)
			{
				sight = map.GetComp_Fast<SightTracker>().colonistsAndFriendlies;
				walls = map.GetComp_Fast<WallGrid>();
				glow  = map.glowGrid;
				ready = sight != null;
			}
			base.MapComponentUpdate();
			if (ready)
			{
				SkyGlow = map.skyManager.CurSkyGlow;
				Rect     rect     = new Rect();
				CellRect cellRect = Find.CameraDriver.CurrentViewRect;
				rect.xMin     = Mathf.Clamp(cellRect.minX - SECTION_SIZE, 0, cellIndices.mapSizeX);
				rect.xMax     = Mathf.Clamp(cellRect.maxX + SECTION_SIZE, 0, cellIndices.mapSizeX);
				rect.yMin     = Mathf.Clamp(cellRect.minZ - SECTION_SIZE, 0, cellIndices.mapSizeZ);
				rect.yMax     = Mathf.Clamp(cellRect.maxZ + SECTION_SIZE, 0, cellIndices.mapSizeZ);
				mapScreenRect = rect;
				asyncActions.ExecuteMainThreadActions();
				zoom = Mathf.CeilToInt(Mathf.Clamp(Find.CameraDriver?.rootPos.y ?? 30, 15, 30f));
				DrawFog(Mathf.FloorToInt(mapScreenRect.xMin / SECTION_SIZE), Mathf.FloorToInt(mapScreenRect.yMin / SECTION_SIZE), Mathf.FloorToInt(mapScreenRect.xMax / SECTION_SIZE), Mathf.FloorToInt(mapScreenRect.yMax / SECTION_SIZE));
			}
		}

		public void RevealSpot(IntVec3 cell, float radius, int duration)
		{
			ITempSpot spot = new ITempSpot();
			spot.center   = cell;
			spot.radius   = Maths.Max(radius, 1f) * Finder.Settings.FogOfWar_RangeMultiplier;
			spot.duration = duration;
			lock (_lockerSpots)
			{
				spotsQueued.Add(spot);
			}
		}

		public override void MapRemoved()
		{
			alive = false;
			asyncActions.Kill();
			base.MapRemoved();
		}

		private void DrawFog(int minU, int minV, int maxU, int maxV)
		{
			maxU = Mathf.Clamp(Maths.Max(maxU, minU + 1), 0, grid2d.Length - 1);
			minU = Mathf.Clamp(minU - 1, 0, grid2d.Length - 1);
			maxV = Mathf.Clamp(Maths.Max(maxV, minV + 1), 0, grid2d[0].Length - 1);
			minV = Mathf.Clamp(minV - 1, 0, grid2d[0].Length - 1);
			bool  update       = updateNum % 2 == 0;
			bool  updateForced = updateNum % 4 == 0;
			float color        = Finder.Settings.FogOfWar_FogColor;
			for (int u = minU; u <= maxU; u++)
			{
				for (int v = minV; v <= maxV; v++)
				{
					ISection section = grid2d[u][v];
					if (section.s_color != color)
					{
						section.ApplyColor();
					}
					if (updateForced || update && section.dirty)
					{
						section.ApplyFogged();
					}
					section.Draw(mapScreenRect);
				}
			}
			updateNum++;
		}

		private void OffThreadLoop(int minU, int minV, int maxU, int maxV)
		{
			Stopwatch       stopwatch = new Stopwatch();
			List<ITempSpot> spots     = new List<ITempSpot>();
			while (alive)
			{
				if (ready && Finder.Settings.FogOfWar_Enabled)
				{
					stopwatch.Restart();
					lock (_lockerSpots)
					{
						if (spotsQueued.Count > 0)
						{
							spots.AddRange(spotsQueued);
							spotsQueued.Clear();
						}
					}
					if (spots.Count > 0)
					{
						int ticks = ticksGame;
						while (spots.Count > 0)
						{
							ITempSpot spot = spots.Pop();
							Action<IntVec3, int, int, float> setAction = (cell, carry, dist, coverRating) =>
							{
								if (cell.InBounds(map))
								{
									int      u       = cell.x / SECTION_SIZE;
									int      v       = cell.z / SECTION_SIZE;
									ISection section = grid2d[u][v];
									if (section != null)
									{
										ITempCell tCell = new ITempCell();
										tCell.u         = (byte)(cell.x % SECTION_SIZE);
										tCell.v         = (byte)(cell.z % SECTION_SIZE);
										tCell.val       = Mathf.Clamp01(1f - cell.DistanceTo_Fast(spot.center) / spot.radius);
										tCell.timestamp = ticks;
										tCell.duration  = (short)spot.duration;
										section.extraCells.Add(tCell);
									}
								}
							};
							setAction(spot.center, 0, 0, 0);
							ShadowCastingUtility.CastWeighted(map, spot.center, setAction, Mathf.CeilToInt(spot.radius), 16, spotBuffer);
						}
					}
					for (int u = minU; u < maxU; u++)
					{
						for (int v = minV; v < maxV; v++)
						{
							grid2d[u][v].Update();
						}
					}
					stopwatch.Stop();
					float t = 0.016f - (float)stopwatch.ElapsedTicks / Stopwatch.Frequency;
					if (t > 0f)
					{
						Thread.Sleep(Mathf.CeilToInt(t * 1000));
					}
				}
				else
				{
					Thread.Sleep(100);
				}
			}
		}

		private class ISection
		{
			public readonly  float[]              cells;
			private readonly MapComponent_FogGrid comp;
			public readonly  List<ITempCell>      extraCells;
			private readonly Material             mat;
			private readonly Mesh                 mesh;
			private readonly Vector3              pos;
			public           bool                 dirty = true;
			public           Rect                 rect;
			public           float                s_color;

			public ISection(MapComponent_FogGrid comp, Rect rect, Rect mapRect, Mesh mesh, Texture2D tex, Shader shader)
			{
				extraCells = new List<ITempCell>();
				this.comp  = comp;
				this.rect  = rect;
				this.mesh  = mesh;
				pos        = new Vector3(rect.position.x, AltitudeLayer.MapDataOverlay.AltitudeFor() , rect.position.y);
				mat        = new Material(shader);
				mat.SetVector("_Color", new Vector4(0.1f, 0.1f, 0.1f, 0.8f));
				mat.SetTexture("_Tex", tex);
				cells = new float[256];
				cells.Initialize();
				mat.SetFloatArray("_Fog", cells);
			}

			public void ApplyColor()
			{
				mat.SetVector("_Color", new Vector4(0.1f, 0.1f, 0.1f, s_color = Finder.Settings.FogOfWar_FogColor));
			}

			public void ApplyFogged()
			{
				mat.SetFloatArray("_Fog", cells);
				dirty = false;
			}

			public void Update()
			{
				CellIndices indices = comp.map?.cellIndices;
				if (indices == null)
				{
					return;
				}
				int         numGridCells = indices.NumGridCells;
				WallGrid    walls        = comp.walls;
				IntVec3     pos          = this.pos.ToIntVec3();
				IntVec3     loc;

				ColorInt[] glowGrid = comp.glow.glowGrid;
				float      glowSky  = comp.SkyGlow;
				bool       changed  = false;

				void SetCell(int x, int z, float glowOffset, float visibilityOffset, bool allowLowerValues)
				{
					int index = indices.CellToIndex(loc = pos + new IntVec3(x, 0, z));
					if (index >= 0 && index < numGridCells)
					{
						float old           = cells[x * SECTION_SIZE + z];
						bool  isWall        = !walls.CanBeSeenOver(index);
						float visRLimit     = 0;
						float visibility    = 0;
						for (int j = 0; j < comp.sight.Length; j++)
						{
							visibility = Maths.Max(comp.sight[j].gridFog.Get(index), visibility);
						}
						float visibilityAdj = 0;
						for (int i = 0; i < 9; i++)
						{
							int adjIndex = index + indices.mapSizeX * (i / 3 - 1) + i % 3 - 1;
							if (adjIndex >= 0 && adjIndex < numGridCells && (isWall || walls.CanBeSeenOver(adjIndex)))
							{
								float adj = 0;
								for (int j = 0; j < comp.sight.Length; j++)
								{
									adj = Maths.Max(adj, comp.sight[j].gridFog.Get(adjIndex));
								}
								visibilityAdj += adj;
							}
						}
						visibility = Maths.Max(visibilityAdj / 9, visibility) + visibilityOffset;
						if (glowSky < 1)
						{
							ColorInt glow = glowGrid[index];
							visRLimit = Mathf.Lerp(0, 0.5f, 1 - (Maths.Max(!isWall ? 1f : Mathf.Clamp01(Maths.Max(glow.r, glow.g, glow.b) / 255f * 3.6f), glowSky) + glowOffset));
						}
						float val = Maths.Max(1 - visibility, 0);
						if (allowLowerValues || old >= val)
						{
							if (visibility <= visRLimit + 1e-3f)
							{
								comp.grid[index] = true;
							}
							else
							{
								comp.grid[index] = false;
							}
						}
						if (old != val)
						{
							changed = true;
							if (allowLowerValues)
							{
								if (val > old)
								{
									cells[x * SECTION_SIZE + z] = Maths.Min(old + 0.5f, val);
								}
								else
								{
									cells[x * SECTION_SIZE + z] = Maths.Max(old - 0.5f, val);
								}
							}
							else
							{
								cells[x * SECTION_SIZE + z] = Maths.Min(old, val);
							}
						}
					}
					else
					{
						cells[x * SECTION_SIZE + z] = 0f;
					}
				}
				if (comp.sight != null)
				{
					for (int x = 0; x < SECTION_SIZE; x++)
					{
						for (int z = 0; z < SECTION_SIZE; z++)
						{
							SetCell(x, z, 0, 0, true);
						}
					}
					int ticks = comp.ticksGame;
					int i     = 0;
					/*
					TODO fix this
					while (i < extraCells.Count)
					{
						ITempCell tCell = extraCells[i];
						if (tCell.timestamp + tCell.duration < ticks)
						{
							extraCells.RemoveAt(i);
							changed = true;
							continue;
						}
						i++;
						float fade = Maths.Max(cells[tCell.u * SECTION_SIZE + tCell.v], Mathf.Lerp(0.7f, 1.0f, 1f - (float)(GenTicks.TicksGame - tCell.timestamp) / tCell.duration));
						SetCell(tCell.u, tCell.v, 0.5f * fade * tCell.val, fade * tCell.val, false);
					}*/
				}
				dirty = changed;
			}

			public void Draw(Rect screenRect)
			{
				GenDraw.DrawMeshNowOrLater(mesh, pos, Quaternion.identity, mat, false);
			}
		}

		private struct ITempSpot : IEquatable<ITempSpot>
		{
			public IntVec3 center;
			public float   radius;
			public int     duration;

			public override bool Equals(object obj)
			{
				return obj is ITempSpot other && other.Equals(this);
			}

			public bool Equals(ITempSpot other)
			{
				return center == other.center;
			}

			public override int GetHashCode()
			{
				return center.GetHashCode();
			}
		}

		private struct ITempCell
		{
			public byte  u;
			public byte  v;
			public float val;
			public int   timestamp;
			public short duration;
		}
	}
}
