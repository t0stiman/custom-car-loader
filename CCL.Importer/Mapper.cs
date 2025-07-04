﻿using AutoMapper;
using CCL.Importer.Types;
using CCL.Types;
using DV.ThingTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CCL.Importer
{
    public static class Mapper
    {
        private static readonly List<ICacheConfig> s_configCache = new();
        private static readonly Dictionary<MonoBehaviour, MonoBehaviour> s_componentMapCache = new();
        private static readonly HashSet<MonoBehaviour> s_mapped = new();
        // Mapper needs to be declared after the caches, or it will cause reflection problems,
        // as the config will try to access the caches.
        private static readonly MapperConfiguration _config = new(Configure);

        private static IMapper? _map;
        public static IMapper M => _map ??= _config.CreateMapper();

        // This interface is only used to be able to store the generic type without the generics.
        private interface ICacheConfig
        {
            public void StoreComponentsInChildrenInCache(GameObject prefab);

            public void ConvertFromCache();
        }

        private class CacheConfig<TSource, TDestination> : ICacheConfig
            where TSource : MonoBehaviour
            where TDestination : MonoBehaviour
        {
            private Predicate<TSource>? _shouldMap;
            private TSource[] _sourceComponents = null!;

            public CacheConfig()
            {
                _shouldMap = null;
            }

            public CacheConfig(Predicate<TSource> shouldMap)
            {
                _shouldMap = shouldMap;
            }

            public void StoreComponentsInChildrenInCache(GameObject prefab)
            {
                // Cache the found components to avoid calling this twice.
                _sourceComponents = prefab.GetComponentsInChildren<TSource>(true);

                foreach (var source in _sourceComponents)
                {
                    // If the class inherits from another that is also mapped,
                    // skip it so it doesn't get added to the dictionary twice.
                    if (source.GetType() != typeof(TSource)) continue;

                    // If there is no condition or the condition is true.
                    if (_shouldMap == null || _shouldMap(source))
                    {
                        var destination = source.gameObject.AddComponent<TDestination>();
                        s_componentMapCache.Add(source, destination);
                    }
                }
            }

            public void ConvertFromCache()
            {
                // This is only ever called right after the previous one,
                // so it should NEVER be null.
                foreach (MonoBehaviour source in _sourceComponents)
                {
                    if (!s_componentMapCache.TryGetValue(source, out MonoBehaviour cached) || s_mapped.Contains(cached)) continue;

                    M.Map(source, cached);
                    UnityEngine.Object.Destroy(source);
                    s_mapped.Add(cached);
                }
            }
        }

        /// <summary>
        /// Adds a type map config to be automatically processed.
        /// </summary>
        /// <typeparam name="TSource">The proxy component type.</typeparam>
        /// <typeparam name="TDestination">The real component type.</typeparam>
        internal static void AddConfig<TSource, TDestination>()
            where TSource : MonoBehaviour
            where TDestination : MonoBehaviour
        {
            s_configCache.Add(new CacheConfig<TSource, TDestination>());
        }

        /// <summary>
        /// Adds a type map config to be automatically processed.
        /// </summary>
        /// <typeparam name="TSource">The proxy component type.</typeparam>
        /// <typeparam name="TDestination">The real component type.</typeparam>
        /// <param name="shouldMap">The condition that must be met for the map to be possible.</param>
        internal static void AddConfig<TSource, TDestination>(Predicate<TSource> shouldMap)
            where TSource : MonoBehaviour
            where TDestination : MonoBehaviour
        {
            s_configCache.Add(new CacheConfig<TSource, TDestination>(shouldMap));
        }

        /// <summary>
        /// Process all the proxy maps of a prefab.
        /// </summary>
        internal static void ProcessConfigs(GameObject prefab)
        {
            // First create all the real components from each proxy.
            foreach (var item in s_configCache)
            {
                item.StoreComponentsInChildrenInCache(prefab);
            }

            // Then map all components that were created.
            foreach (var item in s_configCache)
            {
                item.ConvertFromCache();
            }
        }

        private static void Configure(IMapperConfigurationExpression cfg)
        {
            // It's safe to map private fields if they're marked with the Serialized attribute.
            cfg.ShouldMapField = f => AutoMapperHelper.IsPublicOrSerialized(f);

            cfg.CreateMap<CustomCarVariant, CCL_CarVariant>()
                .ForMember(c => c.parentType, o => o.Ignore())
                .ForMember(c => c.AllPrefabs, o => o.Ignore());

            cfg.CreateMap<CustomCarType, CCL_CarType>()
                .ForMember(c => c.carInstanceIdGenBase, o => o.MapFrom(ccl => ccl.carIdPrefix))
                .ForMember(c => c.liveries, o => o.ConvertUsing(new LiveriesConverter()))
                .ForMember(c => c.rollingResistanceMultiplier, o => o.MapFrom(ccl => ccl.rollingResistanceCoefficient / CustomCarType.ROLLING_RESISTANCE_COEFFICIENT))
                .ForMember(c => c.wheelSlideFrictionMultiplier, o => o.MapFrom(ccl => ccl.wheelSlidingFrictionCoefficient / CustomCarType.WHEELSLIDE_FRICTION_COEFFICIENT))
                .ForMember(c => c.wheelslipFrictionMultiplier, o => o.MapFrom(ccl => ccl.wheelslipFrictionCoefficient / CustomCarType.WHEELSLIP_FRICTION_COEFFICIENT))
                .ForMember(c => c.audioPoolSize, o => o.MapFrom(ccl => Utilities.GetAudioPoolSize(ccl.KindSelection)));

            cfg.CreateMap<CustomCarType.BrakesSetup, TrainCarType_v2.BrakesSetup>()
                .ForMember(b => b.trainBrake, o => o.MapFrom(s => s.brakeValveType))
                .AfterMap(BrakesAfter);
            cfg.CreateMap<CustomCarType.DamageSetup, TrainCarType_v2.DamageSetup>();
            cfg.AddMaps(Assembly.GetExecutingAssembly());
        }

        private static void BrakesAfter(CustomCarType.BrakesSetup proxy, TrainCarType_v2.BrakesSetup setup)
        {
            switch (proxy.TrainBrakeCurveType)
            {
                case CustomCarType.BrakesSetup.BrakeCurveType.LocoDefault:
                    setup.trainBrakeCurveData = QuickAccess.Locomotives.DE2.parentType.brakes.trainBrakeCurveData;
                    break;
                case CustomCarType.BrakesSetup.BrakeCurveType.Custom:
                    setup.trainBrakeCurveData = ScriptableObject.CreateInstance<BrakesCurve>();
                    setup.trainBrakeCurveData.brakesCurve = proxy.TrainBrakeCurve;
                    break;
                default:
                    break;
            }

            switch (proxy.IndBrakeCurveType)
            {
                case CustomCarType.BrakesSetup.BrakeCurveType.LocoDefault:
                    setup.indBrakeCurveData = QuickAccess.Locomotives.DE2.parentType.brakes.indBrakeCurveData;
                    break;
                case CustomCarType.BrakesSetup.BrakeCurveType.Custom:
                    setup.indBrakeCurveData = ScriptableObject.CreateInstance<BrakesCurve>();
                    setup.indBrakeCurveData.brakesCurve = proxy.IndBrakeCurve;
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Replaces TSource with TDestination using AutoMapper
        /// 
        /// Unlike MapComponentsInChildren this will only map one and targets a *source* instead of a *prefab*
        /// </summary>
        /// <typeparam name="TSource">Type of the component being replaced</typeparam>
        /// <typeparam name="TDestination">Type of component to replace with</typeparam>
        /// <param name="source">Component being replaced, will be destroyed before this returns</param>
        /// <returns>The replaced component which can be used in other mappers</returns>
        public static TDestination MapComponent<TSource, TDestination>(TSource source)
            where TSource : MonoBehaviour
            where TDestination: MonoBehaviour
        {
            var destination = source.gameObject.AddComponent<TDestination>();
            s_componentMapCache.Add(source, destination);
            M.Map(source, destination);
            UnityEngine.Object.Destroy(source);
            return destination;
        }

        /// <summary>
        /// Replaces TSource with TDestination using AutoMapper
        /// 
        /// Unlike MapComponentsInChildren this will only map one and targets a *source* instead of a *prefab*
        /// </summary>
        /// <typeparam name="TSource">Type of the component being replaced</typeparam>
        /// <typeparam name="TDestination">Type of component to replace with</typeparam>
        /// <param name="source">Component being replaced, will be destroyed before this returns</param>
        /// <param name="destination">The component added to replace the original</param>
        public static void MapComponent<TSource, TDestination>(TSource source, out TDestination destination)
            where TSource : MonoBehaviour
            where TDestination : MonoBehaviour
        {
            destination = MapComponent<TSource, TDestination>(source);
        }

        /// <summary>
        /// Gets the mapped version of a <see cref="MonoBehaviour"/> if one has been cached.
        /// </summary>
        /// <param name="source">The source (usually proxy) component.</param>
        /// <returns>The mapped <see cref="MonoBehaviour"/>. If there is no mapped version, <c>null</c>.</returns>
        internal static MonoBehaviour GetFromCache(MonoBehaviour source)
        {
            s_componentMapCache.TryGetValue(source, out MonoBehaviour output);
            return output;
        }

        /// <summary>
        /// Gets an enumerable in which each <see cref="MonoBehaviour"/> is its mapped version if one has been cached.
        /// </summary>
        /// <param name="source">The enumerable of source (usually proxies) components.</param>
        /// <returns>The enumerable of <see cref="MonoBehaviour"/>s. If there is no mapped version, it may contain <c>null</c> values.</returns>
        internal static IEnumerable<MonoBehaviour> GetFromCache(IEnumerable<MonoBehaviour> source)
        {
            return source.Select(scr => GetFromCache(scr));
        }

        /// <summary>
        /// Gets the mapped version of a <see cref="MonoBehaviour"/> if one has been cached.
        /// </summary>
        /// <param name="source">The source (usually proxy) component.</param>
        /// <returns>The mapped <see cref="MonoBehaviour"/>. If there is no mapped version, it will return instead <paramref name="source"/>.</returns>
        internal static MonoBehaviour GetFromCacheOrSelf(MonoBehaviour source)
        {
            s_componentMapCache.TryGetValue(source, out MonoBehaviour output);
            return output ?? source;
        }

        /// <summary>
        /// Gets an enumerable in which each <see cref="MonoBehaviour"/> is its mapped version if one has been cached.
        /// </summary>
        /// <param name="source">The enumerable of source (usually proxies) components.</param>
        /// <returns>
        /// A collection of <see cref="MonoBehaviour"/>. If there is no mapped version for a given one, it will be the original <see cref="MonoBehaviour"/>
        /// that was in <paramref name="source"/>.
        /// </returns>
        internal static IEnumerable<MonoBehaviour> GetFromCacheOrSelf(IEnumerable<MonoBehaviour> source)
        {
            return source.Select(scr => GetFromCacheOrSelf(scr));
        }

        public static void ClearComponentCache()
        {
            s_componentMapCache.Clear();
            s_mapped.Clear();
        }

        private class LiveriesConverter : IValueConverter<List<CustomCarVariant>, List<TrainCarLivery>>
        {
            public List<TrainCarLivery> Convert(List<CustomCarVariant> sourceMember, ResolutionContext context)
            {
                return sourceMember.Select(v =>
                {
                    var l = ScriptableObject.CreateInstance<CCL_CarVariant>();
                    M.Map(v, l);
                    return l;
                }).Cast<TrainCarLivery>().ToList();
            }
        }
    }
}
