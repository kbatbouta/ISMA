using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;
namespace CombatAI
{
    public static class TKVCache<T, K, V>
    {
        private static readonly        CachedDict<int, V>             dict_indexed;
        private static readonly        CachedDict<T, V>               dict;
        private static readonly unsafe delegate*<T, out V, int, bool> getter;
        private static readonly unsafe delegate*<T, V, void>          setter;

        static unsafe TKVCache()
        {
	        if (TCacheHelper.IndexGetter<T>.indexable)
	        {
		        if (Finder.Settings.Debug)
		        {
			        Log.Message($"ISMA: {typeof(T)} using indexed cached!");
		        }
		        dict_indexed = new CachedDict<int, V>(128);
		        TCacheHelper.clearFuncs.Add(dict_indexed.Clear);
		        getter = (delegate*<T, out V, int, bool>) typeof(TKVCache<T, K, V>).GetMethod("TryGet_Indexed", AccessTools.all).MethodHandle.GetFunctionPointer();
		        setter = (delegate*<T, V, void>)typeof(TKVCache<T, K, V>).GetMethod("Put_Indexed", AccessTools.all).MethodHandle.GetFunctionPointer();
	        }
	        else
	        {
		        dict = new CachedDict<T, V>(128);
		        TCacheHelper.clearFuncs.Add(dict.Clear);
		        getter = (delegate*<T, out V, int, bool>) typeof(TKVCache<T, K, V>).GetMethod("TryGet_Default", AccessTools.all).MethodHandle.GetFunctionPointer();
		        setter = (delegate*<T, V, void>)typeof(TKVCache<T, K, V>).GetMethod("Put_Default", AccessTools.all).MethodHandle.GetFunctionPointer();
	        }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Clear()
        {
	        if (TCacheHelper.IndexGetter<T>.indexable)
	        {
		        dict_indexed.Clear();
	        }
	        else
	        {
		        dict.Clear();
	        }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(T key, out V val, int expiry = -1)
        {
	        unsafe
	        {
		        return getter(key, out val, expiry);
	        }   
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Put(T key, V value)
        {
	        unsafe
	        {
		        setter(key, value);
	        }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool TryGet_Indexed(T key, out V val, int expiry)
        {
	        return dict_indexed.TryGetValue(TCacheHelper.IndexGetter<T>.Default(key), out val, expiry);
        }
	        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool TryGet_Default(T key, out V val, int expiry)
        {
	        return dict.TryGetValue(key, out val, expiry);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void Put_Indexed(T key, V val)
        {
	        dict_indexed[TCacheHelper.IndexGetter<T>.Default(key)] = val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void Put_Default(T key, V val)
        {
	        dict[key] = val;
        }
    }
}
