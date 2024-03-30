using System.Runtime.InteropServices;
using Prepatcher;
using Verse;
namespace CombatAI
{
    public class ITRegionGridPrepatched : ITRegionGrid
    {
        private readonly int        gridId;
        private readonly ISRegion[] cells;
        
        public ITRegionGridPrepatched(Map map, int id) : base(map)
        {
            cells                                                      = new ISRegion[NumGridCells];
            gridId                                                     = id;
            Log.Message($"gridId:{id}");
        }
        public override void Set(int index)
        {
            if (index >= 0 && index < NumGridCells)
            {
                ISRegion regionHolder = cells[index];
                if (regionHolder.region != null)
                {
                    IFieldInfo info = GetInfo(regionHolder.region);
                    if (info.sig != r_sig)
                    {
                        int dc = CycleNum - info.cycle;
                        if (dc == 0)
                        {
                            info.num += 1;
                        }
                        else
                        {
                            bool expired = dc > 1;
                            if (expired)
                            {
                                info.numPrev = 0;
                            }
                            else
                            {
                                info.numPrev = info.num;
                            }
                            info.num = 1;
                        }
                        info.cycle  = CycleNum;
                        info.sig    = r_sig;
                        SetInfo(regionHolder.region, info);
                    }
                }
            }
        }
        public override void SetRegionAt(int index, Region region)
        {
            if (index >= 0 && index < NumGridCells)
            {
                cells[index] = new ISRegion() { region = region };
            }
        }
        public override int  GetSignalNumByRegion(Region region)
        {
            if (region != null)
            {
                IFieldInfo cell = GetInfo(region);
                switch (CycleNum - cell.cycle)
                {
                    case 0:
                        return Maths.Max(cell.num, cell.numPrev);
                    case 1:
                        return cell.num;
                    default:
                        return 0;
                }
            }
            return 0;
        }
        public override int  GetRegionId(int index)
        {
            if (index >= 0 && index < NumGridCells)
            {
                return cells[index].region?.id ?? -1;
            }
            return -1;
        }

        private IFieldInfo GetInfo(Region region)
        {
            return ITRegionGridPrepatchedHelper.CombatAI_RegionFields(region).fields[gridId];
        }
        
        private void SetInfo(Region region, IFieldInfo info)
        {
            ITRegionGridPrepatchedHelper.CombatAI_RegionFields(region).fields[gridId] = info;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ISRegion
        {
            public Region region;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct IFieldInfo
        {
            public short cycle;
            public short sig;
            public short num;
            public short numPrev;
        }
        
        public sealed class IRegionFields
        {
            public IFieldInfo[] fields;
        }
    }

    public static class ITRegionGridPrepatchedHelper
    {
        [PrepatcherField]
        [ValueInitializer(nameof(InitRegionFields))]
        public static extern ref ITRegionGridPrepatched.IRegionFields CombatAI_RegionFields(Region region);

        public static ITRegionGridPrepatched.IRegionFields InitRegionFields(Region region)
        {
            ITRegionGridPrepatched.IRegionFields fields = new ITRegionGridPrepatched.IRegionFields();
            fields.fields = new ITRegionGridPrepatched.IFieldInfo[16];
            return fields;
        }
    }
}
